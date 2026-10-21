namespace GridGameAPI.Model
{
    /// <summary>Another named account this player has played a game with - who they can pick from when hosting an
    /// invite-only game. Recorded in both directions once both have made a move in the same game (see
    /// GridGameHub.RecordOpponents), but removed one direction at a time: removing someone only takes them off your own
    /// list, and they come back if the two of you play together again</summary>
    public class PastOpponent
    {
        public required string PlayerId { get; set; }

        public required string OpponentId { get; set; }

        public Player? Opponent { get; set; }

        public DateTime LastPlayedAt { get; set; }
    }
}
