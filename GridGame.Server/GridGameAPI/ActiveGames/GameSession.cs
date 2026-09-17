using GridGameAPI.Model;

namespace GridGameAPI.ActiveGames
{
    public class GameSession
    {
        public Dictionary<string, string> Connections { get; set; } = [];

        /// <summary>Connection ids belonging to named (non-guest) accounts - live score prediction is a named-account
        /// perk like final scores, so it's only ever broadcast to these</summary>
        public HashSet<string> NamedAccountConnections { get; set; } = [];

        /// <summary>Player number each user owns tiles as, assigned in join order</summary>
        public Dictionary<string, int> PlayerNumbers { get; set; } = [];

        public required GridGame GridGame { get; set; }

        /// <summary>Guards board mutation and the reachability/scoring analysis that follows it - SignalR does not
        /// serialize concurrent hub invocations, and BFS work over the whole board is expensive enough that two
        /// overlapping moves could otherwise race</summary>
        public SemaphoreSlim MoveLock { get; } = new(1, 1);
    }
}