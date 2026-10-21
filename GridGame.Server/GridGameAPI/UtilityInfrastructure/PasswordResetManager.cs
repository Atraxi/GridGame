using GridGameAPI.Database;
using GridGameAPI.Model;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace GridGameAPI.UtilityInfrastructure
{
    public class PasswordResetManager(GameContext _context)
    {
        private static readonly TimeSpan Lifetime = TimeSpan.FromHours(1);

        /// <summary>At most one email per account in this window, so the form can't be used to flood someone's inbox</summary>
        private static readonly TimeSpan Throttle = TimeSpan.FromMinutes(2);

        /// <returns>The raw token to put in the link, or null if one was issued for this player too recently</returns>
        public async Task<string?> IssueAsync(Player player)
        {
            var now = DateTime.UtcNow;
            if (await _context.PasswordResetTokens.AnyAsync(token => token.PlayerId == player.Id && token.CreatedAt > now - Throttle))
            {
                return null;
            }

            //Url-safe, since it travels in a query string
            var rawToken = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
            _context.PasswordResetTokens.Add(new PasswordResetToken
            {
                TokenHash = Hash(rawToken),
                PlayerId = player.Id,
                CreatedAt = now,
                ExpiresAt = now + Lifetime,
            });
            await _context.SaveChangesAsync();
            return rawToken;
        }

        /// <summary>The player a still-usable token belongs to, without using it up - lets the new password be checked
        /// first, so a too-short password doesn't cost the user their link</summary>
        public async Task<Player?> PeekAsync(string rawToken) =>
            (await FindUsableAsync(rawToken))?.Player;

        /// <summary>Marks the token used, along with any other outstanding ones for the same player</summary>
        /// <returns>False if it was already used or has expired meanwhile</returns>
        public async Task<bool> ConsumeAsync(string rawToken)
        {
            var stored = await FindUsableAsync(rawToken);
            if (stored is null)
            {
                return false;
            }
            var now = DateTime.UtcNow;
            await _context.PasswordResetTokens
                .Where(token => token.PlayerId == stored.PlayerId && token.UsedAt == null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(token => token.UsedAt, now));
            return true;
        }

        /// <summary>Drops tokens that can no longer be used - run alongside GuestCleanup's own housekeeping</summary>
        public static Task<int> DeleteExpiredAsync(GameContext context, DateTime cutoff) =>
            context.PasswordResetTokens.Where(token => token.ExpiresAt < cutoff).ExecuteDeleteAsync();

        private async Task<PasswordResetToken?> FindUsableAsync(string rawToken)
        {
            var hash = Hash(rawToken);
            var stored = await _context.PasswordResetTokens
                .Include(token => token.Player)
                .SingleOrDefaultAsync(token => token.TokenHash == hash);
            return stored is null || stored.UsedAt is not null || stored.ExpiresAt < DateTime.UtcNow ? null : stored;
        }

        private static string Hash(string rawToken) =>
            Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
    }
}
