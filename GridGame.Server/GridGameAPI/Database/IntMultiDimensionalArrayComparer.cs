using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace GridGameAPI.Database
{
    /// <summary>Change tracking for int[,] columns. EF's default comparer falls back on IStructuralEquatable, which
    /// throws "Array was not a one-dimensional array" as soon as a tracked board is replaced with a different array -
    /// which is exactly what saving over an existing map does (MapsController.Update), so publishing a draft or editing
    /// a published map failed every time. Compared by shape and contents instead, and snapshotted as a copy so an
    /// in-place edit to the board is noticed too</summary>
    public class IntMultiDimensionalArrayComparer : ValueComparer<int[,]?>
    {
        public IntMultiDimensionalArrayComparer() : base(
            (a, b) => AreEqual(a, b),
            board => HashOf(board),
            board => board == null ? null : (int[,])board.Clone())
        { }

        private static bool AreEqual(int[,]? a, int[,]? b) =>
            ReferenceEquals(a, b) ||
            (a is not null && b is not null &&
             a.GetLength(0) == b.GetLength(0) &&
             a.GetLength(1) == b.GetLength(1) &&
             a.Cast<int>().SequenceEqual(b.Cast<int>()));

        private static int HashOf(int[,]? board)
        {
            if (board is null)
            {
                return 0;
            }
            var hash = new HashCode();
            hash.Add(board.GetLength(0));
            hash.Add(board.GetLength(1));
            foreach (var value in board)
            {
                hash.Add(value);
            }
            return hash.ToHashCode();
        }
    }
}
