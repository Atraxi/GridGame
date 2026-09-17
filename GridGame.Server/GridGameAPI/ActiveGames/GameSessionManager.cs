using System.Collections.Concurrent;

namespace GridGameAPI.ActiveGames
{
    public class GameSessionManager
    {
        private readonly ConcurrentDictionary<int, GameSession> _sessions = new();

        public GameSession GetByGameIdOrCreate(int gameId, Func<int, GameSession> newSessionCreator) =>
            _sessions.GetOrAdd(gameId, newSessionCreator);

        public GameSession? GetByGameId(int gameId) =>
            _sessions.GetValueOrDefault(gameId);

        /// <returns>The session containing this connection, or null if it disconnected/was called before ever registering via OnConnectedToGame</returns>
        public GameSession? GetByConnectionId(string connectionId) =>
            //TODO this feels a bit messy, find a cleaner data structure?
            _sessions.FirstOrDefault(sessionsKeyPair =>
                sessionsKeyPair.Value.Connections.Any(connectionKeyPair => connectionKeyPair.Key == connectionId)).Value;

        internal void Remove(int gameId) =>
            _sessions.Remove(gameId, out var _);
    }
}