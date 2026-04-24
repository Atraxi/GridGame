namespace GridGameAPI.Model
{
    public class GameSummary
    {
        public int Id { get; set; }

        public DateTime CreationDate { get; set; }

        public required string Name { get; set; }

        public int TurnNumber { get; set; }
    }
}
