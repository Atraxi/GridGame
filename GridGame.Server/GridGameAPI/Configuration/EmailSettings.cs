namespace GridGameAPI.Configuration
{
    /// <summary>Plain SMTP, so any provider works by configuration alone (see DEPLOYMENT.md). With no Host configured,
    /// emails are written to the log instead of sent - which is how local development gets its reset links</summary>
    public class EmailSettings
    {
        public string? Host { get; set; }

        /// <summary>587 with STARTTLS. Azure blocks outbound port 25, so that one won't work</summary>
        public int Port { get; set; } = 587;

        public string? UserName { get; set; }

        public string? Password { get; set; }

        public string? FromAddress { get; set; }

        public string FromName { get; set; } = "GridGame";
    }
}
