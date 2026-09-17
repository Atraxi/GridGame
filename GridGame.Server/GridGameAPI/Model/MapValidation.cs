namespace GridGameAPI.Model
{
    /// <summary>Sanity checks applied when a map is saved, using the same spawn/jump reachability rules as live
    /// play - see GridGameMoves - so a map can't be published with a player who has no way into the game</summary>
    public static class MapValidation
    {
        public static bool AllPlayersHaveAtLeastOneTile(int[,] board, int playerCount)
        {
            var seen = new bool[playerCount + 1];
            foreach (var value in board)
            {
                if (value >= 1 && value <= playerCount)
                {
                    seen[value] = true;
                }
            }
            return Enumerable.Range(1, playerCount).All(player => seen[player]);
        }

        /// <summary>True if every player can eventually reach every other one - i.e. no player is walled off by dead
        /// ground with no way to ever make contact. Checked separately from each player's own perspective: landing on
        /// an enemy tile kills it rather than capturing it, so (unlike a captured empty tile) it's a dead end, not a
        /// through-route - a path that happens to run through one of player A's own tiles doesn't prove player B
        /// could use that same route, since it's an attack target rather than open ground to player B</summary>
        public static bool AllPlayersCanReachEachOther(int[,] board, int playerCount) =>
            Enumerable.Range(1, playerCount).All(player =>
            {
                var (_, touched) = ReachabilityAnalysis.TraverseReachable(board, player);
                return Enumerable.Range(1, playerCount).Where(other => other != player).All(touched.Contains);
            });
    }
}
