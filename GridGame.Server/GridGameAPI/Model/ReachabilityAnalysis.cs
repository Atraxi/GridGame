namespace GridGameAPI.Model
{
    /// <summary>Shared BFS over the spawn(distance 1)/jump(distance 2) move graph used by MapValidation and
    /// GameEndAnalysis - an enemy tile is a reachable dead end (attacking it kills it rather than capturing it), not
    /// a through-route, so passability is asymmetric between players and every traversal here is seeded from one
    /// specific player's own tiles</summary>
    public static class ReachabilityAnalysis
    {
        public static readonly (int Dx, int Dy)[] Directions =
        [
            (-1, -1), (-1, 0), (-1, 1),
            (0, -1),           (0, 1),
            (1, -1),  (1, 0),  (1, 1),
        ];

        public static List<int> ActivePlayers(int[,] board, int playerCount)
        {
            var seen = new bool[playerCount + 1];
            foreach (var value in board)
            {
                if (value >= 1 && value <= playerCount)
                {
                    seen[value] = true;
                }
            }
            return Enumerable.Range(1, playerCount).Where(player => seen[player]).ToList();
        }

        /// <summary>Fast short-circuiting check: does `player` make contact with any tile currently owned by one of
        /// `opponents`? Returns the instant contact is found rather than computing player's full reachable set -
        /// meant for the hot per-move path, where the common case (game still contested) exits almost immediately</summary>
        public static bool CanReachAnyOf(int[,] board, int player, IReadOnlySet<int> opponents)
        {
            var rows = board.GetLength(0);
            var columns = board.GetLength(1);
            var visited = new bool[rows, columns];
            var queue = new Queue<(int X, int Y)>();

            for (var x = 0; x < rows; x++)
            {
                for (var y = 0; y < columns; y++)
                {
                    if (board[x, y] == player)
                    {
                        visited[x, y] = true;
                        queue.Enqueue((x, y));
                    }
                }
            }

            while (queue.Count > 0)
            {
                var (x, y) = queue.Dequeue();
                foreach (var direction in Directions)
                {
                    foreach (var distance in new[] { 1, 2 })
                    {
                        var nx = x + direction.Dx * distance;
                        var ny = y + direction.Dy * distance;
                        if (nx < 0 || nx >= rows || ny < 0 || ny >= columns || visited[nx, ny])
                        {
                            continue;
                        }

                        var target = board[nx, ny];
                        if (target == UtilityInfrastructure.TileState.Dead)
                        {
                            continue;
                        }
                        if (target != UtilityInfrastructure.TileState.Unclaimed && target != player)
                        {
                            if (opponents.Contains(target))
                            {
                                return true;
                            }
                            continue;
                        }

                        visited[nx, ny] = true;
                        queue.Enqueue((nx, ny));
                    }
                }
            }
            return false;
        }

        /// <summary>Full traversal of everywhere `player` could ever expand to: their own tiles plus every unclaimed
        /// cell reachable through open ground. Also reports which other players border that region. Expensive (a
        /// full flood fill) - meant to run once (map validation at save time, or final scoring once a game ends),
        /// not on every move</summary>
        public static (bool[,] Expandable, HashSet<int> TouchedOwners) TraverseReachable(int[,] board, int player)
        {
            var rows = board.GetLength(0);
            var columns = board.GetLength(1);
            var expandable = new bool[rows, columns];
            var touched = new HashSet<int>();
            var queue = new Queue<(int X, int Y)>();

            for (var x = 0; x < rows; x++)
            {
                for (var y = 0; y < columns; y++)
                {
                    if (board[x, y] == player)
                    {
                        expandable[x, y] = true;
                        queue.Enqueue((x, y));
                    }
                }
            }

            while (queue.Count > 0)
            {
                var (x, y) = queue.Dequeue();
                foreach (var direction in Directions)
                {
                    foreach (var distance in new[] { 1, 2 })
                    {
                        var nx = x + direction.Dx * distance;
                        var ny = y + direction.Dy * distance;
                        if (nx < 0 || nx >= rows || ny < 0 || ny >= columns || expandable[nx, ny])
                        {
                            continue;
                        }

                        var target = board[nx, ny];
                        if (target == UtilityInfrastructure.TileState.Dead)
                        {
                            continue;
                        }
                        if (target != UtilityInfrastructure.TileState.Unclaimed && target != player)
                        {
                            touched.Add(target);
                            continue;
                        }

                        expandable[nx, ny] = true;
                        queue.Enqueue((nx, ny));
                    }
                }
            }

            return (expandable, touched);
        }
    }
}
