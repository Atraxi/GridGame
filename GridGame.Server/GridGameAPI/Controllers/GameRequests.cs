using GridGameAPI.Model;

namespace GridGameAPI.Controllers
{
    /// <param name="InvitedPlayerIds">Accounts from the host's past-opponents list. Any at all makes the game invite-only</param>
    /// <param name="ClockBaseSeconds">Bank mode: each player's time bank. PerTurn mode: the limit on each turn</param>
    public record CreateGameFromMapRequest(
        int MapId, string Name, string[]? InvitedPlayerIds = null,
        ClockMode ClockMode = ClockMode.None, int ClockBaseSeconds = 0, int ClockIncrementSeconds = 0);
}
