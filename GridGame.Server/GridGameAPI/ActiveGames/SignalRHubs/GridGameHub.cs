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
    public class GridGameHub(GameSessionManager _sessionManager, GameContext _gameContext, GameListVersion _listVersion, ILogger<GridGameHub> _logger) : Hub
    {
        private bool IsNamedAccount => Context.User?.FindFirst(Constants.AccountTypeClaim)?.Value == Constants.NamedAccountType;

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
                    var lockedSeats = _gameContext.GameSeats
                        .Where(seat => seat.GridGameId == gameId)
                        .Select(seat => new { seat.PlayerNumber, seat.PlayerId, seat.Player!.UserName })
                        .ToList();
                    var session = new GameSession
                    {
                        GridGame = game,
                        StateHash = StateHash.Compute(game),
                        Analysis = BoardAnalysis.Compute(game.GameBoard, game.PlayerCount),
                        InvitedUserIds = game.IsInviteOnly
                            ? _gameContext.GameInvites.Where(invite => invite.GridGameId == gameId).Select(invite => invite.PlayerId).ToHashSet()
                            : null,
                    };
                    //Only named accounts can lock a seat, so every lock holder is one. Anyone who already moved from
                    //their seat counts as having played, so an opponent's first move after a reload still records them
                    var movedPlayers = game.MoveLog.Select(action => action.Player).ToHashSet();
                    foreach (var seat in lockedSeats)
                    {
                        session.PlayerNumbers[seat.PlayerId] = seat.PlayerNumber;
                        session.LockedSeats[seat.PlayerId] = 0;
                        session.UserNames[seat.PlayerId] = seat.UserName ?? "";
                        session.NamedUsers[seat.PlayerId] = 0;
                        if (movedPlayers.Contains(seat.PlayerNumber))
                        {
                            session.ActiveNamedUsers.Add(seat.PlayerId);
                        }
                    }
                    return session;
                },
                session =>
                {
                    session.Connections[Context.ConnectionId] = userId;
                    session.SeenUsers[userId] = 0;
                    if (IsNamedAccount)
                    {
                        session.NamedAccountConnections[Context.ConnectionId] = 0;
                        session.NamedUsers[userId] = 0;
                    }
                    session.UserNames[userId] = Context.User?.FindFirst(ClaimTypes.Name)?.Value ?? "";
                    session.TryTakeSeat(userId);
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
                game.MoveLog.Add(GameAction.Resignation(game.TurnNumber, player));
                game.ResignedPlayerNumber = player;
                EndGame(session);
                _listVersion.Bump();
                await BroadcastStateChange(session, [], previousStateHash, requiresAck: true);
                await PersistGame(game);
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

        /// <summary>Writes the given (x, y, newTileValue) tuples to the board - either [landing] for a spawn, or
        /// [origin, landing] for a jump - and records the action in the move history. Then advances the turn (skipping past any
        /// player with no legal move left, e.g. eliminated or walled in), checks for game-over, broadcasts all of it,
        /// and persists to the DB at the end of every turn. The in-memory session is otherwise the sole source of truth
        /// while a game is live, and the free App Service plan can unload the app whenever it's had no incoming requests
        /// for a while - which a game whose players have wandered off won't send - so anything unsaved could be lost
        /// with it. At most the turn in progress is now. Callers (RequestMove/ConfirmJump) already hold
        /// session.MoveLock for their whole validate-and-apply body, so this doesn't acquire it itself -
        /// SemaphoreSlim isn't reentrant, so doing both would deadlock</summary>
        private async Task ApplyMove(GameSession session, (int X, int Y, int NewValue)[] updates)
        {
            var game = session.GridGame;
            var previousStateHash = session.StateHash;
            var previousTurnNumber = game.TurnNumber;

            //Recorded before the board changes, since the history keeps what the landing tile held beforehand
            var landing = updates[^1];
            var origin = updates.Length > 1 ? updates[0] : (X: -1, Y: -1, NewValue: 0);
            game.MoveLog.Add(new GameAction(
                game.TurnNumber,
                game.CurrentPlayerNumber,
                updates.Length > 1 ? GameActionKind.Jump : GameActionKind.Spawn,
                landing.X,
                landing.Y,
                game.GameBoard[landing.X, landing.Y],
                origin.X,
                origin.Y));

            foreach (var (x, y, newValue) in updates)
            {
                game.GameBoard[x, y] = newValue;
            }

            game.ActionsRemainingInTurn--;
            session.Analysis = BoardAnalysis.Compute(game.GameBoard, game.PlayerCount);

            if (GameEndAnalysis.IsGameOver(game.GameBoard, game.PlayerCount))
            {
                EndGame(session);
            }
            else if (game.ActionsRemainingInTurn <= 0)
            {
                var nextPlayer = game.CurrentPlayerNumber;
                var foundMover = false;
                for (var attempt = 0; attempt < game.PlayerCount; attempt++)
                {
                    nextPlayer = nextPlayer % game.PlayerCount + 1;
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
                }
                else
                {
                    //Contradicts IsGameOver being false above (a player in contact with another always has at
                    //least one legal move) - treated as game over defensively rather than stalling regardless
                    EndGame(session);
                }
            }

            var turnEnded = game.IsGameOver || game.TurnNumber != previousTurnNumber;
            if (turnEnded)
            {
                //The home page's game list shows the turn number and whether the game is over
                _listVersion.Bump();
            }
            await BroadcastStateChange(session, updates, previousStateHash, requiresAck: turnEnded);
            //Database work comes after the broadcast, so it never delays anyone seeing the move - which matters most
            //when the database has paused itself and takes the better part of a minute to answer
            if (turnEnded)
            {
                await PersistGame(game);
            }
            await RecordOpponents(session);
        }

        /// <summary>Adds the caller and every other named account that has moved in this game to each other's
        /// past-opponents lists, the first time the caller moves in this session. Recorded on moves rather than on
        /// taking a seat, because merely opening a game with a free seat takes one. Called under MoveLock</summary>
        private async Task RecordOpponents(GameSession session)
        {
            var userId = Context.UserIdentifier!;
            if (!session.NamedUsers.ContainsKey(userId) || !session.ActiveNamedUsers.Add(userId))
            {
                return;
            }
            var others = session.ActiveNamedUsers.Where(other => other != userId).ToList();
            if (others.Count == 0)
            {
                return;
            }

            try
            {
                var existing = await _gameContext.PastOpponents
                    .Where(record => (record.PlayerId == userId && others.Contains(record.OpponentId)) ||
                                     (record.OpponentId == userId && others.Contains(record.PlayerId)))
                    .ToListAsync();
                var now = DateTime.UtcNow;
                foreach (var (playerId, opponentId) in others.SelectMany(other => new[] { (userId, other), (other, userId) }))
                {
                    var record = existing.FirstOrDefault(r => r.PlayerId == playerId && r.OpponentId == opponentId);
                    if (record is null)
                    {
                        _gameContext.PastOpponents.Add(new PastOpponent { PlayerId = playerId, OpponentId = opponentId, LastPlayedAt = now });
                    }
                    else
                    {
                        record.LastPlayedAt = now;
                    }
                }
                await _gameContext.SaveChangesAsync();
            }
            catch (DbUpdateException exception)
            {
                //A nice-to-have: the same pair being recorded by two games at once, say, mustn't fail the move itself.
                //Whatever didn't stick is recorded on their next game together
                _logger.LogWarning(exception, "Couldn't record past opponents for game {GameId}", session.GridGame.Id);
            }
        }

        /// <summary>Ties the caller's seat to their account for good: from now on it's theirs whenever the game is
        /// reopened, and nobody else can be given it. Named accounts only, see GameSeat</summary>
        public async Task LockSeat()
        {
            var session = _sessionManager.GetByConnectionId(Context.ConnectionId);
            var userId = Context.UserIdentifier!;
            if (session is null || !IsNamedAccount || session.GridGame.IsGameOver ||
                !session.PlayerNumbers.TryGetValue(userId, out var playerNumber) || session.LockedSeats.ContainsKey(userId))
            {
                return;
            }

            try
            {
                _gameContext.GameSeats.Add(new GameSeat { GridGameId = session.GridGame.Id, PlayerNumber = playerNumber, PlayerId = userId });
                await _gameContext.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                //Already locked - e.g. a second tab asked at the same moment. Either way the seat is now theirs, unless
                //the row says otherwise, in which case it's left alone
                if (!await _gameContext.GameSeats.AnyAsync(seat => seat.GridGameId == session.GridGame.Id && seat.PlayerId == userId && seat.PlayerNumber == playerNumber))
                {
                    throw;
                }
            }
            session.LockedSeats[userId] = 0;
            await BroadcastPresence(session);
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

        /// <summary>Marks the game over and computes the final score. The caller persists it once it has broadcast the
        /// result, rather than waiting for the last connection to drop</summary>
        private static void EndGame(GameSession session)
        {
            var game = session.GridGame;
            game.IsGameOver = true;
            session.Analysis ??= BoardAnalysis.Compute(game.GameBoard, game.PlayerCount);
            game.FinalScores = session.Analysis.Players.Select(player => player.Projected).ToArray();
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
