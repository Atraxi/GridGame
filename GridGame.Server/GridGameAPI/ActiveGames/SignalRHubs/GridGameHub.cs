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
                        .Select(seat => new { seat.PlayerNumber, seat.PlayerId, seat.Player!.UserName, seat.Player.ColourPrimary, seat.Player.ColourSecondary })
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
                    //Anyone who already moved from their seat counts as having played, so an opponent's first move after
                    //a reload still records them. Guests hold locks too (see LockCallersSeat) but aren't recorded as opponents
                    var movedPlayers = game.MoveLog.Select(action => action.Player).ToHashSet();
                    foreach (var seat in lockedSeats)
                    {
                        session.PlayerNumbers[seat.PlayerId] = seat.PlayerNumber;
                        session.LockedSeats[seat.PlayerId] = 0;
                        session.UserNames[seat.PlayerId] = seat.UserName ?? "";
                        session.Colours[seat.PlayerId] = (seat.ColourPrimary, seat.ColourSecondary);
                        if (seat.UserName?.StartsWith(Constants.GuestUserNamePrefix) == true)
                        {
                            continue;
                        }
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
                        session.NamedUsers[userId] = 0;
                    }
                    session.UserNames[userId] = Context.User?.FindFirst(ClaimTypes.Name)?.Value ?? "";
                    session.TryTakeSeat(userId);
                    //A fresh connection always pulls a snapshot (see the client's resync), so whatever they missed
                    //while unresponsive no longer matters
                    session.UnresponsiveUsers.TryRemove(userId, out _);
                });

            //Looked up on every connect, so a colour changed since the last visit shows up
            var colours = await _gameContext.Users.AsNoTracking().Where(user => user.Id == userId)
                .Select(user => new { user.ColourPrimary, user.ColourSecondary }).SingleOrDefaultAsync();
            session.Colours[userId] = (colours?.ColourPrimary, colours?.ColourSecondary);

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
                    resignedOnTime = game.ResignedOnTime,
                    autoResolving = session.AutoResolving,
                    clock = GameClock.Wire(game, DateTime.UtcNow),
                    rematchGameId = game.RematchGameId,
                    stateHash = session.StateHash,
                    informallyDecidedFor = session.Analysis?.InformallyDecidedFor,
                    presence = session.Presence(),
                    analysis = session.Analysis is not null ? BoardAnalysis.ToWire(session.Analysis) : null,
                    clashChoice = (await _gameContext.ClashChoices.AsNoTracking()
                        .Where(choice => choice.GridGameId == game.Id && choice.PlayerId == Context.UserIdentifier)
                        .Select(choice => (bool?)choice.SwitchToDefaults)
                        .SingleOrDefaultAsync()),
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
            await RequestMoveCore(x, y);
            await RunAutoResolve();
        }

        private async Task RequestMoveCore(int x, int y)
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

                if (GridGameMoves.IsBlocked(board, session.Analysis?.Insight, x, y))
                {
                    await Clients.Caller.SendAsync("moveRejected", new { x, y, reason = "uncontested" });
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
            await ConfirmJumpCore(originX, originY, x, y);
            await RunAutoResolve();
        }

        private async Task ConfirmJumpCore(int originX, int originY, int x, int y)
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

                if (GridGameMoves.IsBlocked(board, session.Analysis?.Insight, x, y))
                {
                    await Clients.Caller.SendAsync("moveRejected", new { x, y, reason = "uncontested" });
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
        /// territory, with contested cells left to nobody (see GameEndAnalysis.ComputeScores). Any seated player may
        /// call it at any time - the client just asks for an extra confirmation unless the board analysis says the
        /// game is already informally decided against them</summary>
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
                if (game.IsGameOver || session.AutoResolving || !session.PlayerNumbers.TryGetValue(Context.UserIdentifier!, out var player))
                {
                    return;
                }
                await EndByResignation(session, player, onTime: false);
            }
            finally
            {
                session.MoveLock.Release();
            }
        }

        /// <summary>Ends the game with this player as the loser, whether they conceded or ran out of time. Call under MoveLock</summary>
        private async Task EndByResignation(GameSession session, int player, bool onTime)
        {
            var game = session.GridGame;
            var previousStateHash = session.StateHash;
            game.MoveLog.Add(GameAction.Resignation(game.TurnNumber, player));
            game.ResignedPlayerNumber = player;
            game.ResignedOnTime = onTime;
            EndGame(session);
            _listVersion.Bump();
            await BroadcastStateChange(session, [], previousStateHash, requiresAck: true);
            await PersistGame(game);
        }

        /// <summary>A timeout takes effect: the player loses, or in PerTurnSkip mode their turn just passes on. Call under MoveLock</summary>
        private async Task ExpireTurn(GameSession session, int player)
        {
            var game = session.GridGame;
            if (game.ClockMode != ClockMode.PerTurnSkip)
            {
                await EndByResignation(session, player, onTime: true);
                return;
            }
            var previousStateHash = session.StateHash;
            if (GridGameMoves.NextMover(game.GameBoard, game.PlayerCount, player, session.Analysis!.Insight) is not { } next)
            {
                return;
            }
            GameClock.EndTurn(game, player, DateTime.UtcNow);
            game.CurrentPlayerNumber = next;
            game.ActionsRemainingInTurn = game.ActionsPerTurn;
            game.TurnNumber++;
            _listVersion.Bump();
            await BroadcastStateChange(session, [], previousStateHash, requiresAck: true);
            await PersistGame(game);
        }

        /// <summary>Called by a client whose countdown for the current player has hit zero. The server's own clock decides
        /// - an early or stale claim does nothing - and the timeout takes effect (see ExpireTurn)</summary>
        public async Task ClaimTimeout()
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
                if (!session.AutoResolving && GameClock.IsExpired(game, DateTime.UtcNow))
                {
                    await ExpireTurn(session, game.CurrentPlayerNumber);
                }
            }
            finally
            {
                session.MoveLock.Release();
            }
        }

        private const int AutoResolveStepMs = 500;

        /// <summary>Plays out a forced ending (see AutoResolve) one jump at a time at roughly the pace of the home page's
        /// demo clips, with moves and resignations refused meanwhile. Runs after the move that made the ending forced
        /// has released MoveLock; the lock is only held for each individual jump so reconnecting players aren't kept waiting</summary>
        private async Task RunAutoResolve()
        {
            var session = _sessionManager.GetByConnectionId(Context.ConnectionId);
            if (session is null)
            {
                return;
            }

            List<ForcedMove> plan;
            await session.MoveLock.WaitAsync();
            try
            {
                if (session.PendingAutoPlan is not { } pending || session.GridGame.IsGameOver)
                {
                    return;
                }
                plan = pending;
                session.PendingAutoPlan = null;
                session.AutoResolving = true;
                await Clients.Clients(session.Connections.Keys.ToList()).SendAsync("autoResolveStarted");
            }
            finally
            {
                session.MoveLock.Release();
            }

            try
            {
                foreach (var move in plan)
                {
                    await Task.Delay(AutoResolveStepMs);
                    await session.MoveLock.WaitAsync();
                    try
                    {
                        var game = session.GridGame;
                        if (game.IsGameOver || game.CurrentPlayerNumber != move.Player)
                        {
                            break;
                        }
                        await ApplyMove(session, [(move.OriginX, move.OriginY, TileState.Dead), (move.X, move.Y, TileState.Dead)]);
                    }
                    finally
                    {
                        session.MoveLock.Release();
                    }
                }
            }
            finally
            {
                await session.MoveLock.WaitAsync();
                session.AutoResolving = false;
                //The clock stood still meanwhile, so whoever is on the move starts afresh
                if (session.GridGame.TurnStartedUtc is not null)
                {
                    session.GridGame.TurnStartedUtc = DateTime.UtcNow;
                }
                session.MoveLock.Release();
            }
        }

        /// <summary>Hosts a new game for everyone in this finished one - on the same map unless another published map
        /// is given - and pulls them into it with rematchCreated. Seats aren't carried over: they're handed out in the
        /// order people arrive, as in any new game, so anyone who doesn't follow leaves their seat to whoever joins
        /// next. An invite-only game's rematch keeps the same invite list. Hosting is a
        /// named-account privilege, as for GamesController.CreateFromMap. Only one rematch is ever made per game -
        /// asking again returns the existing one</summary>
        /// <param name="mapId">Null for the map this game was hosted from</param>
        /// <returns>{ gameId } on success, otherwise { error }</returns>
        public async Task<object> Rematch(int? mapId)
        {
            var session = _sessionManager.GetByConnectionId(Context.ConnectionId);
            var userId = Context.UserIdentifier!;
            if (session is null)
            {
                return new { error = "You're no longer connected to this game - refresh the page and try again." };
            }
            if (!IsNamedAccount)
            {
                return new { error = "Only named accounts can host a rematch." };
            }

            await session.MoveLock.WaitAsync();
            try
            {
                var game = session.GridGame;
                if (!game.IsGameOver)
                {
                    return new { error = "This game hasn't ended yet." };
                }
                if (!session.PlayerNumbers.ContainsKey(userId))
                {
                    return new { error = "Only this game's players can start a rematch." };
                }
                if (game.RematchGameId is { } existing)
                {
                    return new { gameId = existing };
                }
                if ((mapId ?? game.MapId) is not { } chosenMapId)
                {
                    return new { error = "This game was hosted before maps were recorded - pick a map for the rematch." };
                }
                var (map, error) = await GameHosting.FindHostableMapAsync(_gameContext, chosenMapId, userId);
                if (map is null)
                {
                    return new { error };
                }

                //A smaller map would eject the players beyond its seat count, so it's only allowed once enough of them have
                //left that everyone still here fits
                var online = session.PlayerNumbers.Keys.Count(seated => session.ConnectionsFor(seated).Any());
                var someoneOffline = online < session.PlayerNumbers.Count;
                if (map.PlayerCount != game.PlayerCount && !(someoneOffline && map.PlayerCount >= online))
                {
                    return new { error = someoneOffline
                        ? $"That map has too few seats for the {online} players still here - pick one with at least {online}."
                        : $"A rematch needs a map with {game.PlayerCount} players, the same as this game." };
                }

                //Named for the map actually being played. The invite list, if any, carries over: the rematch is as
                //private as this game was, and a public one would show up in everyone's list
                var rematchName = map.Id == game.MapId ? GameHosting.RematchName(game.Name) : GameHosting.RematchName(map.Name);
                var rematch = GameHosting.NewGame(map, rematchName, isInviteOnly: game.IsInviteOnly);
                GameClock.Configure(rematch, game.ClockMode, game.ClockBaseSeconds, game.ClockIncrementSeconds);
                _gameContext.GridGames.Add(rematch);
                if (game.IsInviteOnly && session.InvitedUserIds is { } invitees)
                {
                    _gameContext.GameInvites.AddRange(invitees.Select(id => new GameInvite { Game = rematch, PlayerId = id }));
                }
                await _gameContext.SaveChangesAsync();
                game.RematchGameId = rematch.Id;
                _listVersion.Bump();

                await Clients.Clients(session.Connections.Keys.ToList()).SendAsync("rematchCreated", new { gameId = rematch.Id });
                await PersistGame(game);
                return new { gameId = rematch.Id };
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
            else if (session.AutoResolving)
            {
                reason = "autoResolving";
            }
            else if (!session.PlayerNumbers.TryGetValue(Context.UserIdentifier!, out var player))
            {
                reason = "spectating";
            }
            else if (player != session.GridGame.CurrentPlayerNumber)
            {
                reason = "notYourTurn";
            }
            else if (GameClock.IsExpired(session.GridGame, DateTime.UtcNow))
            {
                //Too late: the clock ran out before this move arrived
                await ExpireTurn(session, player);
                reason = session.GridGame.IsGameOver ? "gameOver" : "notYourTurn";
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
            var insight = session.Analysis.Insight;

            if (GameEndAnalysis.IsGameOver(game.GameBoard, game.PlayerCount))
            {
                EndGame(session);
            }
            //A player left with only moves into uncontested ground (see GridGameMoves.IsBlocked) has nothing worth doing
            //with the actions they have left, so their turn ends early rather than leaving them stuck
            else if (game.ActionsRemainingInTurn <= 0 || !GridGameMoves.HasAnyLegalMove(game.GameBoard, game.CurrentPlayerNumber, insight))
            {
                var mover = game.CurrentPlayerNumber;
                if (GridGameMoves.NextMover(game.GameBoard, game.PlayerCount, mover, insight) is { } nextPlayer)
                {
                    GameClock.EndTurn(game, mover, DateTime.UtcNow, paused: session.AutoResolving);
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

            if (!game.IsGameOver && !session.AutoResolving)
            {
                session.PendingAutoPlan = AutoResolve.Plan(game.GameBoard, game.PlayerCount, game.CurrentPlayerNumber,
                    game.ActionsRemainingInTurn, game.ActionsPerTurn, insight);
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
            //A guest who has played leaves a record, so a named account they later log in to can claim the game
            if (!IsNamedAccount)
            {
                await LockCallersSeat(session);
            }
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
        /// reopened, and nobody else can be given it. Guests are locked automatically once they've moved (see ApplyMove)</summary>
        public async Task LockSeat()
        {
            var session = _sessionManager.GetByConnectionId(Context.ConnectionId);
            if (session is null || session.GridGame.IsGameOver)
            {
                return;
            }
            await LockCallersSeat(session);
        }

        /// <summary>Remembers this account's answer to the colour-clash prompt for this game (see ClashChoice)</summary>
        public async Task SetClashChoice(bool switchToDefaults)
        {
            var session = _sessionManager.GetByConnectionId(Context.ConnectionId);
            if (session is null)
            {
                return;
            }
            var (gameId, userId) = (session.GridGame.Id, Context.UserIdentifier!);
            var choice = await _gameContext.ClashChoices.SingleOrDefaultAsync(row => row.GridGameId == gameId && row.PlayerId == userId);
            if (choice is null)
            {
                _gameContext.ClashChoices.Add(new ClashChoice { GridGameId = gameId, PlayerId = userId, SwitchToDefaults = switchToDefaults });
            }
            else
            {
                choice.SwitchToDefaults = switchToDefaults;
            }
            await _gameContext.SaveChangesAsync();
        }

        /// <summary>Lets go of a locked seat, so it's handed out like any other once the game is reopened. Named accounts
        /// only - a guest's lock is just the record of their games</summary>
        public async Task UnlockSeat()
        {
            var session = _sessionManager.GetByConnectionId(Context.ConnectionId);
            var userId = Context.UserIdentifier!;
            if (session is null || !IsNamedAccount || !session.LockedSeats.ContainsKey(userId))
            {
                return;
            }
            await _gameContext.GameSeats.Where(seat => seat.GridGameId == session.GridGame.Id && seat.PlayerId == userId).ExecuteDeleteAsync();
            session.LockedSeats.TryRemove(userId, out _);
            await BroadcastPresence(session);
        }

        private async Task LockCallersSeat(GameSession session)
        {
            var userId = Context.UserIdentifier!;
            if (!session.PlayerNumbers.TryGetValue(userId, out var playerNumber) || session.LockedSeats.ContainsKey(userId))
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

        /// <summary>Pushes a state change to everyone in the game, and the refreshed board analysis.
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
                resignedOnTime = game.ResignedOnTime,
                autoResolving = session.AutoResolving,
                clock = GameClock.Wire(game, DateTime.UtcNow),
                previousStateHash,
                stateHash = session.StateHash,
                requiresAck,
                informallyDecidedFor = session.Analysis?.InformallyDecidedFor,
            });

            //Everyone gets the analysis, since the territory overlay and the move hints depend on it. Only the statistics
            //table is a named-account perk, and the client decides that
            if (session.Analysis is not null)
            {
                await Clients.Clients(session.Connections.Keys.ToList())
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
