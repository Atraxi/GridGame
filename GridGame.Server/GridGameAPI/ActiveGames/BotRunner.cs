using GridGameAPI.Bots;
using GridGameAPI.Model;

namespace GridGameAPI.ActiveGames
{
    /// <summary>Plays the bots' turns in the background, one task per live game with bots in it, so no player's call is
    /// held up by a bot thinking. It ticks until the game ends or its session is dropped; a game reloaded from the
    /// database gets a new one (see GridGameHub.OnConnectedToGame)</summary>
    public class BotRunner(IServiceScopeFactory _scopes, ILogger<BotRunner> _logger)
    {
        private const int StepMs = 500;
        private static readonly TimeSpan ThinkLimit = TimeSpan.FromSeconds(20);

        /// <summary>Starts playing the session's bots unless that's already happening. Call under MembershipLock</summary>
        public void EnsureRunning(GameSession session)
        {
            if (session.BotRunning)
            {
                return;
            }
            session.BotRunning = true;
            _ = Task.Run(() => Run(session));
        }

        private async Task Run(GameSession session)
        {
            try
            {
                while (await PlayStep(session)) { }
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "The bots stopped playing game {GameId}", session.GridGame.Id);
            }
            finally
            {
                lock (session.MembershipLock)
                {
                    session.BotRunning = false;
                }
            }
        }

        /// <returns>False once there is nothing left to play (the game is over or its session dropped)</returns>
        private async Task<bool> PlayStep(GameSession session)
        {
            await Task.Delay(StepMs);

            BotTurn? turn = null;
            IBot? bot = null;
            var seat = 0;
            uint seenHash = 0;
            await session.MoveLock.WaitAsync();
            try
            {
                var game = session.GridGame;
                if (session.IsClosed || game.IsGameOver)
                {
                    return false;
                }
                //Someone else is playing out a forced ending
                if (session.AutoResolving)
                {
                    return true;
                }
                if (session.PendingAutoPlan is null)
                {
                    seat = game.CurrentPlayerNumber;
                    if (!session.Bots.TryGetValue(seat, out bot))
                    {
                        return true;
                    }
                    var legalMoves = GridGameMoves.LegalMoves(game.GameBoard, seat, session.Analysis?.Insight);
                    turn = new BotTurn((int[,])game.GameBoard.Clone(), seat, game.PlayerCount, game.ActionsPerTurn,
                        game.ActionsRemainingInTurn, legalMoves.AsReadOnly());
                    seenHash = session.StateHash;
                }
            }
            finally
            {
                session.MoveLock.Release();
            }

            //Each use gets its own scope, and so its own database context
            using var scope = _scopes.CreateScope();
            var engine = scope.ServiceProvider.GetRequiredService<GameEngine>();
            if (turn is null || bot is null)
            {
                //The last move made the ending forced
                await engine.RunAutoResolve(session);
                return true;
            }

            var move = await ChooseMove(session, seat, bot, turn);

            await session.MoveLock.WaitAsync();
            try
            {
                //Anything that happened while the bot was thinking (a resignation, say) means looking again
                if (session.IsClosed || session.StateHash != seenHash)
                {
                    return true;
                }
                if (GameClock.IsExpired(session.GridGame, DateTime.UtcNow))
                {
                    await engine.ExpireTurn(session, seat);
                    return true;
                }
                await engine.ApplyMove(session, move.TileChanges(session.GridGame.GameBoard, seat));
                return true;
            }
            finally
            {
                session.MoveLock.Release();
            }
        }

#if DEBUG
        /// <summary>The bot's choice. In a debug build a bot that throws or picks an illegal move stops the game (and is
        /// logged), with no time limit so breakpoints work</summary>
        private Task<Move> ChooseMove(GameSession session, int seat, IBot bot, BotTurn turn)
        {
            var move = bot.ChooseMove(turn);
            return turn.LegalMoves.Contains(move)
                ? Task.FromResult(move)
                : throw new InvalidOperationException($"{bot.GetType().Name} chose {move}, which is not a legal move");
        }
#else
        /// <summary>The bot's choice - or a random legal move if it throws or picks an illegal one, so one buggy bot can't
        /// wedge a game. A bot that overruns its time limit is cancelled and, since its thread may never come back, not
        /// asked again. (A debug build fails loudly instead.)</summary>
        private async Task<Move> ChooseMove(GameSession session, int seat, IBot bot, BotTurn turn)
        {
            var name = bot.GetType().Name;
            if (!session.StuckBotSeats.Contains(seat))
            {
                using var timeUp = new CancellationTokenSource();
                try
                {
                    var move = await Task.Run(() => bot.ChooseMove(turn with { Cancel = timeUp.Token }))
                        .WaitAsync(ThinkLimit);
                    if (turn.LegalMoves.Contains(move))
                    {
                        return move;
                    }
                    _logger.LogWarning("{Bot} chose {Move}, which is not a legal move; playing a random one", name, move);
                }
                catch (TimeoutException)
                {
                    timeUp.Cancel();
                    session.StuckBotSeats.Add(seat);
                    _logger.LogWarning("{Bot} took over {Limit}s; playing random moves for it from now on", name, ThinkLimit.TotalSeconds);
                }
                catch (Exception exception)
                {
                    _logger.LogWarning(exception, "{Bot} threw while choosing a move; playing a random one", name);
                }
            }
            return turn.LegalMoves[Random.Shared.Next(turn.LegalMoves.Count)];
        }
#endif
    }
}
