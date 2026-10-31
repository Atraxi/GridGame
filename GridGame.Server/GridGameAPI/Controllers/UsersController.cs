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
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GridGameAPI.Controllers
{
    [ApiController]
    [Route("[controller]/[action]")]
    public partial class UsersController(
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
                player.ColourPrimary,
                player.ColourSecondary,
                player.UseDefaultColours,
                DefaultPalette = player.DefaultPalette is null ? null : JsonSerializer.Deserialize<PaletteSlot[]>(player.DefaultPalette, PaletteJson),
            });
        }

        [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
        private static partial Regex HexColour();

        private static readonly JsonSerializerOptions PaletteJson = new(JsonSerializerDefaults.Web);

        /// <summary>Rebinds the colour this player sees each seat in by default (everywhere, tutorials included). Eight
        /// slots, each a colour and optionally a second for a gradient; no slots resets to the stock palette</summary>
        [Authorize(Policy = AuthPolicy.AnyUser)]
        [HttpPost]
        public async Task<ActionResult> SetDefaultPalette(SetDefaultPaletteModel model)
        {
            var player = await _userManager.GetUserAsync(User);
            if (player is null)
            {
                return Unauthorized();
            }
            if (model.Slots is { } slots && (slots.Length != 8 ||
                slots.Any(slot => !HexColour().IsMatch(slot.Primary ?? "") || (slot.Secondary is not null && !HexColour().IsMatch(slot.Secondary)))))
            {
                return BadRequest("A palette is 8 colours like #1a2b3c.");
            }
            player.DefaultPalette = model.Slots is null ? null : JsonSerializer.Serialize(model.Slots, PaletteJson);
            var result = await _userManager.UpdateAsync(player);
            return result.Succeeded ? NoContent() : BadRequest(Describe(result));
        }

        /// <summary>Chosen tile colours (a second one makes a gradient) and whether to see everyone in default colours.
        /// Open to guests too: it's a display preference, not a privilege</summary>
        [Authorize(Policy = AuthPolicy.AnyUser)]
        [HttpPost]
        public async Task<ActionResult> SetColours(SetColoursModel model)
        {
            var player = await _userManager.GetUserAsync(User);
            if (player is null)
            {
                return Unauthorized();
            }
            var primary = string.IsNullOrWhiteSpace(model.Primary) ? null : model.Primary.Trim().ToLowerInvariant();
            //A gradient needs two colours, so a lone secondary is meaningless
            var secondary = primary is null || string.IsNullOrWhiteSpace(model.Secondary) ? null : model.Secondary.Trim().ToLowerInvariant();
            if ((primary is not null && !HexColour().IsMatch(primary)) || (secondary is not null && !HexColour().IsMatch(secondary)))
            {
                return BadRequest("Colours must look like #1a2b3c.");
            }
            player.ColourPrimary = primary;
            player.ColourSecondary = secondary;
            player.UseDefaultColours = model.UseDefaultColours;
            var result = await _userManager.UpdateAsync(player);
            return result.Succeeded ? NoContent() : BadRequest(Describe(result));
        }

        /// <summary>Folds another named account into the one signed in: its seats, invites, maps, past opponents and stats
        /// move across and it is deleted, freeing its user name. Needs that account's password, so it can only be done by
        /// whoever holds both. Irreversible. Anything it has in a game being played right now stays with the old id until
        /// that game's session closes</summary>
        [Authorize(Policy = AuthPolicy.NamedAccountOnly)]
        [HttpPost]
        public async Task<ActionResult> MergeAccount(MergeAccountModel model)
        {
            var target = await _userManager.GetUserAsync(User);
            if (target is null)
            {
                return Unauthorized();
            }
            var source = await _userManager.FindByNameAsync(model.UserName?.Trim() ?? "");
            if (source is null || source.PasswordHash is null || !await _userManager.CheckPasswordAsync(source, model.Password ?? ""))
            {
                return BadRequest("Unknown user name or password for the other account.");
            }
            if (source.Id == target.Id)
            {
                return BadRequest("That's the account you're signed in to.");
            }

            var failure = (IdentityResult?)null;
            await _context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var transaction = await _context.Database.BeginTransactionAsync();
                var (from, to) = (source.Id, target.Id);

                //Where both accounts held a seat or invite in the same game the target's is kept
                await _context.GameSeats
                    .Where(seat => seat.PlayerId == from && !_context.GameSeats.Any(other => other.GridGameId == seat.GridGameId && other.PlayerId == to))
                    .ExecuteUpdateAsync(set => set.SetProperty(seat => seat.PlayerId, to));
                await _context.GameInvites
                    .Where(invite => invite.PlayerId == from && !_context.GameInvites.Any(other => other.GridGameId == invite.GridGameId && other.PlayerId == to))
                    .ExecuteUpdateAsync(set => set.SetProperty(invite => invite.PlayerId, to));
                await _context.Maps
                    .Where(map => map.DesignedByPlayerId == from)
                    .ExecuteUpdateAsync(set => set.SetProperty(map => map.DesignedByPlayerId, to));

                //Past opponents are keyed by pair, so re-pointing rows could collide - merge them in memory instead
                var sourceRecords = await _context.PastOpponents.Where(record => record.PlayerId == from || record.OpponentId == from).ToListAsync();
                var targetRecords = await _context.PastOpponents.Where(record => record.PlayerId == to || record.OpponentId == to).ToListAsync();
                foreach (var record in sourceRecords)
                {
                    var (playerId, opponentId) = (record.PlayerId == from ? to : record.PlayerId, record.OpponentId == from ? to : record.OpponentId);
                    if (playerId == opponentId)
                    {
                        continue;
                    }
                    var existing = targetRecords.FirstOrDefault(other => other.PlayerId == playerId && other.OpponentId == opponentId);
                    if (existing is null)
                    {
                        var moved = new PastOpponent { PlayerId = playerId, OpponentId = opponentId, LastPlayedAt = record.LastPlayedAt };
                        _context.PastOpponents.Add(moved);
                        targetRecords.Add(moved);
                    }
                    else if (record.LastPlayedAt > existing.LastPlayedAt)
                    {
                        existing.LastPlayedAt = record.LastPlayedAt;
                    }
                }
                _context.PastOpponents.RemoveRange(sourceRecords);
                await _context.SaveChangesAsync();

                target.GamesPlayed += source.GamesPlayed;
                target.GamesWon += source.GamesWon;
                target.GamesLost += source.GamesLost;
                failure = await _userManager.UpdateAsync(target);
                if (failure.Succeeded)
                {
                    //Seats and invites it didn't pass on (clashes) go with it
                    await _context.GameSeats.Where(seat => seat.PlayerId == from).ExecuteDeleteAsync();
                    await _context.GameInvites.Where(invite => invite.PlayerId == from).ExecuteDeleteAsync();
                    await _refreshTokenManager.RevokeAllAsync(from);
                    failure = await _userManager.DeleteAsync(source);
                }
                if (failure.Succeeded)
                {
                    await transaction.CommitAsync();
                }
            });
            return failure is { Succeeded: true } ? NoContent() : BadRequest(failure is null ? "Couldn't merge the accounts." : Describe(failure));
        }

        private static bool IsGuest(Player? player) =>
            player is { PasswordHash: null } && player.UserName!.StartsWith(Constants.GuestUserNamePrefix);

        /// <summary>How many games the guest session behind this refresh token has a seat in. Anonymous because logging in
        /// replaces the guest's tokens, so the login page asks first, with the guest's token as its proof</summary>
        [HttpPost]
        public async Task<ActionResult> GuestHistory(RefreshModel model)
        {
            var guest = await _refreshTokenManager.PeekAsync(model.RefreshToken);
            return Ok(new { games = IsGuest(guest) ? await _context.GameSeats.CountAsync(seat => seat.PlayerId == guest!.Id) : 0 });
        }

        /// <summary>Moves a guest session's seats to the named account signed in, then deletes the guest. Holding the
        /// guest's refresh token is the proof it's theirs. Where both held a seat in the same game the named account's is kept</summary>
        [Authorize(Policy = AuthPolicy.NamedAccountOnly)]
        [HttpPost]
        public async Task<ActionResult> ClaimGuest(RefreshModel model)
        {
            var target = await _userManager.GetUserAsync(User);
            var guest = await _refreshTokenManager.PeekAsync(model.RefreshToken);
            if (target is null)
            {
                return Unauthorized();
            }
            if (!IsGuest(guest))
            {
                return BadRequest("That guest session has expired.");
            }

            var failure = (IdentityResult?)null;
            await _context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var transaction = await _context.Database.BeginTransactionAsync();
                var (from, to) = (guest!.Id, target.Id);
                await _context.GameSeats
                    .Where(seat => seat.PlayerId == from && !_context.GameSeats.Any(other => other.GridGameId == seat.GridGameId && other.PlayerId == to))
                    .ExecuteUpdateAsync(set => set.SetProperty(seat => seat.PlayerId, to));
                await _context.GameSeats.Where(seat => seat.PlayerId == from).ExecuteDeleteAsync();
                await _refreshTokenManager.RevokeAllAsync(from);
                failure = await _userManager.DeleteAsync(guest);
                if (failure.Succeeded)
                {
                    await transaction.CommitAsync();
                }
            });
            return failure is { Succeeded: true } ? NoContent() : BadRequest(failure is null ? "Couldn't claim the guest games." : Describe(failure));
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
