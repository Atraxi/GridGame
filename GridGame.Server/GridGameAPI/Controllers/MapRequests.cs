namespace GridGameAPI.Controllers
{
    /// <param name="IsDraft">Saves without the playability checks, keeping the map private to its designer</param>
    public record SaveMapRequest(string Name, int PlayerCount, int ActionsPerTurn, int[,] StartingBoard, bool IsDraft);
}
