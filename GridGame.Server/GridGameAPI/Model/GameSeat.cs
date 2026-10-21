namespace GridGameAPI.Model
{
    /// <summary>A seat its holder has locked to their account (see GridGameHub.LockSeat). Seats are otherwise handed
    /// out in join order and only held in memory, so whoever opens a game first after its session has been dropped
    /// could take an unlocked one. Locked seats are restored to their owner whenever the session is reloaded, and
    /// nobody else is ever given them. Named accounts only, so a lock can't be left behind by a throwaway guest</summary>
    public class GameSeat
    {
        public int GridGameId { get; set; }

        public int PlayerNumber { get; set; }

        public required string PlayerId { get; set; }

        public Player? Player { get; set; }
    }
}
