namespace GridGameAPI.Model
{
    public class MapSummary
    {
        public int Id { get; set; }

        public DateTime CreationDate { get; set; }

        public required string Name { get; set; }

        public int PlayerCount { get; set; }

        public int ActionsPerTurn { get; set; }

        public required string DesignedByUserName { get; set; }
    }
}
