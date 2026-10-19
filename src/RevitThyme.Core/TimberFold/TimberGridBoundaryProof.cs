using Clipper2Lib;

namespace RevitThyme.Core;

// Exact directed-edge cancellation proves the two filled regions partition the noded
// source, rather than merely having equal total area or a rounded Boolean XOR of zero.
internal static class TimberGridBoundaryProof
{
    private readonly record struct Edge(Point64 A, Point64 B);
    internal static void Require(Paths64 original, Paths64 output, Paths64 changed, bool union, TimberPrismBudget budget)
    {
        if (!Matches(original, output, changed, union, budget)) throw new TimberPrismUnresolved("grid_refinement_partition",
            "Boolean regions do not form an exact directed-edge partition of the recorded refined boundary.");
    }

    internal static bool Matches(Paths64 original, Paths64 output, Paths64 changed, bool union, TimberPrismBudget budget)
        => Counts([(original, 1), (output, -1), (changed, union ? 1 : -1)], budget).Values.All(v => v == 0);

    internal static Point64[] Nodes(Paths64 output, Paths64 changed, bool union, TimberPrismBudget budget) =>
        Counts([(output, 1), (changed, union ? -1 : 1)], budget).Where(e => e.Value != 0)
            .SelectMany(e => new[] { e.Key.A, e.Key.B }).Distinct().ToArray();

    private static Dictionary<Edge, int> Counts((Paths64 Paths, int Sign)[] regions, TimberPrismBudget budget)
    {
        var points = regions.SelectMany(r => r.Paths).SelectMany(p => p).Distinct().ToArray();
        var counts = new Dictionary<Edge, int>();
        foreach (var (paths, sign) in regions)
        {
            foreach (var path in paths)
            for (int i = 0; i < path.Count; i++)
            {
                var a = path[i]; var b = path[(i + 1) % path.Count];
                var nodes = new List<Point64>();
                foreach (var point in points) { budget.Check(); if (OnSegment(point, a, b)) nodes.Add(point); }
                var ordered = nodes.OrderBy(p => Projection(p, a, b)).ToArray();
                for (int n = 1; n < ordered.Length; n++)
                {
                    var first = ordered[n - 1]; var second = ordered[n];
                    bool forward = first.X < second.X || first.X == second.X && first.Y < second.Y;
                    var edge = forward ? new Edge(first, second) : new Edge(second, first);
                    counts.TryGetValue(edge, out int prior); counts[edge] = prior + (forward ? sign : -sign);
                }
            }
        }
        return counts;
    }

    internal static bool OnSegment(Point64 p, Point64 a, Point64 b) =>
        ((Int128)b.X - a.X) * ((Int128)p.Y - a.Y) == ((Int128)b.Y - a.Y) * ((Int128)p.X - a.X) &&
        p.X >= Math.Min(a.X, b.X) && p.X <= Math.Max(a.X, b.X) && p.Y >= Math.Min(a.Y, b.Y) && p.Y <= Math.Max(a.Y, b.Y);
    private static Int128 Projection(Point64 p, Point64 a, Point64 b) =>
        ((Int128)p.X - a.X) * ((Int128)b.X - a.X) + ((Int128)p.Y - a.Y) * ((Int128)b.Y - a.Y);
}
