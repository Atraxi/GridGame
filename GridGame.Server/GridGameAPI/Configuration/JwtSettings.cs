namespace GridGameAPI.Configuration
{
    public class JwtSettings
    {
        public required string Key { get; set; }

        //Short-lived on purpose - RefreshTokenManager is what keeps a session going past this without asking for credentials again
        public int AccessTokenExpiryMinutes { get; set; } = 60;

        //Long-lived, since it's what keeps someone signed in: each refresh rotates it and restarts the clock, so this
        //is really "how long an unused device stays signed in". Revocable (see RefreshTokenManager), and a guest whose
        //last one has lapsed can never be signed back into, which is what GuestCleanup keys off
        public int RefreshTokenExpiryDays { get; set; } = 180;
    }
}
