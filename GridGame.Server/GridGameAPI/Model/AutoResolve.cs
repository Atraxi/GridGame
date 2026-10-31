using GridGameAPI.UtilityInfrastructure;

namespace GridGameAPI.Model
{
    /// <summary>One jump of an auto-resolved ending; `Player` is who makes it</summary>
    public record ForcedMove(int Player, int OriginX, int OriginY, int X, int Y);

    /// <summary>
    /// Spots an ending that has nothing left to decide. When every legal move for whoever is to play is a jump onto an
    /// enemy tile (no spawn, nothing unclaimed to take), each is a tile-for-tile trade: the jumper's origin dies along
    /// with the tile it lands on. If playing the trades out until the game ends never changes who can reach which
    /// unclaimed cell, the result can't be changed by choosing differently, so the game plays them out for the players.
    /// </summary>
    public static class AutoResolve
    {
        private const int MaxMoves = 200;

        /// <returns>The moves to play out from the given position until the game ends, or null if the position still
        /// calls for a decision (or playing it out would change reachability)</returns>
        /// <param name="insight">BoardAnalysis.Insight for `board`. Run after every move, so the common answer (a real
        /// choice remains) is reached before anything expensive</param>
        public static List<ForcedMove>? Plan(int[,] board, int playerCount, int current, int actionsRemaining, int actionsPerTurn, CellInsight[,] insight)
        {
            var sim = (int[,])board.Clone();
            int[]? baseline = null;
            var plan = new List<ForcedMove>();

            while (!GameEndAnalysis.IsGameOver(sim, playerCount))
            {
                if (plan.Count >= MaxMoves)
                {
                    return null;
                }
                var options = ForcedJumps(sim, current, insight);
                if (options is null || options.Count == 0)
                {
                    return null;
                }
                baseline ??= Signature(sim, playerCount);

                //The first trade that leaves reachability as it was; if none does, a choice matters
                ForcedMove? chosen = null;
                foreach (var option in options)
                {
                    var trial = (int[,])sim.Clone();
                    Apply(trial, option);
                    if (Signature(trial, playerCount).SequenceEqual(baseline))
                    {
                        chosen = option;
                        break;
                    }
                }
                if (chosen is null)
                {
                    return null;
                }

                Apply(sim, chosen);
                plan.Add(chosen);
                actionsRemaining--;
                if (GameEndAnalysis.IsGameOver(sim, playerCount))
                {
                    break;
                }
                insight = BoardAnalysis.Compute(sim, playerCount).Insight;
                if (actionsRemaining <= 0 || !GridGameMoves.HasAnyLegalMove(sim, current, insight))
                {
                    if (GridGameMoves.NextMover(sim, playerCount, current, insight) is not { } next)
                    {
                        break;
                    }
                    current = next;
                    actionsRemaining = actionsPerTurn;
                }
            }
            return plan;
        }

        private static void Apply(int[,] board, ForcedMove move)
        {
            board[move.OriginX, move.OriginY] = TileState.Dead;
            board[move.X, move.Y] = TileState.Dead;
        }

        /// <returns>Every jump `player` could make, all of them onto enemy tiles that can't be spawned onto - or null as
        /// soon as any legal move is something else (a spawn, or a claim of unclaimed ground)</returns>
        private static List<ForcedMove>? ForcedJumps(int[,] board, int player, CellInsight[,] insight)
        {
            var moves = new List<ForcedMove>();
            for (var x = 0; x < board.GetLength(0); x++)
            {
                for (var y = 0; y < board.GetLength(1); y++)
                {
                    if (board[x, y] != player)
                    {
                        continue;
                    }
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        for (var dy = -1; dy <= 1; dy++)
                        {
                            if (dx == 0 && dy == 0)
                            {
                                continue;
                            }
                            foreach (var distance in new[] { 1, 2 })
                            {
                                var nx = x + dx * distance;
                                var ny = y + dy * distance;
                                if (!GridGameMoves.InBounds(board, nx, ny) || board[nx, ny] == player ||
                                    board[nx, ny] == TileState.Dead || GridGameMoves.IsBlocked(board, insight, nx, ny))
                                {
                                    continue;
                                }
                                if (distance == 1 || board[nx, ny] == TileState.Unclaimed || GridGameMoves.CanSpawnTo(board, player, nx, ny))
                                {
                                    return null;
                                }
                                moves.Add(new ForcedMove(player, x, y, nx, ny));
                            }
                        }
                    }
                }
            }
            return moves;
        }

        /// <summary>Who can reach each unclaimed cell, collapsed to unreachable / one claimant / contested</summary>
        private static int[] Signature(int[,] board, int playerCount)
        {
            var analysis = BoardAnalysis.Compute(board, playerCount);
            var signature = new List<int>();
            for (var x = 0; x < board.GetLength(0); x++)
            {
                for (var y = 0; y < board.GetLength(1); y++)
                {
                    if (board[x, y] != TileState.Unclaimed)
                    {
                        continue;
                    }
                    signature.Add(analysis.Insight[x, y] switch
                    {
                        CellInsight.Unreachable => 0,
                        CellInsight.Contested => -1,
                        _ => analysis.Claimant[x, y],
                    });
                }
            }
            return [.. signature];
        }
    }
}
