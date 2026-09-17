namespace GridGameAPI.Model
{
    /// <summary>A reusable board design a player can host games from. Only the design and its owner exist so far -
    /// hosting a GridGame from one, and the map editor itself, are later work</summary>
    public class Map
    {
        public int Id { get; set; }

        public DateTime CreationDate { get; set; }

        public required string Name { get; set; }

        public required string DesignedByPlayerId { get; set; }

        public Player? DesignedBy { get; set; }

        public required int PlayerCount { get; set; }

        public int ActionsPerTurn { get; set; } = 3;

        public required int[,] StartingBoard { get; set; }
    }
}
