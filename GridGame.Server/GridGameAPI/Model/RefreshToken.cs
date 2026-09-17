namespace GridGameAPI.Model
{
    /// <summary>A long-lived opaque credential that lets a client mint a new access token without re-authenticating.
    /// Works identically for guest and named Players, since both are just Identity users underneath</summary>
    public class RefreshToken
    {
        public int Id { get; set; }

        /// <summary>SHA-256 hash of the actual token - only the hash is ever persisted, so a DB leak can't be replayed directly</summary>
        public required string TokenHash { get; set; }

        public required string PlayerId { get; set; }

        public Player? Player { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime ExpiresAt { get; set; }

        /// <summary>Set once this token has been used to mint a new one (rotation) - a refresh token is single-use</summary>
        public DateTime? RevokedAt { get; set; }
    }
}
