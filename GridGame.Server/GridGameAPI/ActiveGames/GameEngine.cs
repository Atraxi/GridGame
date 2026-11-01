using GridGameAPI.ActiveGames.SignalRHubs;
using GridGameAPI.Database;
using GridGameAPI.Model;
using GridGameAPI.UtilityInfrastructure;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace GridGameAPI.ActiveGames
{
    /// <summary>What happens to a live game once a move or event has been decided on: applying it, passing the turn,
    /// ending the game, pushing the result to the players and saving it. Shared by GridGameHub (a connected player's
    /// moves) and BotRunner (the bots'). Every method that takes a session must be called under its MoveLock</summary>
    public class GameEngine(GameContext _gameContext, IHubContext<GridGameHub> _hub, GameListVersion _listVersion)
    {
        /// <summary>Ends the game with this player as the loser, whether they conceded or ran out of time. Call under MoveLock</summary>
        public async Task EndByResignation(GameSession session, int player, bool onTime)
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
        public async Task ExpireTurn(GameSession session, int player)
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

        private const int AutoResolveStepMs = 500;

        /// <summary>Plays out a forced ending (see AutoResolve) one jump at a time at roughly the pace of the home page's
        /// demo clips, with moves and resignations refused meanwhile. Runs after the move that made the ending forced
        /// has released MoveLock; the lock is only held for each individual jump so reconnecting players aren't kept waiting</summary>
        public async Task RunAutoResolve(GameSession session)
        {
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
                await _hub.Clients.Clients(session.Connections.Keys.ToList()).SendAsync("autoResolveStarted");
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

        /// <summary>Writes the given (x, y, newTileValue) tuples to the board - either [landing] for a spawn, or
        /// [origin, landing] for a jump - and records the action in the move history. Then advances the turn (skipping past any
        /// player with no legal move left, e.g. eliminated or walled in), checks for game-over, broadcasts all of it,
        /// and persists to the DB at the end of every turn. The in-memory session is otherwise the sole source of truth
        /// while a game is live, and the free App Service plan can unload the app whenever it's had no incoming requests
        /// for a while - which a game whose players have wandered off won't send - so anything unsaved could be lost
        /// with it. At most the turn in progress is now. Callers already hold session.MoveLock for their whole
        /// validate-and-apply body, so this doesn't acquire it itself - SemaphoreSlim isn't reentrant, so doing both
        /// would deadlock</summary>
        public async Task ApplyMove(GameSession session, (int X, int Y, int NewValue)[] updates)
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

            await _hub.Clients.Clients(session.Connections.Keys.ToList()).SendAsync("tileUpdateProcessed", new
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
                await _hub.Clients.Clients(session.Connections.Keys.ToList())
                    .SendAsync("boardAnalysisUpdated", BoardAnalysis.ToWire(session.Analysis));
            }
        }

        public Task BroadcastPresence(GameSession session) =>
            _hub.Clients.Clients(session.Connections.Keys.ToList()).SendAsync("presenceUpdated", session.Presence());

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
        public async Task PersistGame(GridGame game)
        {
            _gameContext.Attach(game);
            _gameContext.Entry(game).State = EntityState.Modified;
            await _gameContext.SaveChangesAsync();
        }
    }
}
