namespace GridGameAPI.Model
{
    /// <summary>A seat its holder has locked to their account (see GridGameHub.LockSeat). Seats are otherwise handed
    /// out in join order and only held in memory, so whoever opens a game first after its session has been dropped
    /// could take an unlocked one. Locked seats are restored to their owner whenever the session is reloaded, and
    /// nobody else is ever given them. Guests are locked once they've moved, which is the record a named account can
    /// claim at login (UsersController.ClaimGuest); GuestCleanup deletes a lapsed guest and its seats with it</summary>
    public class GameSeat
    {
        public int GridGameId { get; set; }

        public int PlayerNumber { get; set; }

        public required string PlayerId { get; set; }

        public Player? Player { get; set; }
    }
}
