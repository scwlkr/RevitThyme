using System.Runtime.ExceptionServices;

namespace RevitThyme.Addin;

// Conservative broad phase only: every returned segment still passes the
// unchanged finite-capsule/parallel-interval coverage predicates.
internal sealed class NativeTimberKitSegmentIndex
{
    private readonly record struct Box(double LowX, double LowY, double HighX, double HighY)
    {
        internal static Box For(NativeTimberKitSegment segment) => new(
            Math.Min(segment.A.X, segment.B.X), Math.Min(segment.A.Y, segment.B.Y),
            Math.Max(segment.A.X, segment.B.X), Math.Max(segment.A.Y, segment.B.Y));
        internal Box Include(Box other) => new(Math.Min(LowX, other.LowX), Math.Min(LowY, other.LowY),
            Math.Max(HighX, other.HighX), Math.Max(HighY, other.HighY));
        internal Box Expand(double budget) => new(Math.BitDecrement(LowX - budget), Math.BitDecrement(LowY - budget),
            Math.BitIncrement(HighX + budget), Math.BitIncrement(HighY + budget));
        internal bool Intersects(Box other) => LowX <= other.HighX && HighX >= other.LowX &&
            LowY <= other.HighY && HighY >= other.LowY;
    }
    private sealed record Entry(NativeTimberKitSegment Segment, Box Bounds, int OriginalIndex);
    private sealed record Node(Box Bounds, int Start, int Count, Node? Left = null, Node? Right = null);
    private readonly Entry[] entries;
    private readonly Node? root;
    private readonly CancellationToken cancellationToken;
    private readonly Action step;

    internal NativeTimberKitSegmentIndex(IReadOnlyList<NativeTimberKitSegment> segments, CancellationToken ct, Action budgetStep)
    {
        cancellationToken = ct; step = budgetStep; ct.ThrowIfCancellationRequested();
        entries = new Entry[segments.Count];
        for (int i = 0; i < entries.Length; i++)
        {
            Step(); entries[i] = new(segments[i], Box.For(segments[i]), i);
        }
        root = entries.Length == 0 ? null : Build(0, entries.Length);
    }

    internal IEnumerable<NativeTimberKitSegment> Candidates(NativeTimberKitSegment segment, double budget)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (root is null) yield break;
        var bounds = Box.For(segment).Expand(budget);
        var pending = new Stack<Node>(); pending.Push(root);
        while (pending.Count > 0)
        {
            Step(); var node = pending.Pop();
            if (!node.Bounds.Intersects(bounds)) continue;
            if (node.Left is not null)
            {
                pending.Push(node.Right!); pending.Push(node.Left); continue;
            }
            for (int i = node.Start; i < node.Start + node.Count; i++)
            {
                Step();
                if (entries[i].Bounds.Intersects(bounds)) yield return entries[i].Segment;
            }
        }
    }

    private Node Build(int start, int count)
    {
        Step(); var bounds = entries[start].Bounds;
        for (int i = start + 1; i < start + count; i++) { Step(); bounds = bounds.Include(entries[i].Bounds); }
        if (count <= 8) return new(bounds, start, count);
        bool byX = bounds.HighX - bounds.LowX >= bounds.HighY - bounds.LowY;
        try
        {
            Array.Sort(entries, start, count, Comparer<Entry>.Create((a, b) =>
            {
                Step();
                double left = byX ? a.Bounds.LowX / 2 + a.Bounds.HighX / 2 : a.Bounds.LowY / 2 + a.Bounds.HighY / 2;
                double right = byX ? b.Bounds.LowX / 2 + b.Bounds.HighX / 2 : b.Bounds.LowY / 2 + b.Bounds.HighY / 2;
                int comparison = left.CompareTo(right);
                return comparison != 0 ? comparison : a.OriginalIndex.CompareTo(b.OriginalIndex);
            }));
        }
        catch (InvalidOperationException error) when (error.InnerException is not null)
        {
            // Array.Sort wraps comparer exceptions; preserve cancellation and the
            // caller's explicit exhausted-budget result across that boundary.
            ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw;
        }
        int half = count / 2;
        return new(bounds, start, count, Build(start, half), Build(start + half, count - half));
    }
    private void Step() { cancellationToken.ThrowIfCancellationRequested(); step(); }
}
