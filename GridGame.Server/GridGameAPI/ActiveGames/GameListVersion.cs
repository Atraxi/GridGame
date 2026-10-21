namespace GridGameAPI.ActiveGames
{
    /// <summary>
    /// A cheap "has the game list changed?" check for the home page, which polls GamesController.GetSummaries while
    /// it's open. The version is bumped whenever anything a summary shows changes (a game is created, a turn ends, a
    /// game finishes), and serves as the response's ETag - an unchanged poll is answered 304 from memory, without
    /// touching the database. That matters on the serverless SQL tier: a query would keep it from auto-pausing and
    /// spend the free vCore allowance, an ETag comparison doesn't.
    ///
    /// Polling rather than a push channel, because the alternatives cost more here: Azure SignalR's free tier is
    /// capped at 20 concurrent connections, which a socket on the home page would take away from actual games, and a
    /// long-lived server-sent-events response would have to hold up through both the Cloudflare Worker proxy and the
    /// F1 plan's front end.
    ///
    /// Held in memory only, so it's prefixed with a per-process epoch: after a restart every client's stored ETag
    /// mismatches and they fetch once, rather than a reset counter colliding with a version they already hold.
    /// </summary>
    public class GameListVersion
    {
        private readonly string _epoch = DateTime.UtcNow.Ticks.ToString("x");
        private long _version;

        public void Bump() => Interlocked.Increment(ref _version);

        public string ETagFor(int page) => $"\"{_epoch}-{Interlocked.Read(ref _version)}-{page}\"";
    }
}
