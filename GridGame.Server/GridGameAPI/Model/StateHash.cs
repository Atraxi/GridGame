namespace GridGameAPI.Model
{
    /// <summary>Cheap fingerprint of everything a client renders about a live game, attached to every state push so a
    /// client can tell it has missed or misapplied one (and resync) instead of silently drifting. Must stay
    /// byte-for-byte identical to computeStateHash in the client's model/stateHash.ts: FNV-1a (32-bit) over a
    /// fixed sequence of int32s, each fed in as 4 little-endian bytes. Not a security measure - collisions only
    /// mean a desync goes unnoticed until the next push, and TurnNumber/ActionsRemainingInTurn change on every move,
    /// so consecutive states never share a hash in practice</summary>
    public static class StateHash
    {
        private const uint OffsetBasis = 2166136261;
        private const uint Prime = 16777619;

        public static uint Compute(GridGame game)
        {
            var board = game.GameBoard;
            var rows = board.GetLength(0);
            var columns = board.GetLength(1);

            var hash = OffsetBasis;
            hash = Mix(hash, game.TurnNumber);
            hash = Mix(hash, game.CurrentPlayerNumber);
            hash = Mix(hash, game.ActionsRemainingInTurn);
            hash = Mix(hash, game.IsGameOver ? 1 : 0);
            hash = Mix(hash, rows);
            hash = Mix(hash, columns);
            for (var x = 0; x < rows; x++)
            {
                for (var y = 0; y < columns; y++)
                {
                    hash = Mix(hash, board[x, y]);
                }
            }
            return hash;
        }

        private static uint Mix(uint hash, int value)
        {
            unchecked
            {
                var bits = (uint)value;
                for (var shift = 0; shift < 32; shift += 8)
                {
                    hash ^= (bits >> shift) & 0xFF;
                    hash *= Prime;
                }
                return hash;
            }
        }
    }
}
