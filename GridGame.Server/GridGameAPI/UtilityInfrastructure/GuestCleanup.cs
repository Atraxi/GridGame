using GridGameAPI.Database;
using Microsoft.EntityFrameworkCore;

namespace GridGameAPI.UtilityInfrastructure
{
    /// <summary>Deletes guest accounts that can never be signed back into, plus refresh-token rows that can no longer
    /// be used. A guest has no password, so once its last refresh token has lapsed (or been revoked) nothing can ever
    /// authenticate as it again.
    ///
    /// Event-driven rather than on a timer: the free App Service plan unloads an idle app, so a timer can't be relied
    /// on anyway. It runs at startup and whenever a new guest is minted (the thing that makes guests pile up), at most
    /// once per RunInterval, in the background on its own DI scope</summary>
    public class GuestCleanup(IServiceScopeFactory _scopeFactory, ILogger<GuestCleanup> _logger)
    {
        private static readonly TimeSpan RunInterval = TimeSpan.FromHours(24);

        /// <summary>Slack past a token's expiry before its guest is treated as unrecoverable - covers the access
        /// token minted alongside the last refresh token, and clock skew</summary>
        private static readonly TimeSpan Grace = TimeSpan.FromDays(1);

        /// <summary>Revoked tokens are kept this long after revocation - enough to investigate a refresh-token reuse
        /// report, should reuse detection ever be added</summary>
        private static readonly TimeSpan RevokedTokenRetention = TimeSpan.FromDays(30);

        private const int BatchSize = 500;

        private long _lastRunTicks = DateTime.MinValue.Ticks;

        public void TriggerIfDue()
        {
            var now = DateTime.UtcNow;
            var last = Interlocked.Read(ref _lastRunTicks);
            if (now.Ticks - last < RunInterval.Ticks ||
                Interlocked.CompareExchange(ref _lastRunTicks, now.Ticks, last) != last)
            {
                return;
            }
            _ = Task.Run(RunAsync);
        }

        private async Task RunAsync()
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<GameContext>();
                var cutoff = DateTime.UtcNow - Grace;

                var guestsDeleted = 0;
                int deleted;
                do
                {
                    //Guest-ness is checked by both the name prefix and the absence of a password, so a named account
                    //that happens to have chosen a "Guest-..." user name can never be swept up. Identity's own tables
                    //and RefreshTokens cascade on delete
                    deleted = await context.Users
                        .Where(player => player.UserName!.StartsWith(Constants.GuestUserNamePrefix) && player.PasswordHash == null)
                        .Where(player => !context.RefreshTokens.Any(token =>
                            token.PlayerId == player.Id && token.RevokedAt == null && token.ExpiresAt > cutoff))
                        .OrderBy(player => player.Id)
                        .Take(BatchSize)
                        .ExecuteDeleteAsync();
                    guestsDeleted += deleted;
                } while (deleted == BatchSize);

                var revokedCutoff = DateTime.UtcNow - RevokedTokenRetention;
                var tokensDeleted = await context.RefreshTokens
                    .Where(token => token.ExpiresAt < cutoff || (token.RevokedAt != null && token.RevokedAt < revokedCutoff))
                    .ExecuteDeleteAsync();

                _logger.LogInformation("Guest cleanup removed {Guests} unrecoverable guest accounts and {Tokens} dead refresh tokens",
                    guestsDeleted, tokensDeleted);
            }
            catch (Exception exception)
            {
                //Best-effort housekeeping - let the next trigger retry rather than waiting out the full interval
                Interlocked.Exchange(ref _lastRunTicks, DateTime.MinValue.Ticks);
                _logger.LogWarning(exception, "Guest cleanup failed");
            }
        }
    }
}
