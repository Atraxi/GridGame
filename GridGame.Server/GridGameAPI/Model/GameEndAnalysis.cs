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

        /// <summary>Score per player (index 0 = player 1) if the game ended with the board as it stands: tiles they
        /// already own, plus every unclaimed tile only they can reach (BoardAnalysis.Projected). At a natural game
        /// over this is exact - nobody can reach anybody, so there are no contested cells and no threats left - and
        /// it's also what a resignation settles on, leaving contested cells unscored</summary>
        public static int[] ComputeScores(int[,] board, int playerCount) =>
            BoardAnalysis.Compute(board, playerCount).Players.Select(player => player.Projected).ToArray();
    }
}
