using Q = RevitThyme.Core.TimberPrismRational;
using Point = RevitThyme.Core.TimberPrismPoint;

namespace RevitThyme.Core;

internal static class TimberPrismClipping
{
    internal static Q Intersection(TimberPrismCell first, TimberPrismCell second, TimberPrismBudget budget)
    {
        var faces = first.Faces.Select(f => f.ToList()).ToList();
        foreach (var plane in second.Planes)
        {
            budget.Check();
            bool inside = false, outside = false;
            foreach (var p in faces.SelectMany(f => f))
            {
                var sign = plane.Distance(p).Sign;
                inside |= sign < 0; outside |= sign > 0;
            }
            if (!inside) return 0; // Empty or exact face/edge/vertex contact has no volume.
            if (!outside) continue;
            var clipped = new List<List<Point>>();
            foreach (var face in faces)
            {
                var result = new List<Point>();
                for (int i = 0; i < face.Count; i++)
                {
                    budget.Check();
                    var a = face[i]; var b = face[(i + 1) % face.Count];
                    var da = plane.Distance(a); var db = plane.Distance(b);
                    if (da <= 0) result.Add(a);
                    if (da.Sign * db.Sign < 0) result.Add(a + (b - a) * (da / (da - db)));
                }
                result = result.Distinct().ToList();
                if (result.Count >= 3) clipped.Add(result);
            }
            var cap = Hull(clipped.SelectMany(f => f).Where(p => plane.Distance(p) == 0), plane, budget);
            if (cap.Count < 3) throw new TimberPrismUnresolved("invalid_cap", "Exact prism clipping could not close a positive-volume cut.");
            clipped.Add(cap); faces = clipped;
        }
        Q volume = 0;
        foreach (var face in faces)
        for (int i = 1; i + 1 < face.Count; i++)
        {
            budget.Check();
            volume += face[0].Dot(face[i].Cross(face[i + 1])) / 6;
        }
        if (volume < 0) throw new TimberPrismUnresolved("invalid_orientation", "Clipped prism has inconsistent boundary orientation.");
        return volume;
    }

    private static List<Point> Hull(IEnumerable<Point> vertices, TimberPrismPlane plane, TimberPrismBudget budget)
    {
        // Dropping any nonzero normal component gives a nonsingular exact 2D projection.
        int drop = plane.Normal.X != 0 ? 0 : plane.Normal.Y != 0 ? 1 : 2;
        int u = (drop + 1) % 3, v = (drop + 2) % 3;
        var ordered = vertices.Distinct().OrderBy(p => p[u]).ThenBy(p => p[v]).ToArray();
        List<Point> Chain(IEnumerable<Point> source)
        {
            var chain = new List<Point>();
            foreach (var p in source)
            {
                budget.Check();
                while (chain.Count >= 2 && Turn(chain[^2], chain[^1], p) <= 0) chain.RemoveAt(chain.Count - 1);
                chain.Add(p);
            }
            return chain;
        }
        Q Turn(Point a, Point b, Point c) => (b[u] - a[u]) * (c[v] - a[v]) - (b[v] - a[v]) * (c[u] - a[u]);
        var lower = Chain(ordered); var upper = Chain(ordered.Reverse());
        var hull = lower.Take(Math.Max(0, lower.Count - 1)).Concat(upper.Take(Math.Max(0, upper.Count - 1))).ToList();
        if (hull.Count >= 3 && (hull[1] - hull[0]).Cross(hull[2] - hull[0]).Dot(plane.Normal) < 0) hull.Reverse();
        return hull;
    }
}
