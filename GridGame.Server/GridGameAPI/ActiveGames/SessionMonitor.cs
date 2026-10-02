using GridGameAPI.ActiveGames.SignalRHubs;
using Microsoft.AspNetCore.SignalR;

namespace GridGameAPI.ActiveGames
{
    /// <summary>Chases turn-ending state pushes that haven't been acknowledged (see GridGameHub). A hub instance only
    /// lives for one invocation, so re-prompting on a timer has to happen out here, through IHubContext.
    ///
    /// Each overdue user is re-sent the current state hash; their client compares it with its own, resyncs if they
    /// differ, and acks. After a few unanswered prompts the user is flagged unresponsive to the other players, and
    /// prompting carries on at a slower pace until they answer or disconnect</summary>
    public class SessionMonitor(GameSessionManager _sessionManager, IHubContext<GridGameHub> _hubContext, ILogger<SessionMonitor> _logger)
        : BackgroundService
    {
        private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan PromptInterval = TimeSpan.FromSeconds(4);
        private static readonly TimeSpan UnresponsivePromptInterval = TimeSpan.FromSeconds(15);
        private const int PromptsBeforeUnresponsive = 3;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(TickInterval);
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                foreach (var session in _sessionManager.AllSessions)
                {
                    try
                    {
                        await PromptOverdueAcks(session, stoppingToken);
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        //One bad session (or a transient send failure) mustn't stop the monitor for every other game
                        _logger.LogWarning(exception, "Failed to prompt state acks for game {GameId}", session.GridGame.Id);
                    }
                }
            }
        }

        private async Task PromptOverdueAcks(GameSession session, CancellationToken stoppingToken)
        {
            var now = DateTime.UtcNow;
            foreach (var (userId, pending) in session.PendingAcks)
            {
                var interval = pending.Prompts >= PromptsBeforeUnresponsive ? UnresponsivePromptInterval : PromptInterval;
                if (now - pending.LastPromptedUtc < interval)
                {
                    continue;
                }

                var connections = session.ConnectionsFor(userId).ToList();
                if (connections.Count == 0)
                {
                    //Disconnected - presence already says so, and they'll resync from scratch when they come back
                    session.PendingAcks.TryRemove(new KeyValuePair<string, PendingAck>(userId, pending));
                    continue;
                }

                var prompted = pending with { LastPromptedUtc = now, Prompts = pending.Prompts + 1 };
                if (!session.PendingAcks.TryUpdate(userId, prompted, pending))
                {
                    //Acked, or superseded by a newer turn end, since this pass started
                    continue;
                }

                await _hubContext.Clients.Clients(connections)
                    .SendAsync("stateAckRequested", new { stateHash = session.StateHash }, stoppingToken);

                if (prompted.Prompts == PromptsBeforeUnresponsive && session.UnresponsiveUsers.TryAdd(userId, 0))
                {
                    await _hubContext.Clients.Clients(session.Connections.Keys.ToList())
                        .SendAsync("presenceUpdated", session.Presence(), stoppingToken);
                }
            }
        }
    }
}
