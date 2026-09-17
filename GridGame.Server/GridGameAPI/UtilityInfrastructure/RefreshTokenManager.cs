using GridGameAPI.Configuration;
using GridGameAPI.Database;
using GridGameAPI.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;

namespace GridGameAPI.UtilityInfrastructure
{
    public class RefreshTokenManager(GameContext _context, IOptions<JwtSettings> _jwtSettings)
    {
        public async Task<string> IssueAsync(Player player)
        {
            var rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

            _context.RefreshTokens.Add(new RefreshToken
            {
                TokenHash = Hash(rawToken),
                PlayerId = player.Id,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(_jwtSettings.Value.RefreshTokenExpiryDays),
            });
            await _context.SaveChangesAsync();

            return rawToken;
        }

        /// <summary>Redeems a refresh token for the Player it belongs to, revoking it in the same call since each one is single-use</summary>
        /// <returns>The owning Player, or null if the token is unknown, expired, or already used</returns>
        public async Task<Player?> ConsumeAsync(string rawToken)
        {
            var hash = Hash(rawToken);
            var stored = await _context.RefreshTokens
                .Include(refreshToken => refreshToken.Player)
                .SingleOrDefaultAsync(refreshToken => refreshToken.TokenHash == hash);

            if (stored is null || stored.RevokedAt is not null || stored.ExpiresAt < DateTime.UtcNow)
            {
                return null;
            }

            stored.RevokedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return stored.Player;
        }

        private static string Hash(string rawToken) =>
            Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
    }
}
