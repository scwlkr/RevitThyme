using Clipper2Lib;
using Q = RevitThyme.Core.TimberPrismRational;
using Point = RevitThyme.Core.TimberPrismPoint;

namespace RevitThyme.Core;

internal static class TimberRoofExposureSweep
{
    internal static Paths64 Sweep(Paths64 region, Point delta, TimberPrismBudget budget)
    {
        if (region.Count == 0) return [];
        if (Clipper.Triangulate(region, out var cells) != TriangulateResult.success)
            throw new TimberPrismUnresolved("wall_envelope_sweep", "Finite wall/roof sweep could not triangulate its perforated region.");
        var result = new Paths64();
        foreach (var cell in cells)
        {
            budget.Check(); var points = cell.Select(Exact).ToArray();
            result.Add(TimberJointHull.Grid(TimberJointHull.Exact(points.Concat(points.Select(p => p + delta)), budget), budget));
        }
        return Clipper.Union(result, FillRule.NonZero);
    }
    internal static Paths64 TopBands(Path64 outside, Point up, TimberPrismBudget budget)
    {
        var result = new Paths64();
        foreach (var (a, b) in TopEdges(outside, up)) result.Add(Quad(a, b, up, budget));
        return Clipper.Union(result, FillRule.NonZero);
    }
    internal static Paths64 Continued(Path64 outside, IEnumerable<Path64> upperOutlines, Point up, TimberPrismBudget budget)
    {
        var result = new Paths64();
        foreach (var (a, b) in TopEdges(outside, up))
        foreach (var upper in upperOutlines)
        for (int i = 0; i < upper.Count; i++)
        {
            budget.Check(); var c = upper[i]; var d = upper[(i + 1) % upper.Count];
            if (Outward(c, d, up) >= 0 || Cross(a, b, c) != 0 || Cross(a, b, d) != 0) continue;
            bool x = Math.Abs(b.X - a.X) >= Math.Abs(b.Y - a.Y);
            long Along(Point64 p) => x ? p.X : p.Y;
            var endpoints = new[] { a, b, c, d }.OrderBy(Along).ToArray();
            var low = endpoints[1]; var high = endpoints[2];
            if (Along(high) <= Along(low) || Along(low) < Math.Min(Along(a), Along(b)) || Along(high) > Math.Max(Along(a), Along(b)) ||
                Along(low) < Math.Min(Along(c), Along(d)) || Along(high) > Math.Max(Along(c), Along(d))) continue;
            result.Add(Quad(low, high, up, budget));
        }
        return Clipper.Union(result, FillRule.NonZero);
    }
    private static IEnumerable<(Point64 A, Point64 B)> TopEdges(Path64 outside, Point up) =>
        Enumerable.Range(0, outside.Count).Select(i => (A: outside[i], B: outside[(i + 1) % outside.Count])).Where(e => Outward(e.A, e.B, up) > 0);
    private static Q Outward(Point64 a, Point64 b, Point up) => Q.From(b.Y - a.Y) * up.X - Q.From(b.X - a.X) * up.Y;
    private static Path64 Quad(Point64 a, Point64 b, Point delta, TimberPrismBudget budget) =>
        TimberJointHull.Grid(TimberJointHull.Exact([Exact(a), Exact(b), Exact(a) + delta, Exact(b) + delta], budget), budget);
    private static Point Exact(Point64 p) => new(Q.From(p.X) / Q.From(TimberContours.Grid), Q.From(p.Y) / Q.From(TimberContours.Grid), 0);
    private static Int128 Cross(Point64 a, Point64 b, Point64 c) => ((Int128)b.X - a.X) * (c.Y - a.Y) - ((Int128)b.Y - a.Y) * (c.X - a.X);
}
