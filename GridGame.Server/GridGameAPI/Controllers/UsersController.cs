using GridGameAPI.Configuration;
using GridGameAPI.Model;
using GridGameAPI.UtilityInfrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace GridGameAPI.Controllers
{
    [ApiController]
    [Route("[controller]/[action]")]
    public class UsersController(
        UserManager<Player> _userManager,
        JwtManager _jwtManager,
        RefreshTokenManager _refreshTokenManager,
        GuestCleanup _guestCleanup,
        IOptions<JwtSettings> _jwtSettings) : ControllerBase
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
            await _userManager.SetUserNameAsync(player, model.UserName);
            await _userManager.SetEmailAsync(player, model.Email);
            var result = await _userManager.AddPasswordAsync(player, model.Password);
            if (!result.Succeeded)
            {
                return BadRequest(string.Join(" ", result.Errors.Select(error => error.Description)));
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
    }
}
