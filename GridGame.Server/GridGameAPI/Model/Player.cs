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

        /// <summary>Preferred tile colour as #rrggbb, shown to everyone in a game unless it clashes with another seat's</summary>
        public string? ColourPrimary { get; set; }

        /// <summary>Optional second colour: tiles blend from it (centre) out to ColourPrimary (edges), which tiles seamlessly</summary>
        public string? ColourSecondary { get; set; }

        /// <summary>Show everyone in default colours, ignoring chosen ones</summary>
        public bool UseDefaultColours { get; set; }

        /// <summary>JSON array of 8 { primary, secondary } - what this player sees each seat in by default, in place of
        /// the stock palette. Only they see it; null means the stock palette</summary>
        public string? DefaultPalette { get; set; }
    }
}
