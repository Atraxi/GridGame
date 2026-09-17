namespace GridGameAPI.Controllers
{
    public record CreateMapRequest(string Name, int PlayerCount, int ActionsPerTurn, int[,] StartingBoard);
}
