using GridGameAPI.UtilityInfrastructure;

namespace GridGameAPI.Bots
{
    /// <summary>
    /// A computer opponent. Implement this in a class with a public parameterless constructor anywhere in this project
    /// and it shows up in the "play against" list under its class name - there is nothing to register. A fresh instance
    /// is made for every game, so fields can hold what it has learnt about that game.
    ///
    /// The board is board[x, y]. A cell holds the number of the player who owns it, TileState.Unclaimed (0) or
    /// TileState.Dead (-1). On your action you either spawn onto a cell next to one of your tiles, or jump to a cell
    /// exactly two steps away in a straight or diagonal line (over anything); the tile you jumped from dies. Landing on
    /// an enemy tile kills it. See RandomBot for the smallest working example.
    ///
    /// To look ahead: BoardAnalysis.Compute(board, playerCount).Insight, then GridGameMoves.LegalMoves for any player
    /// on a board copy, then Move.TileChanges to play a move out on it.
    /// </summary>
    public interface IBot
    {
        /// <summary>Called for each action you get - a turn has several. Return one of turn.LegalMoves.
        /// You have about 20 seconds; stop early when turn.Cancel is cancelled. If you throw or return anything else a
        /// random legal move is played, and if you run out of time you are not asked again: random moves for the rest of the game</summary>
        Move ChooseMove(BotTurn turn);
    }

    /// <param name="Board">Your own copy, safe to change</param>
    /// <param name="Me">Your player number</param>
    /// <param name="ActionsPerTurn">What every player gets at the start of their turn</param>
    /// <param name="ActionsLeft">Including this one</param>
    /// <param name="Cancel">Cancelled when your time is up - check it in any long loop so the thread is freed</param>
    /// <param name="LegalMoves">Never empty. A cell you could spawn onto appears once, as a spawn; any other cell you
    /// can reach appears once per tile you could jump from</param>
    public record BotTurn(int[,] Board, int Me, int PlayerCount, int ActionsPerTurn, int ActionsLeft, IReadOnlyList<Move> LegalMoves, CancellationToken Cancel = default);

    /// <summary>Claims (X, Y). For a jump, (FromX, FromY) is the tile it starts from; both are null for a spawn</summary>
    public record Move(int X, int Y, int? FromX = null, int? FromY = null)
    {
        /// <summary>The cells this move rewrites as (x, y, new value) - handy for looking ahead on a copy of the board</summary>
        public (int X, int Y, int NewValue)[] TileChanges(int[,] board, int player)
        {
            var landing = (X, Y, board[X, Y] == TileState.Unclaimed ? player : TileState.Dead);
            return FromX is { } fromX && FromY is { } fromY ? [(fromX, fromY, TileState.Dead), landing] : [landing];
        }
    }
}
