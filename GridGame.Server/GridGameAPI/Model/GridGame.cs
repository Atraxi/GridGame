namespace GridGameAPI.Model
{
    public class GridGame
    {
        public int Id { get; set; }

        public DateTime CreationDate { get; set; }

        public required string Name { get; set; }

        public int TurnNumber { get; set; }
    }
}