namespace GridGameAPI.Model
{
    /// <summary>A reusable board design a player can host games from. A game copies the board when it's created, so
    /// editing a map afterwards never changes games already hosted from it</summary>
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

        /// <summary>Unfinished: saved without the playability checks, visible only to its designer, and not offered
        /// for hosting until it's published</summary>
        public bool IsDraft { get; set; }
    }
}
