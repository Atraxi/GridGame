namespace GridGameAPI.Bots
{
    /// <summary>Plays any legal move. Copy this to start a bot of your own</summary>
    public class RandomBot : IBot
    {
        public Move ChooseMove(BotTurn turn) => turn.LegalMoves[Random.Shared.Next(turn.LegalMoves.Count)];
    }
}
