namespace GridGameAPI.Model
{
    /// <summary>Pure board math for the two move types: spawn (1 tile, 8 directions) and jump (2 tiles, 8 directions, origin dies)</summary>
    public static class GridGameMoves
    {
        private static readonly (int Dx, int Dy)[] Directions =
        [
            (-1, -1), (-1, 0), (-1, 1),
            (0, -1),           (0, 1),
            (1, -1),  (1, 0),  (1, 1),
        ];

        public static bool InBounds(int[,] board, int x, int y) =>
            x >= 0 && x < board.GetLength(0) && y >= 0 && y < board.GetLength(1);

        public static bool CanSpawnTo(int[,] board, int player, int x, int y) =>
            Directions.Any(d => InBounds(board, x + d.Dx, y + d.Dy) && board[x + d.Dx, y + d.Dy] == player);

        public static List<(int X, int Y)> JumpOriginsFor(int[,] board, int player, int x, int y) =>
            Directions
                .Select(d => (X: x + d.Dx * 2, Y: y + d.Dy * 2))
                .Where(origin => InBounds(board, origin.X, origin.Y) && board[origin.X, origin.Y] == player)
                .ToList();

        public static bool IsValidJump(int[,] board, int player, int originX, int originY, int x, int y) =>
            InBounds(board, originX, originY) &&
            board[originX, originY] == player &&
            Directions.Any(d => originX + d.Dx * 2 == x && originY + d.Dy * 2 == y);

        /// <summary>An unclaimed cell only one player can ever reach: moving there is pointless, since nobody can contest
        /// it and no route to a contested cell runs through it (reach is symmetric), so such moves are refused. It's
        /// scored for its claimant when the game ends anyway (see BoardAnalysis)</summary>
        public static bool IsBlocked(int[,] board, CellInsight[,]? insight, int x, int y) =>
            insight is not null && board[x, y] == UtilityInfrastructure.TileState.Unclaimed &&
            insight[x, y] is CellInsight.UncontestedSecure or CellInsight.UncontestedAtRisk;

        /// <summary>The next player after `current` with a legal move, or null if nobody has one</summary>
        public static int? NextMover(int[,] board, int playerCount, int current, CellInsight[,]? insight)
        {
            for (var attempt = 0; attempt < playerCount; attempt++)
            {
                current = current % playerCount + 1;
                if (HasAnyLegalMove(board, current, insight))
                {
                    return current;
                }
            }
            return null;
        }

        /// <summary>True if any tile `player` owns has at least one legal spawn or jump target. Used to skip a
        /// player's turn instead of stalling on them once they have nowhere left to go (eliminated, or walled in).
        /// Targets blocked by `insight` (see IsBlocked) don't count</summary>
        public static bool HasAnyLegalMove(int[,] board, int player, CellInsight[,]? insight = null)
        {
            var rows = board.GetLength(0);
            var columns = board.GetLength(1);
            for (var x = 0; x < rows; x++)
            {
                for (var y = 0; y < columns; y++)
                {
                    if (board[x, y] != player)
                    {
                        continue;
                    }
                    foreach (var direction in Directions)
                    {
                        foreach (var distance in new[] { 1, 2 })
                        {
                            var nx = x + direction.Dx * distance;
                            var ny = y + direction.Dy * distance;
                            if (InBounds(board, nx, ny) && board[nx, ny] != player && board[nx, ny] != UtilityInfrastructure.TileState.Dead &&
                                !IsBlocked(board, insight, nx, ny))
                            {
                                return true;
                            }
                        }
                    }
                }
            }
            return false;
        }
    }
}
