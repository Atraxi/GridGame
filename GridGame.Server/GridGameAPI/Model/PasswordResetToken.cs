namespace GridGameAPI.Model
{
    /// <summary>A single-use "forgot password" link, emailed to the account's address (see PasswordResetManager).
    /// Stored rather than using Identity's DataProtection-based tokens, which would stop validating if the free App
    /// Service plan's key ring didn't survive the app being unloaded between the email being sent and the link opened</summary>
    public class PasswordResetToken
    {
        public int Id { get; set; }

        /// <summary>SHA-256 hash of the token in the link - only the hash is persisted, same as RefreshToken</summary>
        public required string TokenHash { get; set; }

        public required string PlayerId { get; set; }

        public Player? Player { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime ExpiresAt { get; set; }

        /// <summary>Set once the link has been used to choose a new password</summary>
        public DateTime? UsedAt { get; set; }
    }
}
