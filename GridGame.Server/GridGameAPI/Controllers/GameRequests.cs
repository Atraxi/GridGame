using GridGameAPI.Model;

namespace GridGameAPI.Controllers
{
    /// <param name="InvitedPlayerIds">Accounts from the host's past-opponents list. Any at all makes the game invite-only</param>
    /// <param name="ClockBaseSeconds">Bank mode: each player's time bank. PerTurn mode: the limit on each turn</param>
    /// <param name="BotNames">Per seat (first = player 1): one of GamesController.GetBots, or null for a human. Leave out for no bots. All bots makes a game to watch</param>
    public record CreateGameFromMapRequest(
        int MapId, string Name, string[]? InvitedPlayerIds = null,
        ClockMode ClockMode = ClockMode.None, int ClockBaseSeconds = 0, int ClockIncrementSeconds = 0,
        string?[]? BotNames = null);
}
