namespace GridGameAPI.Model
{
    public class GridGame
    {
        public int Id { get; set; }

        public DateTime CreationDate { get; set; }

        public required string Name { get; set; }

        public int TurnNumber { get; set; }

        public required IEnumerable<Player> Players { get; set; }

        //public required int[,] GameBoard {  get; set; }
    }
}