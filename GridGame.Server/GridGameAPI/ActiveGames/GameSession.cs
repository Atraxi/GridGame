using GridGameAPI.Model;
using System.Collections.Concurrent;

namespace GridGameAPI.ActiveGames
{
    /// <summary>A turn-ending state push some seated user hasn't yet confirmed receiving (see GridGameHub.AcknowledgeState
    /// and SessionMonitor). Immutable so the hub and the monitor can swap it atomically in PendingAcks</summary>
    public record PendingAck(uint ExpectedHash, DateTime LastPromptedUtc, int Prompts);

    public class GameSession
    {
        /// <summary>Connection id -> user id. Concurrent because SessionMonitor reads it from a background thread
        /// while hub calls add and remove connections</summary>
        public ConcurrentDictionary<string, string> Connections { get; } = new();

        /// <summary>Connection ids belonging to named (non-guest) accounts - live board analysis is a named-account
        /// perk like final scores, so it's only ever broadcast to these</summary>
        public ConcurrentDictionary<string, byte> NamedAccountConnections { get; } = new();

        /// <summary>Player number each user owns tiles as. Locked seats (see GameSeat) are filled in when the session
        /// is loaded; the rest are handed out in join order. Only mutated under MembershipLock</summary>
        public ConcurrentDictionary<string, int> PlayerNumbers { get; } = new();

        /// <summary>Everyone who has connected since this session was loaded. A locked seat's holder who isn't among
        /// them is away, rather than disconnected</summary>
        public ConcurrentDictionary<string, byte> SeenUsers { get; } = new();

        /// <summary>Users whose seat is locked to their account (a subset of PlayerNumbers' keys)</summary>
        public ConcurrentDictionary<string, byte> LockedSeats { get; } = new();

        /// <summary>For an invite-only game, the users allowed to take a seat. Null when anyone may</summary>
        public required IReadOnlySet<string>? InvitedUserIds { get; init; }

        /// <summary>Users known to be on named accounts. Only they get a past-opponents history (see
        /// GridGameHub.RecordOpponents)</summary>
        public ConcurrentDictionary<string, byte> NamedUsers { get; } = new();

        /// <summary>Named users who have made a move since this session was loaded, or held a locked seat they'd
        /// already moved from - the ones who count as having played each other. Only mutated under MoveLock</summary>
        public HashSet<string> ActiveNamedUsers { get; } = [];

        /// <summary>Display name per user id, captured from their token when they join</summary>
        public ConcurrentDictionary<string, string> UserNames { get; } = new();

        /// <summary>Seated users owed an ack for the last turn-ending push, keyed by user id rather than connection
        /// id: token rotation briefly gives one user two connections, and either one acking is enough</summary>
        public ConcurrentDictionary<string, PendingAck> PendingAcks { get; } = new();

        /// <summary>Seated users who are connected but have stopped acknowledging state pushes</summary>
        public ConcurrentDictionary<string, byte> UnresponsiveUsers { get; } = new();

        public required GridGame GridGame { get; set; }

        /// <summary>StateHash.Compute of GridGame as of the last applied change. Only written under MoveLock</summary>
        public uint StateHash { get; set; }

        /// <summary>BoardAnalysis of the current board, recomputed after every move. Only written under MoveLock</summary>
        public BoardAnalysis.Result? Analysis { get; set; }

        /// <summary>Guards board mutation and the reachability/scoring analysis that follows it - SignalR does not
        /// serialize concurrent hub invocations, and BFS work over the whole board is expensive enough that two
        /// overlapping moves could otherwise race</summary>
        public SemaphoreSlim MoveLock { get; } = new(1, 1);

        /// <summary>Guards joining/leaving against the session being torn down (see GameSessionManager)</summary>
        public object MembershipLock { get; } = new();

        /// <summary>Set (under MembershipLock) once this session has been persisted and dropped from the manager - a
        /// join that raced the teardown must start a fresh session from the database instead of joining this one</summary>
        public bool IsClosed { get; set; }

        public IEnumerable<string> ConnectionsFor(string userId) =>
            Connections.Where(connection => connection.Value == userId).Select(connection => connection.Key);

        /// <summary>Gives the user the lowest-numbered free seat, unless they already have one, the game is invite-only
        /// and they aren't invited, the game is over, or every seat is taken. Call under MembershipLock</summary>
        public void TryTakeSeat(string userId)
        {
            //Nobody new is seated once the game is over: a seat is what lets someone start a rematch (see
            //GridGameHub.Rematch) and be pulled into it, so it has to mean they actually played. Whoever held one when
            //the game ended keeps it for as long as the session stays loaded
            if (PlayerNumbers.ContainsKey(userId) || InvitedUserIds?.Contains(userId) == false || GridGame.IsGameOver)
            {
                return;
            }
            //Lowest free rather than Count + 1: a locked seat restored at load can leave a gap below it
            var taken = PlayerNumbers.Values.ToHashSet();
            var free = Enumerable.Range(1, GridGame.PlayerCount).FirstOrDefault(seat => !taken.Contains(seat));
            if (free != 0)
            {
                PlayerNumbers[userId] = free;
            }
        }

        /// <summary>One entry per seat, for the "player disconnected / not responding" notices</summary>
        public object[] Presence()
        {
            var connectedUsers = Connections.Values.ToHashSet();
            var seatHolders = PlayerNumbers.ToDictionary(seat => seat.Value, seat => seat.Key);
            return Enumerable.Range(1, GridGame.PlayerCount).Select(playerNumber =>
            {
                if (!seatHolders.TryGetValue(playerNumber, out var userId))
                {
                    return (object)new { playerNumber, userName = (string?)null, status = "open", locked = false };
                }
                var status = !connectedUsers.Contains(userId) ? (SeenUsers.ContainsKey(userId) ? "disconnected" : "away")
                    : UnresponsiveUsers.ContainsKey(userId) ? "unresponsive"
                    : "connected";
                return (object)new { playerNumber, userName = UserNames.GetValueOrDefault(userId), status, locked = LockedSeats.ContainsKey(userId) };
            }).ToArray();
        }
    }
}
