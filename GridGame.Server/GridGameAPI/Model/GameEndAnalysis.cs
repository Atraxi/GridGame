namespace GridGameAPI.Model
{
    /// <summary>Detects when a game has no more contested moves left, and computes the final tile-count score once
    /// it does. The game continues while any active player can still reach another (there's still a fight to have);
    /// once no pair can reach each other, every remaining unclaimed cell is only ever reachable by at most one
    /// player - if two players could reach the same cell, they could reach each other through it, contradicting
    /// game-over - so a plain flood fill from each player's territory partitions the board cleanly. This is exactly
    /// what playing out the remaining uncontested moves to completion would produce, just computed directly instead
    /// of one tedious move at a time</summary>
    public static class GameEndAnalysis
    {
        public static bool IsGameOver(int[,] board, int playerCount)
        {
            var activePlayers = ReachabilityAnalysis.ActivePlayers(board, playerCount);
            if (activePlayers.Count <= 1)
            {
                return true;
            }

            var activeSet = activePlayers.ToHashSet();
            foreach (var player in activePlayers)
            {
                var opponents = new HashSet<int>(activeSet);
                opponents.Remove(player);
                if (ReachabilityAnalysis.CanReachAnyOf(board, player, opponents))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>Final score per player (index 0 = player 1): tiles they already own, plus every unclaimed tile
        /// only they could ever have reached. Expensive (a full flood fill per player) - call once, when
        /// IsGameOver first becomes true</summary>
        public static int[] ComputeScores(int[,] board, int playerCount)
        {
            var rows = board.GetLength(0);
            var columns = board.GetLength(1);
            var scores = new int[playerCount];

            foreach (var value in board)
            {
                if (value >= 1 && value <= playerCount)
                {
                    scores[value - 1]++;
                }
            }

            for (var player = 1; player <= playerCount; player++)
            {
                var (expandable, _) = ReachabilityAnalysis.TraverseReachable(board, player);
                for (var x = 0; x < rows; x++)
                {
                    for (var y = 0; y < columns; y++)
                    {
                        if (expandable[x, y] && board[x, y] == UtilityInfrastructure.TileState.Unclaimed)
                        {
                            scores[player - 1]++;
                        }
                    }
                }
            }

            return scores;
        }
    }
}
