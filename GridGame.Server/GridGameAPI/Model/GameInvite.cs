namespace GridGameAPI.Model
{
    /// <summary>One account allowed to take a seat in an invite-only game (see GridGame.IsInviteOnly). The host gets
    /// a row too. Anyone else can still open the game, but only to spectate</summary>
    public class GameInvite
    {
        public int GridGameId { get; set; }

        /// <summary>Lets an invite be saved along with a brand new game, before the game has an id</summary>
        public GridGame? Game { get; set; }

        public required string PlayerId { get; set; }
    }
}
