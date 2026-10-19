using Q = RevitThyme.Core.TimberPrismRational;

namespace RevitThyme.Core;

// An independent exact planar guard: collision sums require a complete, disjoint cell partition.
internal static class TimberPrismPartition
{
    private readonly record struct Point(Q X, Q Y);
    internal static void Validate(TimberPanel panel, TimberPrismBudget budget)
    {
        if (panel.RingsInches.IsDefaultOrEmpty || panel.RingsInches.Length > 4096 || panel.RingsInches.Any(r => r.IsDefaultOrEmpty) ||
            panel.RingsInches.SelectMany(r => r).Take(16385).Count() > 16384)
            Fail("Panel contours are missing or exceed the 16384-vertex partition envelope.");
        Point Convert(TimberPoint2 p) => new(Q.From(p.X), Q.From(p.Y));
        var rings = panel.RingsInches.Select(r => r.Select(Convert).ToArray()).ToArray();
        var triangles = panel.Triangles.Select(t => new[] { Convert(t.A), Convert(t.B), Convert(t.C) }).ToArray();
        Q expected = 0, observed = 0;
        for (int r = 0; r < rings.Length; r++)
        {
            var ring = rings[r];
            if (ring.Length < 3) Fail("Panel contour has fewer than three vertices.");
            var area = Area(ring);
            if (r == 0 ? area <= 0 : area >= 0) Fail("Panel outer/hole contour orientation or area is invalid.");
            if (Edges(ring).Any(e => e.A == e.B)) Fail("Panel contour has a zero-length edge.");
            expected += area;
            for (int i = 0; i < ring.Length; i++)
            for (int j = i + 1; j < ring.Length; j++)
            {
                budget.Check();
                if (j == i + 1 || i == 0 && j == ring.Length - 1) continue;
                if (Intersects(ring[i], ring[(i + 1) % ring.Length], ring[j], ring[(j + 1) % ring.Length]))
                    Fail("Panel contour is not simple.");
            }
            if (r == 0) continue;
            if (Location(ring[0], rings[0]) != 1) Fail("Panel hole is outside its outer contour.");
            for (int s = 0; s < r; s++)
            {
                foreach (var (a, b) in Edges(ring))
                foreach (var (c, d) in Edges(rings[s]))
                { budget.Check(); if (Intersects(a, b, c, d)) Fail("Panel contour boundaries intersect or touch."); }
                if (s > 0 && (Location(ring[0], rings[s]) >= 0 || Location(rings[s][0], ring) >= 0))
                    Fail("Panel holes overlap or have nested ownership.");
            }
        }
        foreach (var triangle in triangles)
        {
            budget.Check();
            Q area = Cross(triangle[0], triangle[1], triangle[2]);
            if (area == 0) Fail("Panel triangulation has a degenerate cell.");
            if (area < 0) { (triangle[1], triangle[2]) = (triangle[2], triangle[1]); area = 0 - area; }
            observed += area;
            foreach (var p in triangle)
                if (!Filled(p, true)) Fail("A triangle vertex lies outside the perforated panel.");
            var center = new Point((triangle[0].X + triangle[1].X + triangle[2].X) / 3,
                (triangle[0].Y + triangle[1].Y + triangle[2].Y) / 3);
            if (!Filled(center, false)) Fail("A triangle occupies an aperture or absent panel region.");
            foreach (var ring in rings)
            {
                foreach (var p in ring)
                { budget.Check(); if (StrictlyInside(p, triangle)) Fail("A triangle covers an unresolved contour boundary."); }
                foreach (var (a, b) in Edges(ring))
                {
                    budget.Check();
                    if (StrictlyInside(new((a.X + b.X) / 2, (a.Y + b.Y) / 2), triangle))
                        Fail("A triangle covers a contour segment whose endpoints lie on cell edges.");
                }
                foreach (var (a, b) in Edges(triangle))
                foreach (var (c, d) in Edges(ring))
                { budget.Check(); if (Cross(a, b, c).Sign * Cross(a, b, d).Sign < 0 && Cross(c, d, a).Sign * Cross(c, d, b).Sign < 0) Fail("A triangle crosses its panel boundary."); }
            }
        }
        if (observed != expected || expected <= 0) Fail("Triangle cells do not conserve the exact binary contour area.");
        for (int i = 0; i < triangles.Length; i++)
        for (int j = 0; j < i; j++)
        {
            budget.Check();
            if (!Separated(triangles[i], triangles[j]) && !Separated(triangles[j], triangles[i]))
                Fail("Panel triangle cells overlap in their interiors.");
        }
        bool Filled(Point p, bool boundary)
        {
            budget.Check(rings.Sum(r => r.Length));
            int outer = Location(p, rings[0]);
            return (boundary ? outer >= 0 : outer > 0) && rings.Skip(1).All(r => boundary ? Location(p, r) <= 0 : Location(p, r) < 0);
        }
    }
    private static bool Separated(Point[] a, Point[] b) => Edges(a).Any(edge => b.All(p => Cross(edge.A, edge.B, p) <= 0));
    private static bool StrictlyInside(Point p, Point[] triangle) => Edges(triangle).All(e => Cross(e.A, e.B, p) > 0);
    private static IEnumerable<(Point A, Point B)> Edges(Point[] ring) => Enumerable.Range(0, ring.Length).Select(i => (ring[i], ring[(i + 1) % ring.Length]));
    private static Q Cross(Point a, Point b, Point c) => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
    private static Q Area(Point[] ring) => Edges(ring).Aggregate((Q)0, (sum, e) => sum + e.A.X * e.B.Y - e.A.Y * e.B.X);
    private static bool Intersects(Point a, Point b, Point c, Point d) =>
        Max(a.X, b.X) >= Min(c.X, d.X) && Max(c.X, d.X) >= Min(a.X, b.X) &&
        Max(a.Y, b.Y) >= Min(c.Y, d.Y) && Max(c.Y, d.Y) >= Min(a.Y, b.Y) &&
        Cross(a, b, c).Sign * Cross(a, b, d).Sign <= 0 && Cross(c, d, a).Sign * Cross(c, d, b).Sign <= 0;
    private static Q Min(Q a, Q b) => a < b ? a : b;
    private static Q Max(Q a, Q b) => a > b ? a : b;
    private static int Location(Point p, Point[] ring)
    {
        bool inside = false;
        foreach (var (a, b) in Edges(ring))
        {
            var side = Cross(a, b, p);
            if (side == 0 && p.X >= Min(a.X, b.X) && p.X <= Max(a.X, b.X) && p.Y >= Min(a.Y, b.Y) && p.Y <= Max(a.Y, b.Y)) return 0;
            if ((a.Y > p.Y) != (b.Y > p.Y) && (b.Y > a.Y ? side > 0 : side < 0)) inside = !inside;
        }
        return inside ? 1 : -1;
    }
    private static void Fail(string message) => throw new TimberPrismUnresolved("invalid_partition", message);
}
