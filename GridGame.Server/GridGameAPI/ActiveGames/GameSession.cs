using GridGameAPI.Model;

namespace GridGameAPI.ActiveGames
{
    public class GameSession
    {
        public Dictionary<string, int> Connections { get; set; } = [];

        public required GridGame GridGame { get; set; }
    }
}