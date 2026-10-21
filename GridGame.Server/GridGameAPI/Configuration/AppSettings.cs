namespace GridGameAPI.Configuration
{
    public class AppSettings
    {
        /// <summary>The address players use, e.g. https://grid.example.com - links in emails are built from this. Not
        /// taken from the request: the Cloudflare proxy rewrites Host to the azurewebsites.net name, and a header an
        /// attacker controls mustn't decide where a password reset link points</summary>
        public string? PublicUrl { get; set; }
    }
}
