using GridGameAPI.UtilityInfrastructure;
using System.Text.Json.Serialization;

namespace GridGameAPI.Model
{
    /// <summary>What a single cell means for the outcome, from the whole board's point of view</summary>
    public enum CellInsight
    {
        /// <summary>Dead ground - out of play for everyone</summary>
        None = 0,
        /// <summary>Owned, and no opponent can ever get within spawn/jump distance of it</summary>
        OwnedUntouchable = 1,
        /// <summary>Owned, but some opponent could still get within spawn/jump distance and kill it</summary>
        OwnedThreatened = 2,
        /// <summary>Unclaimed and reachable by two or more players - a race</summary>
        Contested = 3,
        /// <summary>Unclaimed and reachable by nobody - will never score for anyone</summary>
        Unreachable = 4,
        /// <summary>Unclaimed, reachable only by its claimant, via a route no opponent can interfere with</summary>
        UncontestedSecure = 5,
        /// <summary>Unclaimed and reachable only by its claimant, but every route there passes through a tile an
        /// opponent could kill or a cell an opponent could claim first - so the claimant may yet be cut off from it</summary>
        UncontestedAtRisk = 6,
    }

    [JsonConverter(typeof(JsonStringEnumConverter<VictoryStatus>))]
    public enum VictoryStatus
    {
        None,
        /// <summary>Ahead on owned + uncontested, ignoring contested cells and threats entirely</summary>
        Leading,
        /// <summary>Owned + uncontested beats every opponent's best case (all their owned, all their uncontested, AND
        /// every contested cell they can reach) - threatened tiles are ignored on both sides, on the basis that a
        /// kill-for-kill exchange roughly cancels out. Informal: an aggressive opponent can in principle still turn it</summary>
        Decided,
        /// <summary>Untouchable + securely uncontested alone beats every opponent's best case - nothing any opponent
        /// does can change the result (modulo the known jump-origin simplification, see TODO.md)</summary>
        Locked,
    }

    /// <param name="ContestedReachable">Contested cells this player is one of the players able to reach</param>
    /// <param name="Floor">Untouchable + securely uncontested: what this player keeps no matter what anyone else does</param>
    /// <param name="Projected">Owned + all uncontested: the "threats cancel out" estimate, and exactly the final
    /// score if the game ended right now (see GameEndAnalysis.ComputeScores)</param>
    /// <param name="Ceiling">Owned + every unclaimed cell this player can reach: the most they could ever finish with</param>
    public record PlayerAnalysis(
        int PlayerNumber,
        bool IsActive,
        int Owned,
        int OwnedUntouchable,
        int OwnedThreatened,
        int UncontestedSecure,
        int UncontestedAtRisk,
        int ContestedReachable,
        int Floor,
        int Projected,
        int Ceiling,
        VictoryStatus Status);

    /// <summary>
    /// Classifies every cell of a live board by who can still affect it, and summarises that per player.
    ///
    /// Built on the same reach model as ReachabilityAnalysis.TraverseReachable: a player's reach is every cell they
    /// could ever occupy - their own tiles, plus unclaimed cells chained together by spawns (1 step) and jumps (exactly
    /// 2 steps in a straight/diagonal line, over anything). Enemy tiles are attack targets, never stepping stones. Two
    /// properties make the classification stable enough to be worth showing:
    ///  - Reach only ever shrinks: cells only go unclaimed -> owned -> dead, and each of those transitions removes
    ///    options for every other player. So "no opponent can reach/attack this" stays true for the rest of the game.
    ///  - An unclaimed cell outside an opponent's reach is also outside their attack range (anything within spawn/jump
    ///    distance of their reach that is still unclaimed would itself be in their reach) - so once an uncontested cell
    ///    is claimed, it is untouchable.
    ///
    /// Known simplification (deliberately not modelled yet, see TODO.md): a jump kills its origin, so a tile whose
    /// only exits are jumps in different directions can only take one of them. The flood fill counts every exit as
    /// reachable, which can over-count reach, uncontested cells and Floor in those (rare) corridors.
    ///
    /// Cost: one flood fill per player plus a couple of linear passes - cheap enough to run after every move
    /// </summary>
    public static class BoardAnalysis
    {
        private static readonly int[] Hops = [1, 2];

        /// <param name="Claimant">For uncontested cells, the only player able to reach them; for owned cells, the owner; else 0</param>
        /// <param name="SettledCells">Cells whose final owner (or lack of one) can no longer change: dead,
        /// unreachable, untouchable, and securely uncontested</param>
        /// <param name="InformallyDecidedFor">The player with Decided or Locked status, if any - at most one player can
        /// hold either, since each requires beating every opponent's ceiling, and a ceiling is never below its own projection</param>
        public record Result(
            CellInsight[,] Insight,
            int[,] Claimant,
            PlayerAnalysis[] Players,
            int TotalCells,
            int DeadCells,
            int UnclaimedCells,
            int ContestedCells,
            int UnreachableCells,
            int SettledCells,
            int? InformallyDecidedFor);

        public static Result Compute(int[,] board, int playerCount)
        {
            var rows = board.GetLength(0);
            var columns = board.GetLength(1);

            var reach = new bool[playerCount + 1][,];
            var attack = new bool[playerCount + 1][,];
            var active = new bool[playerCount + 1];
            //How many players can reach each unclaimed cell, and (when exactly one) which
            var reachCount = new int[rows, columns];
            var soleReacher = new int[rows, columns];

            for (var player = 1; player <= playerCount; player++)
            {
                var (expandable, _) = ReachabilityAnalysis.TraverseReachable(board, player);
                reach[player] = expandable;
                attack[player] = AttackRange(board, expandable);

                for (var x = 0; x < rows; x++)
                {
                    for (var y = 0; y < columns; y++)
                    {
                        if (board[x, y] == player)
                        {
                            active[player] = true;
                        }
                        else if (board[x, y] == TileState.Unclaimed && expandable[x, y])
                        {
                            reachCount[x, y]++;
                            soleReacher[x, y] = player;
                        }
                    }
                }
            }

            var insight = new CellInsight[rows, columns];
            var claimant = new int[rows, columns];
            var owned = new int[playerCount + 1];
            var untouchable = new int[playerCount + 1];
            var contestedReachable = new int[playerCount + 1];
            int dead = 0, unclaimed = 0, contested = 0, unreachable = 0;

            for (var x = 0; x < rows; x++)
            {
                for (var y = 0; y < columns; y++)
                {
                    var value = board[x, y];
                    if (value == TileState.Dead)
                    {
                        dead++;
                    }
                    else if (value == TileState.Unclaimed)
                    {
                        unclaimed++;
                        switch (reachCount[x, y])
                        {
                            case 0:
                                insight[x, y] = CellInsight.Unreachable;
                                unreachable++;
                                break;
                            case 1:
                                //Secure vs at-risk is decided by the per-player flood below; at-risk is the default
                                insight[x, y] = CellInsight.UncontestedAtRisk;
                                claimant[x, y] = soleReacher[x, y];
                                break;
                            default:
                                insight[x, y] = CellInsight.Contested;
                                contested++;
                                for (var player = 1; player <= playerCount; player++)
                                {
                                    if (reach[player][x, y])
                                    {
                                        contestedReachable[player]++;
                                    }
                                }
                                break;
                        }
                    }
                    else if (value >= 1 && value <= playerCount)
                    {
                        owned[value]++;
                        claimant[x, y] = value;
                        var threatened = false;
                        for (var opponent = 1; opponent <= playerCount && !threatened; opponent++)
                        {
                            threatened = opponent != value && attack[opponent][x, y];
                        }
                        insight[x, y] = threatened ? CellInsight.OwnedThreatened : CellInsight.OwnedUntouchable;
                        if (!threatened)
                        {
                            untouchable[value]++;
                        }
                    }
                }
            }

            for (var player = 1; player <= playerCount; player++)
            {
                MarkSecure(board, insight, claimant, player);
            }

            var secure = new int[playerCount + 1];
            var atRisk = new int[playerCount + 1];
            for (var x = 0; x < rows; x++)
            {
                for (var y = 0; y < columns; y++)
                {
                    if (insight[x, y] == CellInsight.UncontestedSecure)
                    {
                        secure[claimant[x, y]]++;
                    }
                    else if (insight[x, y] == CellInsight.UncontestedAtRisk)
                    {
                        atRisk[claimant[x, y]]++;
                    }
                }
            }

            var floor = new int[playerCount + 1];
            var projected = new int[playerCount + 1];
            var ceiling = new int[playerCount + 1];
            for (var player = 1; player <= playerCount; player++)
            {
                floor[player] = untouchable[player] + secure[player];
                projected[player] = owned[player] + secure[player] + atRisk[player];
                ceiling[player] = projected[player] + contestedReachable[player];
            }

            var players = new PlayerAnalysis[playerCount];
            int? decidedFor = null;
            for (var player = 1; player <= playerCount; player++)
            {
                var opponents = Enumerable.Range(1, playerCount).Where(other => other != player && active[other]).ToList();
                var status = VictoryStatus.None;
                if (active[player] && opponents.Count > 0)
                {
                    if (opponents.All(other => floor[player] > ceiling[other]))
                    {
                        status = VictoryStatus.Locked;
                    }
                    else if (opponents.All(other => projected[player] > ceiling[other]))
                    {
                        status = VictoryStatus.Decided;
                    }
                    else if (opponents.All(other => projected[player] > projected[other]))
                    {
                        status = VictoryStatus.Leading;
                    }
                }
                if (status is VictoryStatus.Decided or VictoryStatus.Locked)
                {
                    decidedFor = player;
                }

                players[player - 1] = new PlayerAnalysis(
                    PlayerNumber: player,
                    IsActive: active[player],
                    Owned: owned[player],
                    OwnedUntouchable: untouchable[player],
                    OwnedThreatened: owned[player] - untouchable[player],
                    UncontestedSecure: secure[player],
                    UncontestedAtRisk: atRisk[player],
                    ContestedReachable: contestedReachable[player],
                    Floor: floor[player],
                    Projected: projected[player],
                    Ceiling: ceiling[player],
                    Status: status);
            }

            var settled = dead + unreachable + untouchable.Sum() + secure.Sum();
            return new Result(insight, claimant, players, rows * columns, dead, unclaimed, contested, unreachable, settled, decidedFor);
        }

        /// <summary>Every cell a player standing anywhere in `reach` could hit with one spawn or one jump</summary>
        private static bool[,] AttackRange(int[,] board, bool[,] reach)
        {
            var rows = board.GetLength(0);
            var columns = board.GetLength(1);
            var range = new bool[rows, columns];
            for (var x = 0; x < rows; x++)
            {
                for (var y = 0; y < columns; y++)
                {
                    if (!reach[x, y])
                    {
                        continue;
                    }
                    foreach (var (dx, dy) in ReachabilityAnalysis.Directions)
                    {
                        foreach (var hop in Hops)
                        {
                            var nx = x + dx * hop;
                            var ny = y + dy * hop;
                            if (nx >= 0 && nx < rows && ny >= 0 && ny < columns)
                            {
                                range[nx, ny] = true;
                            }
                        }
                    }
                }
            }
            return range;
        }

        /// <summary>Upgrades `player`'s uncontested cells to secure where they can be reached from one of the player's
        /// untouchable tiles by stepping only through other cells uncontested for that same player. Such a route can't
        /// be interfered with: no opponent can reach (so can't claim) any cell on it, and no opponent can attack any of
        /// it, either now or once claimed. Routes through a threatened tile or a contested cell are left at-risk, even
        /// though a 1-wide cut could sometimes still be jumped - erring conservative keeps Floor a genuine floor</summary>
        private static void MarkSecure(int[,] board, CellInsight[,] insight, int[,] claimant, int player)
        {
            var rows = board.GetLength(0);
            var columns = board.GetLength(1);
            var visited = new bool[rows, columns];
            var queue = new Queue<(int X, int Y)>();

            for (var x = 0; x < rows; x++)
            {
                for (var y = 0; y < columns; y++)
                {
                    if (board[x, y] == player && insight[x, y] == CellInsight.OwnedUntouchable)
                    {
                        visited[x, y] = true;
                        queue.Enqueue((x, y));
                    }
                }
            }

            while (queue.Count > 0)
            {
                var (x, y) = queue.Dequeue();
                foreach (var (dx, dy) in ReachabilityAnalysis.Directions)
                {
                    foreach (var hop in Hops)
                    {
                        var nx = x + dx * hop;
                        var ny = y + dy * hop;
                        if (nx < 0 || nx >= rows || ny < 0 || ny >= columns || visited[nx, ny])
                        {
                            continue;
                        }
                        if (insight[nx, ny] != CellInsight.UncontestedAtRisk || claimant[nx, ny] != player)
                        {
                            continue;
                        }
                        visited[nx, ny] = true;
                        insight[nx, ny] = CellInsight.UncontestedSecure;
                        queue.Enqueue((nx, ny));
                    }
                }
            }
        }

        /// <summary>Wire shape shared by the hub's live push and the final-results endpoint. Jagged arrays rather than
        /// int[,], since SignalR's JSON protocol doesn't have the MVC-side TwoDimensionalIntArrayJsonConverter</summary>
        public static object ToWire(Result result) => new
        {
            insight = ToJagged(result.Insight, insight => (int)insight),
            claimant = ToJagged(result.Claimant, value => value),
            players = result.Players,
            totalCells = result.TotalCells,
            deadCells = result.DeadCells,
            unclaimedCells = result.UnclaimedCells,
            contestedCells = result.ContestedCells,
            unreachableCells = result.UnreachableCells,
            settledCells = result.SettledCells,
            informallyDecidedFor = result.InformallyDecidedFor,
        };

        public static int[][] ToJagged<T>(T[,] grid, Func<T, int> select)
        {
            var rows = grid.GetLength(0);
            var columns = grid.GetLength(1);
            var jagged = new int[rows][];
            for (var x = 0; x < rows; x++)
            {
                jagged[x] = new int[columns];
                for (var y = 0; y < columns; y++)
                {
                    jagged[x][y] = select(grid[x, y]);
                }
            }
            return jagged;
        }
    }
}
