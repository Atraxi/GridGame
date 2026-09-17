using System.Text.Json.Serialization;

namespace GridGameAPI.Model
{
    public class GridGame
    {
        public int Id { get; set; }

        public DateTime CreationDate { get; set; }

        public required string Name { get; set; }

        public int TurnNumber { get; set; }

        /// <summary>Fixed number of seats, set when the map/game is created</summary>
        public required int PlayerCount { get; set; }

        /// <summary>How many actions (spawns/jumps) each player gets per turn, set when the map/game is created</summary>
        public int ActionsPerTurn { get; set; } = 3;

        public int CurrentPlayerNumber { get; set; } = 1;

        public int ActionsRemainingInTurn { get; set; } = 3;

        public required IEnumerable<Player> Players { get; set; }

        public required int[,] GameBoard {  get; set; }

        public bool IsGameOver { get; set; }

        /// <summary>Final tile count per player (index 0 = player 1), set once when IsGameOver first becomes true.
        /// Deliberately excluded from the general JSON representation (GetGame, SignalR broadcasts) - this and other
        /// meta score stats are a named-account-only feature, served only via GamesController.GetFinalScores</summary>
        [JsonIgnore]
        public int[]? FinalScores { get; set; }
    }
}