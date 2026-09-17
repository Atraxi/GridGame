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

        /// <summary>True if any tile `player` owns has at least one legal spawn or jump target. Used to skip a
        /// player's turn instead of stalling on them once they have nowhere left to go (eliminated, or walled in)</summary>
        public static bool HasAnyLegalMove(int[,] board, int player)
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
                            if (InBounds(board, nx, ny) && board[nx, ny] != player && board[nx, ny] != UtilityInfrastructure.TileState.Dead)
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
