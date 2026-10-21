using GridGameAPI.Configuration;
using GridGameAPI.Database;
using GridGameAPI.Model;
using GridGameAPI.UtilityInfrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Security.Claims;

namespace GridGameAPI.Controllers
{
    [ApiController]
    [Route("[controller]/[action]")]
    public class UsersController(
        UserManager<Player> _userManager,
        JwtManager _jwtManager,
        RefreshTokenManager _refreshTokenManager,
        GuestCleanup _guestCleanup,
        GameContext _context,
        PasswordResetManager _passwordResetManager,
        EmailSender _emailSender,
        IOptions<AppSettings> _appSettings,
        IOptions<JwtSettings> _jwtSettings,
        ILogger<UsersController> _logger) : ControllerBase
    {
        private DateTime AccessTokenExpiry => DateTime.UtcNow.AddMinutes(_jwtSettings.Value.AccessTokenExpiryMinutes);

        private async Task<ActionResult> TokenFor(Player player) =>
            Ok(new
            {
                token = _jwtManager.GetToken(player, AccessTokenExpiry),
                refreshToken = await _refreshTokenManager.IssueAsync(player),
                userName = player.UserName,
            });

        [HttpPost]
        public async Task<ActionResult> GuestAuth()
        {
            //A guest is a real Identity user with no password, which is what lets it be promoted in place later without losing its games
            var guest = new Player { UserName = $"{Constants.GuestUserNamePrefix}{Guid.NewGuid():N}" };
            var result = await _userManager.CreateAsync(guest);
            if (!result.Succeeded)
            {
                return BadRequest(string.Join(" ", result.Errors.Select(error => error.Description)));
            }
            _guestCleanup.TriggerIfDue();
            return await TokenFor(guest);
        }

        [Authorize(Policy = AuthPolicy.AnyUser)]
        [HttpPost]
        public async Task<ActionResult> Promote(RegisterModel model)
        {
            //Can be null if the token's user id no longer resolves to a real account - e.g. a stale token left
            //over from before the account was deleted, or the dev DB was reset
            var player = await _userManager.GetUserAsync(User);
            if (player is null)
            {
                return Unauthorized();
            }
            //Every step is checked: a rejected user name (one with a space in it, say) used to be ignored, leaving a
            //"Guest-" account that had a password but still counted as a guest. The password is checked before anything
            //is saved, so a weak one doesn't leave the account half-promoted either
            if (await ValidatePasswordAsync(player, model.Password) is { } invalidPassword)
            {
                return BadRequest(invalidPassword);
            }
            foreach (var step in new Func<Task<IdentityResult>>[]
            {
                () => _userManager.SetUserNameAsync(player, model.UserName),
                () => _userManager.SetEmailAsync(player, model.Email),
                () => _userManager.AddPasswordAsync(player, model.Password),
            })
            {
                if (await step() is { Succeeded: false } failed)
                {
                    return BadRequest(Describe(failed));
                }
            }
            return await TokenFor(player);
        }

        [HttpPost]
        public async Task<ActionResult> Login(LoginModel model)
        {
            var player = await _userManager.FindByNameAsync(model.UserName);
            if (player is null || !await _userManager.CheckPasswordAsync(player, model.Password))
            {
                return Unauthorized("Unknown user name or password");
            }
            return await TokenFor(player);
        }

        /// <summary>Emails a reset link to every named account matching the user name or email address. Answers the
        /// same way whether or not anything matched, so it can't be used to find out whether an account exists</summary>
        [HttpPost]
        public async Task<ActionResult> ForgotPassword(ForgotPasswordModel model)
        {
            var publicUrl = _appSettings.Value.PublicUrl?.TrimEnd('/');
            if (string.IsNullOrEmpty(publicUrl))
            {
                _logger.LogError("App:PublicUrl isn't configured, so password reset links can't be built");
                return NoContent();
            }

            //Queried directly rather than with FindByEmailAsync, which throws when an address is shared - emails aren't
            //required to be unique. Guests have no password to reset, and nowhere to send the link anyway
            var lookup = model.UserNameOrEmail?.Trim() ?? "";
            var normalizedName = _userManager.NormalizeName(lookup);
            var normalizedEmail = _userManager.NormalizeEmail(lookup);
            var players = await _context.Users
                .Where(player => player.NormalizedUserName == normalizedName || player.NormalizedEmail == normalizedEmail)
                .Where(player => player.PasswordHash != null && player.Email != null)
                .Take(10)
                .ToListAsync();

            foreach (var player in players)
            {
                if (await _passwordResetManager.IssueAsync(player) is not { } token)
                {
                    continue;
                }
                _emailSender.Queue(player.Email!, "Reset your GridGame password",
                    $"""
                    Someone asked to reset the password for the GridGame account "{player.UserName}".

                    To choose a new password, open this link within the next hour:
                    {publicUrl}/reset-password?token={token}

                    If that wasn't you, you can ignore this email - your password hasn't changed.
                    """);
            }
            return NoContent();
        }

        /// <summary>Sets a new password from an emailed reset link, then signs in with it. Every other device is signed
        /// out, since a reset usually means the old password can't be trusted</summary>
        [HttpPost]
        public async Task<ActionResult> ResetPassword(ResetPasswordModel model)
        {
            const string unusableLink = "This reset link has expired or has already been used. Ask for a new one.";
            var player = await _passwordResetManager.PeekAsync(model.Token ?? "");
            if (player is null)
            {
                return BadRequest(unusableLink);
            }
            //Checked before the link is used up, so a too-short password doesn't cost the user their link
            if (await ValidatePasswordAsync(player, model.Password) is { } invalidPassword)
            {
                return BadRequest(invalidPassword);
            }
            if (!await _passwordResetManager.ConsumeAsync(model.Token!))
            {
                return BadRequest(unusableLink);
            }

            var removed = await _userManager.RemovePasswordAsync(player);
            var result = removed.Succeeded ? await _userManager.AddPasswordAsync(player, model.Password) : removed;
            if (!result.Succeeded)
            {
                return BadRequest(Describe(result));
            }
            await _refreshTokenManager.RevokeAllAsync(player.Id);
            return await TokenFor(player);
        }

        /// <returns>Why the password isn't acceptable, or null if it is</returns>
        private async Task<string?> ValidatePasswordAsync(Player player, string password)
        {
            foreach (var validator in _userManager.PasswordValidators)
            {
                if (await validator.ValidateAsync(_userManager, player, password) is { Succeeded: false } invalid)
                {
                    return Describe(invalid);
                }
            }
            return null;
        }

        private static string Describe(IdentityResult result) =>
            string.Join(" ", result.Errors.Select(error => error.Description));

        //Deliberately anonymous - the whole point is to mint a new access token once the old one has already expired
        [HttpPost]
        public async Task<ActionResult> Refresh(RefreshModel model)
        {
            var player = await _refreshTokenManager.ConsumeAsync(model.RefreshToken);
            if (player is null)
            {
                return Unauthorized("Refresh token is invalid, expired, or already used");
            }
            return await TokenFor(player);
        }

        /// <summary>Signs this device out by revoking its refresh token. Anonymous for the same reason as Refresh: the
        /// access token may well have expired already, and the refresh token is itself the proof of ownership</summary>
        [HttpPost]
        public async Task<ActionResult> Logout(RefreshModel model)
        {
            await _refreshTokenManager.RevokeAsync(model.RefreshToken);
            return NoContent();
        }

        /// <summary>Signs every device out of this account</summary>
        [Authorize(Policy = AuthPolicy.NamedAccountOnly)]
        [HttpPost]
        public async Task<ActionResult> LogoutEverywhere()
        {
            var player = await _userManager.GetUserAsync(User);
            if (player is null)
            {
                return Unauthorized();
            }
            await _refreshTokenManager.RevokeAllAsync(player.Id);
            return NoContent();
        }

        [Authorize(Policy = AuthPolicy.AnyUser)]
        [HttpGet]
        public async Task<ActionResult> Me()
        {
            var player = await _userManager.GetUserAsync(User);
            if (player is null)
            {
                return Unauthorized();
            }
            return Ok(new
            {
                player.UserName,
                player.GamesPlayed,
                player.GamesWon,
                player.GamesLost,
            });
        }

        /// <summary>Named accounts this player has played a game with, most recent first - the people an invite-only
        /// game can be opened to (see GamesController.CreateFromMap)</summary>
        [Authorize(Policy = AuthPolicy.NamedAccountOnly)]
        [HttpGet]
        public async Task<ActionResult> Opponents()
        {
            var playerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var opponents = await _context.PastOpponents
                .Where(record => record.PlayerId == playerId)
                .OrderByDescending(record => record.LastPlayedAt)
                .Select(record => new { id = record.OpponentId, userName = record.Opponent!.UserName, record.LastPlayedAt })
                .ToListAsync();
            return Ok(opponents);
        }

        /// <summary>Takes someone off this player's own past-opponents list. They aren't told, stay on their own list
        /// as they were, and are added back if the two play together again</summary>
        [Authorize(Policy = AuthPolicy.NamedAccountOnly)]
        [HttpPost]
        public async Task<ActionResult> RemoveOpponent(RemoveOpponentModel model)
        {
            var playerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            await _context.PastOpponents
                .Where(record => record.PlayerId == playerId && record.OpponentId == model.OpponentId)
                .ExecuteDeleteAsync();
            return NoContent();
        }
    }
}
