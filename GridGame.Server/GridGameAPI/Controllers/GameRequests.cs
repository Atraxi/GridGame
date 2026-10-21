namespace GridGameAPI.Controllers
{
    /// <param name="InvitedPlayerIds">Accounts from the host's past-opponents list. Any at all makes the game invite-only</param>
    public record CreateGameFromMapRequest(int MapId, string Name, string[]? InvitedPlayerIds = null);
}
