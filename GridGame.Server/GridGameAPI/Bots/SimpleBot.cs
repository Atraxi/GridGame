namespace GridGameAPI.Bots
{
    /// <summary>
    /// Plays the way the home page's demo games do (a port of `simulate` in the client's tutorial.ts): push towards the
    /// nearest enemy along a solid front, kill any enemy tile in reach, and only jump to get past a wall. Each legal move
    /// gets a score and the pick is weighted towards the best, so it is not a strong player, just a plausible one.
    /// A good example of scoring moves without looking ahead.
    /// </summary>
    public class SimpleBot : IBot
    {
        private static readonly (int Dx, int Dy)[] Neighbours =
            [(-1, -1), (-1, 0), (-1, 1), (0, -1), (0, 1), (1, -1), (1, 0), (1, 1)];

        public Move ChooseMove(BotTurn turn)
        {
            var board = turn.Board;
            var me = turn.Me;
            var enemies = Cells(board, tile => tile > 0 && tile != me);
            var walkable = ReachableBySpawning(board, me);

            //Moves, not steps: a spawn covers a diagonal as readily as a straight line
            int DistanceToEnemy(int x, int y) =>
                enemies.Count == 0 ? 0 : enemies.Min(enemy => Math.Max(Math.Abs(x - enemy.X), Math.Abs(y - enemy.Y)));

            //When a cell can be jumped to from several tiles, give up the one furthest from the fighting
            var options = turn.LegalMoves
                .GroupBy(move => (move.X, move.Y))
                .Select(group => group.MaxBy(move => move.FromX is { } fromX ? DistanceToEnemy(fromX, move.FromY!.Value) : 0)!)
                .ToList();

            double Score(Move move)
            {
                var attack = board[move.X, move.Y] > 0;
                var toEnemy = DistanceToEnemy(move.X, move.Y);
                if (move.FromX is null)
                {
                    //Pushing forward along a solid front rather than in a thin line
                    var backing = Neighbours.Count(n => IsTile(board, move.X + n.Dx, move.Y + n.Dy, me));
                    return attack ? 6 : 0.3 * backing - toEnemy;
                }
                //A jump costs a tile, so it is worth it to get past a wall - onto ground spawning could never reach - but
                //not just to spread. Killing by jumping is worth less than by spawning, for the same reason
                var crossesWall = !attack && !walkable.Contains((move.X, move.Y));
                return attack ? 0.5 : crossesWall ? 2.5 - toEnemy : -4 - toEnemy;
            }

            var weights = options.Select(move => Math.Exp(Score(move) * 1.5)).ToList();
            var pick = Random.Shared.NextDouble() * weights.Sum();
            for (var i = 0; i < options.Count; i++)
            {
                pick -= weights[i];
                if (pick < 0)
                {
                    return options[i];
                }
            }
            return options[^1];
        }

        private static bool IsTile(int[,] board, int x, int y, int tile) =>
            x >= 0 && x < board.GetLength(0) && y >= 0 && y < board.GetLength(1) && board[x, y] == tile;

        private static List<(int X, int Y)> Cells(int[,] board, Func<int, bool> match) =>
            Enumerable.Range(0, board.GetLength(0))
                .SelectMany(x => Enumerable.Range(0, board.GetLength(1)).Select(y => (X: x, Y: y)))
                .Where(cell => match(board[cell.X, cell.Y]))
                .ToList();

        /// <summary>The unclaimed cells `player` could fill by spawning alone, one after another</summary>
        private static HashSet<(int X, int Y)> ReachableBySpawning(int[,] board, int player)
        {
            var reached = new HashSet<(int X, int Y)>();
            var queue = new Queue<(int X, int Y)>(Cells(board, tile => tile == player));
            while (queue.TryDequeue(out var cell))
            {
                foreach (var (dx, dy) in Neighbours)
                {
                    var next = (X: cell.X + dx, Y: cell.Y + dy);
                    if (IsTile(board, next.X, next.Y, 0) && reached.Add(next))
                    {
                        queue.Enqueue(next);
                    }
                }
            }
            return reached;
        }
    }
}
