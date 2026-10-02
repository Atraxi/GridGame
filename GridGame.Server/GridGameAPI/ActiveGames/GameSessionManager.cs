using System.Collections.Concurrent;

namespace GridGameAPI.ActiveGames
{
    public class GameSessionManager
    {
        private readonly ConcurrentDictionary<int, GameSession> _sessions = new();

        private readonly ConcurrentDictionary<string, GameSession> _sessionsByConnection = new();

        public IEnumerable<GameSession> AllSessions => _sessions.Values;

        public GameSession? GetByGameId(int gameId) =>
            _sessions.GetValueOrDefault(gameId);

        /// <returns>The session containing this connection, or null if it disconnected/was called before ever registering via Join</returns>
        public GameSession? GetByConnectionId(string connectionId) =>
            _sessionsByConnection.GetValueOrDefault(connectionId);

        /// <summary>Registers a connection with the game's live session, loading the session from the database if
        /// there isn't one. Retries if it lands on a session that is mid-teardown (see TryClose): that session's
        /// state has already been persisted, so a fresh load picks it up instead of joining something about to vanish</summary>
        /// <param name="onJoined">Runs under the session's MembershipLock, so seat assignment can't race another join</param>
        public GameSession Join(int gameId, string connectionId, Func<int, GameSession> newSessionCreator, Action<GameSession> onJoined)
        {
            while (true)
            {
                var session = _sessions.GetOrAdd(gameId, newSessionCreator);
                lock (session.MembershipLock)
                {
                    if (session.IsClosed)
                    {
                        continue;
                    }
                    _sessionsByConnection[connectionId] = session;
                    onJoined(session);
                    return session;
                }
            }
        }

        /// <returns>The session the connection belonged to, or null if it never joined one</returns>
        public GameSession? Leave(string connectionId)
        {
            if (!_sessionsByConnection.TryRemove(connectionId, out var session))
            {
                return null;
            }
            lock (session.MembershipLock)
            {
                session.Connections.TryRemove(connectionId, out _);
                session.NamedAccountConnections.TryRemove(connectionId, out _);
            }
            return session;
        }

        /// <summary>Drops the session if it is still empty. Call only after persisting it, and while holding its
        /// MoveLock so no move can land between the save and the removal</summary>
        /// <returns>False if someone joined in the meantime, in which case the session stays live</returns>
        public bool TryClose(GameSession session)
        {
            lock (session.MembershipLock)
            {
                if (!session.Connections.IsEmpty)
                {
                    return false;
                }
                session.IsClosed = true;
                _sessions.TryRemove(new KeyValuePair<int, GameSession>(session.GridGame.Id, session));
                return true;
            }
        }
    }
}
