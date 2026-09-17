namespace GridGameAPI.Configuration
{
    public class JwtSettings
    {
        public required string Key { get; set; }

        //Short-lived on purpose - RefreshTokenManager is what keeps a session going past this without asking for credentials again
        public int AccessTokenExpiryMinutes { get; set; } = 60;

        public int RefreshTokenExpiryDays { get; set; } = 30;
    }
}
