using GridGameAPI.Configuration;
using GridGameAPI.Model;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Text;

namespace GridGameAPI.UtilityInfrastructure
{
    public class JwtManager(IOptions<JwtSettings> _jwtSettings)
    {
        public string GetToken(Player player, DateTime expiry)
        {
            var isGuest = player.UserName!.StartsWith(Constants.GuestUserNamePrefix);

            List<Claim> claims =
            [
                new Claim(ClaimTypes.NameIdentifier, player.Id),
                new Claim(ClaimTypes.Name, player.UserName!),
                new Claim(Constants.AccountTypeClaim, isGuest ? Constants.GuestAccountType : Constants.NamedAccountType),
            ];
            if (player.IsAdmin)
            {
                claims.Add(new Claim(ClaimTypes.Role, AppRole.Admin));
            }

            return GetToken(claims, expiry);
        }

        public string GetToken(IList<Claim> claims, DateTime expiry)
        {
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.Value.Key));

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Expires = expiry,
                SigningCredentials = new SigningCredentials(
                    key,
                    SecurityAlgorithms.HmacSha256)
            };
            //JsonWebTokenHandler writes claim types verbatim, and JwtBearer is configured not to remap them on the way back in,
            //so the ClaimTypes names above survive the round trip for SignalR's UserIdentifier and UserManager to find
            return new JsonWebTokenHandler().CreateToken(tokenDescriptor);
        }
    }
}
