using Microsoft.AspNetCore.Identity;
using System.Text.Json.Serialization;

namespace GridGameAPI.Model
{
    public class Player : IdentityUser
    {
        //GridGame.Players is serialized straight out to clients, so the security material IdentityUser carries has to be kept out of it
        [JsonIgnore] public override string? PasswordHash { get; set; }
        [JsonIgnore] public override string? SecurityStamp { get; set; }
        [JsonIgnore] public override string? ConcurrencyStamp { get; set; }

        /// <summary>Grants the Admin role claim on login. No API sets this - it's only ever flipped by editing the database directly</summary>
        public bool IsAdmin { get; set; }

        public int GamesPlayed { get; set; }
        public int GamesWon { get; set; }
        public int GamesLost { get; set; }
    }
}
