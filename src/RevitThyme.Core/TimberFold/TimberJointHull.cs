using Clipper2Lib;
using System.Numerics;
using Q = RevitThyme.Core.TimberPrismRational;
using Point = RevitThyme.Core.TimberPrismPoint;

namespace RevitThyme.Core;

internal static class TimberJointHull
{
    internal static Point[] Exact(IEnumerable<Point> source, TimberPrismBudget budget)
    {
        var points = source.Select(p => new Point(p.X, p.Y, 0)).Distinct().OrderBy(p => p.X).ThenBy(p => p.Y).ToArray();
        List<Point> Chain(IEnumerable<Point> input)
        {
            var chain = new List<Point>();
            foreach (var p in input)
            {
                budget.Check();
                while (chain.Count > 1 && Cross(chain[^2], chain[^1], p) <= 0) chain.RemoveAt(chain.Count - 1);
                chain.Add(p);
            }
            return chain;
        }
        var low = Chain(points); var high = Chain(points.Reverse());
        return low.Take(Math.Max(0, low.Count - 1)).Concat(high.Take(Math.Max(0, high.Count - 1))).ToArray();
    }
    internal static Path64 Grid(Point[] exact, TimberPrismBudget budget)
    {
        var boxes = exact.SelectMany(p =>
        {
            var x = p.X * Q.From(TimberContours.Grid); var y = p.Y * Q.From(TimberContours.Grid);
            long left = Floor(x), right = Ceiling(x), bottom = Floor(y), top = Ceiling(y);
            return new Point64[] { new(left, bottom), new(right, bottom), new(right, top), new(left, top) };
        });
        var hull = Integer(boxes, budget);
        // A convex hull of every rational vertex's enclosing box encloses the entire exact
        // convex shadow. Recheck every supporting half-plane with exact rational arithmetic.
        foreach (var p in exact)
        for (int i = 0; i < hull.Count; i++)
        {
            budget.Check(); var a = hull[i]; var b = hull[(i + 1) % hull.Count];
            var cross = Q.From(b.X - a.X) * (p.Y * Q.From(TimberContours.Grid) - Q.From(a.Y)) -
                Q.From(b.Y - a.Y) * (p.X * Q.From(TimberContours.Grid) - Q.From(a.X));
            if (cross < 0) throw new TimberPrismUnresolved("joint_enclosure", "Grid joint shadow does not contain its exact convex material shadow.");
        }
        return hull;
    }
    internal static Path64 Expand(Path64 hull, long u, long v, TimberPrismBudget budget) => Integer(hull.SelectMany(p =>
        new Point64[] { new(checked(p.X - u), checked(p.Y - v)), new(checked(p.X + u), checked(p.Y - v)),
            new(checked(p.X + u), checked(p.Y + v)), new(checked(p.X - u), checked(p.Y + v)) }), budget);
    private static Path64 Integer(IEnumerable<Point64> source, TimberPrismBudget budget)
    {
        var points = source.Distinct().OrderBy(p => p.X).ThenBy(p => p.Y).ToArray();
        List<Point64> Chain(IEnumerable<Point64> input)
        {
            var chain = new List<Point64>();
            foreach (var p in input)
            {
                budget.Check();
                if (Math.Abs(p.X) > 100000000000000 || Math.Abs(p.Y) > 100000000000000)
                    throw new TimberPrismUnresolved("joint_grid_envelope", "Joint shadow exceeds the qualified local integer-grid envelope.");
                while (chain.Count > 1 && Cross(chain[^2], chain[^1], p) <= 0) chain.RemoveAt(chain.Count - 1);
                chain.Add(p);
            }
            return chain;
        }
        var low = Chain(points); var high = Chain(points.Reverse());
        return new(low.Take(Math.Max(0, low.Count - 1)).Concat(high.Take(Math.Max(0, high.Count - 1))));
    }
    internal static Q Area(Point[] ring)
    {
        Q area = 0;
        for (int i = 0; i < ring.Length; i++) { var a = ring[i]; var b = ring[(i + 1) % ring.Length]; area += a.X * b.Y - a.Y * b.X; }
        return area / 2;
    }
    internal static long Ceiling(Q value) => checked((long)(-FloorInteger(new Q(-value.Numerator, value.Denominator))));
    private static long Floor(Q value) => checked((long)FloorInteger(value));
    private static BigInteger FloorInteger(Q value)
    {
        var quotient = BigInteger.DivRem(value.Numerator, value.Denominator, out var remainder);
        return remainder.Sign < 0 ? quotient - 1 : quotient;
    }
    private static Q Cross(Point a, Point b, Point c) => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
    private static Int128 Cross(Point64 a, Point64 b, Point64 c) => ((Int128)b.X - a.X) * (c.Y - a.Y) - ((Int128)b.Y - a.Y) * (c.X - a.X);
}
