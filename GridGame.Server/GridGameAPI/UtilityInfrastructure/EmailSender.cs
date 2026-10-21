using GridGameAPI.Configuration;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Mail;

namespace GridGameAPI.UtilityInfrastructure
{
    public class EmailSender(IOptions<EmailSettings> _settings, ILogger<EmailSender> _logger)
    {
        /// <summary>Sends in the background: the caller answers the same way whether or not an account matched, so
        /// it mustn't take noticeably longer when one did. Failures are only logged</summary>
        public void Queue(string toAddress, string subject, string body) =>
            _ = Task.Run(async () =>
            {
                try
                {
                    await SendAsync(toAddress, subject, body);
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Couldn't send \"{Subject}\" email", subject);
                }
            });

        private async Task SendAsync(string toAddress, string subject, string body)
        {
            var settings = _settings.Value;
            if (string.IsNullOrEmpty(settings.Host))
            {
                _logger.LogWarning("No SMTP host is configured (Email:Host), so this email wasn't sent. To {To}: {Subject}\n{Body}",
                    toAddress, subject, body);
                return;
            }

            using var message = new MailMessage(
                new MailAddress(settings.FromAddress ?? settings.UserName!, settings.FromName),
                new MailAddress(toAddress))
            {
                Subject = subject,
                Body = body,
            };
            using var client = new SmtpClient(settings.Host, settings.Port)
            {
                //STARTTLS on 587 - SmtpClient doesn't do implicit TLS (465)
                EnableSsl = true,
                Credentials = new NetworkCredential(settings.UserName, settings.Password),
            };
            await client.SendMailAsync(message);
        }
    }
}
