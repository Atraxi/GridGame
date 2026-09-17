using GridGameAPI.UtilityInfrastructure;

namespace GridGameAPI.Model
{
    /// <summary>Estimates how the board would divide up if play continued naturally, without waiting for the strict
    /// end-game condition (mutual unreachability) to be met - two players can stay "in contact" through a long open
    /// corridor long after it's obvious who would win any race to claim most of the board</summary>
    public static class ScorePrediction
    {
        /// <summary>Sentinel distance meaning "not yet reached by anyone's expansion" - shares the value of
        /// TileState.Unclaimed's owner-less cells, which is also what an unclaimed, unreached cell defaults to below</summary>
        private const int Unreached = -1;

        /// <summary>Owner value meaning two or more players would reach this cell in the same number of moves, so
        /// nothing about it is settled - also used for the (rare) cell no active player can reach at all</summary>
        private const int Contested = 0;

        public record Result(int[] PredictedScores, int ContestedCells);

        /// <summary>One multi-source BFS, seeded from every active player's tiles at once: each unclaimed cell is
        /// claimed by whichever player's territory reaches it in the fewest moves, or left contested on a tie.
        /// O(cells) total (not per-player), so it's cheap enough to run after every move</summary>
        public static Result Compute(int[,] board, int playerCount)
        {
            var rows = board.GetLength(0);
            var columns = board.GetLength(1);
            var distance = new int[rows, columns];
            var owner = new int[rows, columns];
            for (var x = 0; x < rows; x++)
            {
                for (var y = 0; y < columns; y++)
                {
                    distance[x, y] = Unreached;
                }
            }

            var queue = new Queue<(int X, int Y)>();
            for (var x = 0; x < rows; x++)
            {
                for (var y = 0; y < columns; y++)
                {
                    if (board[x, y] >= 1 && board[x, y] <= playerCount)
                    {
                        distance[x, y] = 0;
                        owner[x, y] = board[x, y];
                        queue.Enqueue((x, y));
                    }
                }
            }

            while (queue.Count > 0)
            {
                var (x, y) = queue.Dequeue();
                var currentOwner = owner[x, y];
                var nextDistance = distance[x, y] + 1;

                foreach (var direction in ReachabilityAnalysis.Directions)
                {
                    foreach (var hop in new[] { 1, 2 })
                    {
                        var nx = x + direction.Dx * hop;
                        var ny = y + direction.Dy * hop;
                        if (nx < 0 || nx >= rows || ny < 0 || ny >= columns)
                        {
                            continue;
                        }

                        //Already-owned cells (this player's own territory elsewhere, or an opponent's) were seeded
                        //directly above - nothing to claim there, and (for an opponent's) it isn't a through-route
                        if (board[nx, ny] != TileState.Unclaimed)
                        {
                            continue;
                        }

                        if (distance[nx, ny] == Unreached)
                        {
                            distance[nx, ny] = nextDistance;
                            owner[nx, ny] = currentOwner;
                            queue.Enqueue((nx, ny));
                        }
                        else if (distance[nx, ny] == nextDistance && owner[nx, ny] != currentOwner)
                        {
                            //Someone else's expansion reached this same cell in the same number of moves - a tie
                            //propagates onward too, tainting anything only reachable through this cell as contested
                            owner[nx, ny] = Contested;
                        }
                        //else already settled by a strictly closer expansion (or already marked contested) - leave it
                    }
                }
            }

            var scores = new int[playerCount];
            foreach (var value in board)
            {
                if (value >= 1 && value <= playerCount)
                {
                    scores[value - 1]++;
                }
            }

            var contested = 0;
            for (var x = 0; x < rows; x++)
            {
                for (var y = 0; y < columns; y++)
                {
                    if (board[x, y] != TileState.Unclaimed)
                    {
                        continue;
                    }
                    var claimant = owner[x, y];
                    if (claimant >= 1 && claimant <= playerCount)
                    {
                        scores[claimant - 1]++;
                    }
                    else
                    {
                        contested++;
                    }
                }
            }

            return new Result(scores, contested);
        }
    }
}
