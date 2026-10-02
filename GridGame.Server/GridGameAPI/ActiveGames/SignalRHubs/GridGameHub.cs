using GridGameAPI.Database;
using GridGameAPI.Model;
using GridGameAPI.UtilityInfrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace GridGameAPI.ActiveGames.SignalRHubs
{
    /// <summary>
    /// Live play for one game per connection.
    ///
    /// Every state push carries previousStateHash/stateHash (see StateHash) so a client that missed or misapplied a
    /// push notices on the next one and pulls a fresh snapshot via GetGameState. That alone can't catch a dropped push
    /// that was the LAST one before it became someone's turn - nothing follows it until they move, and they don't know
    /// to. So turn-ending pushes are flagged requiresAck, every seated connected user must answer with
    /// AcknowledgeState, and SessionMonitor re-prompts anyone who doesn't, eventually flagging them as unresponsive to
    /// the other players.
    /// </summary>
    [Authorize(Policy = AuthPolicy.AnyUser)]
    public class GridGameHub(GameSessionManager _sessionManager, GameContext _gameContext) : Hub
    {
        /// <returns>The player number the caller owns tiles as, or 0 if the game's seats are already full and they're only spectating</returns>
        public async Task<int> OnConnectedToGame(int gameId)
        {
            var userId = Context.UserIdentifier!;
            var session = _sessionManager.Join(gameId, Context.ConnectionId,
                _ =>
                {
                    var game = _gameContext.GridGames
                        .AsNoTracking()
                        .Single(game => game.Id == gameId);
                    return new GameSession
                    {
                        GridGame = game,
                        StateHash = StateHash.Compute(game),
                        Analysis = BoardAnalysis.Compute(game.GameBoard, game.PlayerCount),
                    };
                },
                session =>
                {
                    session.Connections[Context.ConnectionId] = userId;
                    if (Context.User?.FindFirst(Constants.AccountTypeClaim)?.Value == Constants.NamedAccountType)
                    {
                        session.NamedAccountConnections[Context.ConnectionId] = 0;
                    }
                    session.UserNames[userId] = Context.User?.FindFirst(ClaimTypes.Name)?.Value ?? "";
                    if (!session.PlayerNumbers.ContainsKey(userId) &&
                        session.PlayerNumbers.Count < session.GridGame.PlayerCount)
                    {
                        session.PlayerNumbers[userId] = session.PlayerNumbers.Count + 1;
                    }
                    //A fresh connection always pulls a snapshot (see the client's resync), so whatever they missed
                    //while unresponsive no longer matters
                    session.UnresponsiveUsers.TryRemove(userId, out _);
                });

            await BroadcastPresence(session);
            return session.PlayerNumbers.GetValueOrDefault(userId, 0);
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var session = _sessionManager.Leave(Context.ConnectionId);
            if (session is null)
            {
                //Disconnected (or was never fully connected) before ever calling OnConnectedToGame - nothing to clean up
                return;
            }

            var userId = Context.UserIdentifier!;
            if (!session.ConnectionsFor(userId).Any())
            {
                //Presence now reports them as disconnected, which supersedes waiting on their ack
                session.PendingAcks.TryRemove(userId, out _);
                session.UnresponsiveUsers.TryRemove(userId, out _);
            }

            if (!session.Connections.IsEmpty)
            {
                await BroadcastPresence(session);
                return;
            }

            //Persisted BEFORE the session is dropped, and dropped only if nobody rejoined meanwhile - otherwise a quick
            //rejoin (a page refresh, a phone waking up) could load the game from a database that doesn't have the
            //latest moves yet, and that stale copy would then become the live one
            await session.MoveLock.WaitAsync();
            try
            {
                await PersistGame(session.GridGame);
                _sessionManager.TryClose(session);
            }
            finally
            {
                session.MoveLock.Release();
            }
        }

        /// <summary>Full snapshot of the live game, taken under the move lock so it's never half-way through a move.
        /// Clients call this on every (re)connect and whenever a push's hashes show they've fallen out of step</summary>
        public async Task<object?> GetGameState()
        {
            var session = _sessionManager.GetByConnectionId(Context.ConnectionId);
            if (session is null)
            {
                return null;
            }

            await session.MoveLock.WaitAsync();
            try
            {
                var game = session.GridGame;
                return new
                {
                    gameBoard = BoardAnalysis.ToJagged(game.GameBoard, value => value),
                    currentPlayerNumber = game.CurrentPlayerNumber,
                    actionsRemainingInTurn = game.ActionsRemainingInTurn,
                    turnNumber = game.TurnNumber,
                    isGameOver = game.IsGameOver,
                    resignedPlayerNumber = game.ResignedPlayerNumber,
                    stateHash = session.StateHash,
                    informallyDecidedFor = session.Analysis?.InformallyDecidedFor,
                    presence = session.Presence(),
                    //Named-account perk, same as the pushed boardAnalysisUpdated
                    analysis = session.NamedAccountConnections.ContainsKey(Context.ConnectionId) && session.Analysis is not null
                        ? BoardAnalysis.ToWire(session.Analysis)
                        : null,
                };
            }
            finally
            {
                session.MoveLock.Release();
            }
        }

        /// <summary>Confirms the caller has the state with this hash. Accepted if it matches either the state the
        /// pending ack was raised for, or the current state - moves made since then don't need re-confirming</summary>
        public async Task AcknowledgeState(uint stateHash)
        {
            var session = _sessionManager.GetByConnectionId(Context.ConnectionId);
            if (session is null)
            {
                return;
            }

            var userId = Context.UserIdentifier!;
            if (session.PendingAcks.TryGetValue(userId, out var pending) &&
                (pending.ExpectedHash == stateHash || session.StateHash == stateHash))
            {
                session.PendingAcks.TryRemove(new KeyValuePair<string, PendingAck>(userId, pending));
            }
            if (stateHash == session.StateHash && session.UnresponsiveUsers.TryRemove(userId, out _))
            {
                await BroadcastPresence(session);
            }
        }

        /// <summary>
        /// Requests a move onto (x, y): a spawn if any owned tile is orthogonally/diagonally adjacent, otherwise a
        /// jump from an owned tile 2 tiles away (which then dies). If more than one tile could be jumped from, the
        /// move isn't applied yet - the caller is asked to disambiguate via ConfirmJump instead.
        /// </summary>
        public async Task RequestMove(int x, int y)
        {
            var session = _sessionManager.GetByConnectionId(Context.ConnectionId);
            if (session is null)
            {
                return;
            }

            //Held across the whole validate-and-apply body, not just the mutation - otherwise two concurrent
            //requests can both pass validation (e.g. both read the same ActionsRemainingInTurn) before either
            //applies, and the second then mutates a board whose state it never actually validated against
            await session.MoveLock.WaitAsync();
            try
            {
                var game = session.GridGame;
                var board = game.GameBoard;

                if (await RejectIfNotCallersTurn(session, x, y) is not { } player)
                {
                    return;
                }
                if (!GridGameMoves.InBounds(board, x, y) || board[x, y] == TileState.Dead || board[x, y] == player)
                {
                    await Clients.Caller.SendAsync("moveRejected", new { x, y, reason = "unreachable" });
                    return;
                }

                //Landing on empty ground claims it; landing on an enemy tile kills it outright rather than capturing it
                var landingValue = board[x, y] == TileState.Unclaimed ? player : TileState.Dead;

                if (GridGameMoves.CanSpawnTo(board, player, x, y))
                {
                    await ApplyMove(session, [(x, y, landingValue)]);
                    return;
                }

                var jumpOrigins = GridGameMoves.JumpOriginsFor(board, player, x, y);
                switch (jumpOrigins.Count)
                {
                    case 0:
                        await Clients.Caller.SendAsync("moveRejected", new { x, y, reason = "unreachable" });
                        break;
                    case 1:
                        await ApplyMove(session, [(jumpOrigins[0].X, jumpOrigins[0].Y, TileState.Dead), (x, y, landingValue)]);
                        break;
                    default:
                        await Clients.Caller.SendAsync("chooseJumpOrigin", new
                        {
                            x,
                            y,
                            origins = jumpOrigins.Select(origin => new { x = origin.X, y = origin.Y })
                        });
                        break;
                }
            }
            finally
            {
                session.MoveLock.Release();
            }
        }

        /// <summary>Completes a jump once the caller has picked which of several eligible tiles to jump from</summary>
        public async Task ConfirmJump(int originX, int originY, int x, int y)
        {
            var session = _sessionManager.GetByConnectionId(Context.ConnectionId);
            if (session is null)
            {
                return;
            }

            await session.MoveLock.WaitAsync();
            try
            {
                var board = session.GridGame.GameBoard;

                if (await RejectIfNotCallersTurn(session, x, y) is not { } player)
                {
                    return;
                }
                if (!GridGameMoves.InBounds(board, x, y) || board[x, y] == TileState.Dead || board[x, y] == player ||
                    !GridGameMoves.IsValidJump(board, player, originX, originY, x, y))
                {
                    await Clients.Caller.SendAsync("moveRejected", new { x, y, reason = "unreachable" });
                    return;
                }

                //Landing on empty ground claims it; landing on an enemy tile kills it outright rather than capturing it
                var landingValue = board[x, y] == TileState.Unclaimed ? player : TileState.Dead;
                await ApplyMove(session, [(originX, originY, TileState.Dead), (x, y, landingValue)]);
            }
            finally
            {
                session.MoveLock.Release();
            }
        }

        /// <summary>Concedes, ending the game for everyone. Scored as the board stands: owned tiles plus uncontested
        /// territory, with contested cells left to nobody (see GameEndAnalysis.ComputeScores). The client only offers
        /// this once the board analysis says the game is informally decided, but any seated player may call it</summary>
        public async Task Resign()
        {
            var session = _sessionManager.GetByConnectionId(Context.ConnectionId);
            if (session is null)
            {
                return;
            }

            await session.MoveLock.WaitAsync();
            try
            {
                var game = session.GridGame;
                if (game.IsGameOver || !session.PlayerNumbers.TryGetValue(Context.UserIdentifier!, out var player))
                {
                    return;
                }

                var previousStateHash = session.StateHash;
                game.ResignedPlayerNumber = player;
                await EndGame(session);
                await BroadcastStateChange(session, [], previousStateHash, requiresAck: true);
            }
            finally
            {
                session.MoveLock.Release();
            }
        }

        /// <returns>The caller's player number if they may move right now, otherwise null (having told them why)</returns>
        private async Task<int?> RejectIfNotCallersTurn(GameSession session, int x, int y)
        {
            string? reason = null;
            if (session.GridGame.IsGameOver)
            {
                reason = "gameOver";
            }
            else if (!session.PlayerNumbers.TryGetValue(Context.UserIdentifier!, out var player))
            {
                reason = "spectating";
            }
            else if (player != session.GridGame.CurrentPlayerNumber)
            {
                reason = "notYourTurn";
            }
            else
            {
                return player;
            }
            await Clients.Caller.SendAsync("moveRejected", new { x, y, reason });
            return null;
        }

        /// <summary>Writes the given (x, y, newTileValue) tuples to the board, advances the turn (skipping past any
        /// player with no legal move left, e.g. eliminated or walled in), checks for game-over, persists to the DB
        /// once per completed round (rather than every move - the in-memory session is otherwise the sole source of
        /// truth while a game is live), and broadcasts all of it. Callers (RequestMove/ConfirmJump) already hold
        /// session.MoveLock for their whole validate-and-apply body, so this doesn't acquire it itself -
        /// SemaphoreSlim isn't reentrant, so doing both would deadlock</summary>
        private async Task ApplyMove(GameSession session, (int X, int Y, int NewValue)[] updates)
        {
            var game = session.GridGame;
            var previousStateHash = session.StateHash;
            var previousTurnNumber = game.TurnNumber;

            foreach (var (x, y, newValue) in updates)
            {
                game.GameBoard[x, y] = newValue;
            }

            game.ActionsRemainingInTurn--;
            session.Analysis = BoardAnalysis.Compute(game.GameBoard, game.PlayerCount);

            if (GameEndAnalysis.IsGameOver(game.GameBoard, game.PlayerCount))
            {
                await EndGame(session);
            }
            else if (game.ActionsRemainingInTurn <= 0)
            {
                var nextPlayer = game.CurrentPlayerNumber;
                var foundMover = false;
                //True once the search below has wrapped past the last player back to the first, i.e. a full
                //round has completed - tracked by candidate value rather than "landed on player 1", since a
                //player skipped for having no legal move (eliminated, walled in) might mean nobody ever lands
                //on exactly 1 again
                var completedRound = false;
                for (var attempt = 0; attempt < game.PlayerCount; attempt++)
                {
                    var candidate = nextPlayer % game.PlayerCount + 1;
                    if (candidate < nextPlayer)
                    {
                        completedRound = true;
                    }
                    nextPlayer = candidate;
                    if (GridGameMoves.HasAnyLegalMove(game.GameBoard, nextPlayer))
                    {
                        foundMover = true;
                        break;
                    }
                }

                if (foundMover)
                {
                    game.CurrentPlayerNumber = nextPlayer;
                    game.ActionsRemainingInTurn = game.ActionsPerTurn;
                    game.TurnNumber++;

                    if (completedRound)
                    {
                        await PersistGame(game);
                    }
                }
                else
                {
                    //Contradicts IsGameOver being false above (a player in contact with another always has at
                    //least one legal move) - treated as game over defensively rather than stalling regardless
                    await EndGame(session);
                }
            }

            var turnEnded = game.IsGameOver || game.TurnNumber != previousTurnNumber;
            await BroadcastStateChange(session, updates, previousStateHash, requiresAck: turnEnded);
        }

        /// <summary>Pushes a state change to everyone in the game, and the refreshed board analysis to named accounts.
        /// Must be called under MoveLock, after the change has been applied to session.GridGame</summary>
        private async Task BroadcastStateChange(GameSession session, (int X, int Y, int NewValue)[] updates, uint previousStateHash, bool requiresAck)
        {
            var game = session.GridGame;
            session.StateHash = StateHash.Compute(game);

            if (requiresAck)
            {
                //The push itself counts as the first prompt - SessionMonitor takes it from there
                var pending = new PendingAck(session.StateHash, DateTime.UtcNow, Prompts: 0);
                foreach (var userId in session.PlayerNumbers.Keys.Where(userId => session.ConnectionsFor(userId).Any()))
                {
                    session.PendingAcks[userId] = pending;
                }
            }

            await Clients.Clients(session.Connections.Keys.ToList()).SendAsync("tileUpdateProcessed", new
            {
                updates = updates.Select(u => new { x = u.X, y = u.Y, newTileValue = u.NewValue }),
                currentPlayerNumber = game.CurrentPlayerNumber,
                actionsRemainingInTurn = game.ActionsRemainingInTurn,
                turnNumber = game.TurnNumber,
                isGameOver = game.IsGameOver,
                resignedPlayerNumber = game.ResignedPlayerNumber,
                previousStateHash,
                stateHash = session.StateHash,
                requiresAck,
                informallyDecidedFor = session.Analysis?.InformallyDecidedFor,
            });

            //Live board analysis is a named-account perk like final scores, so it's only sent to connections known
            //to be on a named account (see OnConnectedToGame)
            if (session.Analysis is not null && !session.NamedAccountConnections.IsEmpty)
            {
                await Clients.Clients(session.NamedAccountConnections.Keys.ToList())
                    .SendAsync("boardAnalysisUpdated", BoardAnalysis.ToWire(session.Analysis));
            }
        }

        private Task BroadcastPresence(GameSession session) =>
            Clients.Clients(session.Connections.Keys.ToList()).SendAsync("presenceUpdated", session.Presence());

        /// <summary>Marks the game over, computes the final score, and persists both immediately rather than waiting
        /// for the last connection to drop</summary>
        private async Task EndGame(GameSession session)
        {
            var game = session.GridGame;
            game.IsGameOver = true;
            session.Analysis ??= BoardAnalysis.Compute(game.GameBoard, game.PlayerCount);
            game.FinalScores = session.Analysis.Players.Select(player => player.Projected).ToArray();
            await PersistGame(game);
        }

        /// <summary>Force-saves the whole entity - the in-memory session, not EF change tracking, is the source of
        /// truth while a game is live, so every field is marked Modified rather than relying on EF to have noticed
        /// what changed</summary>
        private async Task PersistGame(GridGame game)
        {
            _gameContext.Attach(game);
            _gameContext.Entry(game).State = EntityState.Modified;
            await _gameContext.SaveChangesAsync();
        }
    }
}
