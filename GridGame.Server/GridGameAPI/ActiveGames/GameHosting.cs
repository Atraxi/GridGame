using GridGameAPI.Database;
using GridGameAPI.Model;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace GridGameAPI.ActiveGames
{
    /// <summary>Starting a game from a saved map - shared by hosting a new game (GamesController.CreateFromMap) and a
    /// rematch (GridGameHub.Rematch)</summary>
    public static partial class GameHosting
    {
        public const string MapNotFound = "No map with that id exists.";

        /// <returns>The map, or (with a null map) why it can't be hosted: MapNotFound, or that it's still a draft</returns>
        public static async Task<(Map? Map, string? Error)> FindHostableMapAsync(GameContext context, int mapId, string hostId)
        {
            var map = await context.Maps.SingleOrDefaultAsync(map => map.Id == mapId);
            //A draft hasn't passed the playability checks (see MapsController.Validate) - hosting one could start a game
            //some player can never move in. Someone else's draft is reported as missing, same as MapsController.Get
            if (map is null || (map.IsDraft && map.DesignedByPlayerId != hostId))
            {
                return (null, MapNotFound);
            }
            if (map.IsDraft)
            {
                return (null, "Publish this map before hosting a game from it.");
            }
            return (map, null);
        }

        public static GridGame NewGame(Map map, string name, bool isInviteOnly) => new()
        {
            CreationDate = DateTime.Now,
            Name = name,
            TurnNumber = 0,
            PlayerCount = map.PlayerCount,
            ActionsPerTurn = map.ActionsPerTurn,
            CurrentPlayerNumber = 1,
            ActionsRemainingInTurn = map.ActionsPerTurn,
            Players = [],
            //Cloned so that playing the game can never mutate the map's own saved template
            GameBoard = (int[,])map.StartingBoard.Clone(),
            MapId = map.Id,
            IsInviteOnly = isInviteOnly,
        };

        /// <summary>"Duel" -> "Duel (rematch)" -> "Duel (rematch 2)" ..., rather than piling up suffixes</summary>
        public static string RematchName(string name)
        {
            var match = RematchSuffix().Match(name);
            if (!match.Success)
            {
                return $"{name} (rematch)";
            }
            var number = match.Groups[2].Success ? int.Parse(match.Groups[2].Value) + 1 : 2;
            return $"{match.Groups[1].Value} (rematch {number})";
        }

        [GeneratedRegex(@"^(.*) \(rematch(?: (\d+))?\)$")]
        private static partial Regex RematchSuffix();
    }
}
