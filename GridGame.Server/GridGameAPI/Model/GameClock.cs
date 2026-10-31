using System.Text.Json.Serialization;

namespace GridGameAPI.Model
{
    [JsonConverter(typeof(JsonStringEnumConverter<ClockMode>))]
    public enum ClockMode
    {
        None = 0,
        /// <summary>Chess clock: every player has a bank of time that only runs down on their own turns, topped up by an
        /// increment each time they finish one</summary>
        Bank = 1,
        /// <summary>Every turn has the same time limit</summary>
        PerTurn = 2,
        /// <summary>Every turn has the same time limit, but running out only ends the turn rather than the game</summary>
        PerTurnSkip = 3,
    }

    /// <summary>
    /// Turn timer. Running out of time loses the game, scored like a resignation (see GridGameHub.EndByResignation) -
    /// except in PerTurnSkip mode, where it only passes the turn on (see GridGameHub.SkipTurn). The clock is paused while
    /// a forced ending is auto-played (see GridGameHub.RunAutoResolve).
    ///
    /// Shortcuts: the clock starts when the first turn ends, so the opening turn is untimed - nobody should be flagged
    /// while still waiting for the other seats to fill. And a timeout is only noticed when a client asks for it (see
    /// GridGameHub.ClaimTimeout) or someone moves late; a game nobody has open stays as it is until someone returns
    /// </summary>
    public static class GameClock
    {
        public const int MinBankSeconds = 30, MaxBankSeconds = 3600, MaxIncrementSeconds = 60;
        public const int MinTurnSeconds = 5, MaxTurnSeconds = 600;

        /// <returns>Why the settings aren't acceptable, or null</returns>
        public static string? Validate(ClockMode mode, int baseSeconds, int incrementSeconds) => mode switch
        {
            ClockMode.None => null,
            ClockMode.Bank when baseSeconds < MinBankSeconds || baseSeconds > MaxBankSeconds => $"A time bank must be {MinBankSeconds}-{MaxBankSeconds} seconds.",
            ClockMode.Bank when incrementSeconds < 0 || incrementSeconds > MaxIncrementSeconds => $"The increment must be 0-{MaxIncrementSeconds} seconds.",
            ClockMode.PerTurn or ClockMode.PerTurnSkip when baseSeconds < MinTurnSeconds || baseSeconds > MaxTurnSeconds => $"A turn limit must be {MinTurnSeconds}-{MaxTurnSeconds} seconds.",
            ClockMode.Bank or ClockMode.PerTurn or ClockMode.PerTurnSkip => null,
            _ => "Unknown clock mode.",
        };

        public static void Configure(GridGame game, ClockMode mode, int baseSeconds, int incrementSeconds)
        {
            game.ClockMode = mode;
            game.ClockBaseSeconds = mode == ClockMode.None ? 0 : baseSeconds;
            game.ClockIncrementSeconds = mode == ClockMode.Bank ? incrementSeconds : 0;
            game.ClockRemainingMs = mode == ClockMode.Bank ? Enumerable.Repeat(baseSeconds * 1000, game.PlayerCount).ToArray() : null;
        }

        private static bool IsPerTurn(GridGame game) => game.ClockMode is ClockMode.PerTurn or ClockMode.PerTurnSkip;

        private static bool IsRunning(GridGame game) =>
            game.ClockMode != ClockMode.None && game.TurnStartedUtc is not null && !game.IsGameOver;

        /// <summary>Milliseconds the current player has left, or null if no clock is running</summary>
        public static long? RemainingMs(GridGame game, DateTime now)
        {
            if (!IsRunning(game))
            {
                return null;
            }
            var elapsed = (long)(now - game.TurnStartedUtc!.Value).TotalMilliseconds;
            var allowance = IsPerTurn(game)
                ? game.ClockBaseSeconds * 1000L
                : game.ClockRemainingMs![game.CurrentPlayerNumber - 1];
            return allowance - elapsed;
        }

        public static bool IsExpired(GridGame game, DateTime now) => RemainingMs(game, now) <= 0;

        /// <summary>Call when the turn passes on from `mover`, before CurrentPlayerNumber/TurnNumber are read again.
        /// `paused` (auto-play) charges the mover nothing and gives no increment</summary>
        public static void EndTurn(GridGame game, int mover, DateTime now, bool paused = false)
        {
            if (game.ClockMode == ClockMode.None)
            {
                return;
            }
            if (game.ClockMode == ClockMode.Bank && !paused && game.TurnStartedUtc is { } started)
            {
                var elapsed = (int)Math.Min((now - started).TotalMilliseconds, int.MaxValue);
                var bank = game.ClockRemainingMs![mover - 1];
                game.ClockRemainingMs[mover - 1] = Math.Max(bank - elapsed, 0) + game.ClockIncrementSeconds * 1000;
            }
            game.TurnStartedUtc = now;
        }

        /// <summary>What clients need to draw the clocks: the banks as of the start of the current turn, plus how much of
        /// that turn has gone. Elapsed time is sent rather than a timestamp so client clock skew doesn't matter</summary>
        public static object? Wire(GridGame game, DateTime now) => game.ClockMode == ClockMode.None ? null : new
        {
            mode = game.ClockMode,
            baseMs = game.ClockBaseSeconds * 1000,
            incrementMs = game.ClockIncrementSeconds * 1000,
            remainingMs = game.ClockMode == ClockMode.Bank ? game.ClockRemainingMs : null,
            elapsedMs = IsRunning(game) ? (long)(now - game.TurnStartedUtc!.Value).TotalMilliseconds : (long?)null,
        };
    }
}
