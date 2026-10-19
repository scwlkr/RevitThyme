using Clipper2Lib;

namespace RevitThyme.Core;

internal static class TimberContourTopology
{
    internal static void Simple(Path64 ring)
    {
        for (int i = 0; i < ring.Count; i++)
        {
            var a = ring[(i + ring.Count - 1) % ring.Count]; var b = ring[i]; var c = ring[(i + 1) % ring.Count];
            if (a == b || b == c || (Cross(a, b, c) == 0 &&
                ((Int128)a.X - b.X) * (c.X - b.X) + ((Int128)a.Y - b.Y) * (c.Y - b.Y) > 0))
                throw new InvalidOperationException("A contour has a collapsed or overlapping adjacent edge.");
        }
        var edges = Enumerable.Range(0, ring.Count).Select(i => new Edge(ring[i], ring[(i + 1) % ring.Count], i))
            .OrderBy(e => e.MinX).ThenBy(e => e.MinY);
        var active = new List<Edge>();
        foreach (var edge in edges)
        {
            active.RemoveAll(e => e.MaxX < edge.MinX);
            foreach (var other in active)
            {
                if ((edge.Index + 1) % ring.Count == other.Index || (other.Index + 1) % ring.Count == edge.Index) continue;
                if (edge.MaxY >= other.MinY && other.MaxY >= edge.MinY && Intersects(edge.A, edge.B, other.A, other.B))
                    throw new InvalidOperationException("A source contour self-intersects or touches a nonadjacent edge.");
            }
            active.Add(edge);
        }
    }
    internal static void OuterAndHoles(Paths64 paths)
    {
        for (int i = 1; i < paths.Count; i++)
        {
            if (Clipper.PointInPolygon(paths[i][0], paths[0]) != PointInPolygonResult.IsInside || Touch(paths[i], paths[0]))
                throw new InvalidOperationException("A hole is outside or touches its source outer contour.");
            for (int j = 1; j < i; j++)
                if (Touch(paths[i], paths[j]) || Clipper.PointInPolygon(paths[i][0], paths[j]) != PointInPolygonResult.IsOutside ||
                    Clipper.PointInPolygon(paths[j][0], paths[i]) != PointInPolygonResult.IsOutside)
                    throw new InvalidOperationException("Source holes overlap, touch or have ambiguous nested ownership.");
        }
    }
    internal static bool Touch(Path64 a, Path64 b, CancellationToken cancellationToken = default)
    {
        for (int i = 0; i < a.Count; i++)
        for (int j = 0; j < b.Count; j++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Intersects(a[i], a[(i + 1) % a.Count], b[j], b[(j + 1) % b.Count])) return true;
        }
        return false;
    }
    internal static bool SharesEdge(Path64 a, Path64 b, CancellationToken cancellationToken = default)
    {
        for (int i = 0; i < a.Count; i++)
        for (int j = 0; j < b.Count; j++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var p = a[i]; var q = a[(i + 1) % a.Count]; var r = b[j]; var s = b[(j + 1) % b.Count];
            if (Cross(p, q, r) != 0 || Cross(p, q, s) != 0) continue;
            if (p.X != q.X ? Math.Min(Math.Max(p.X, q.X), Math.Max(r.X, s.X)) > Math.Max(Math.Min(p.X, q.X), Math.Min(r.X, s.X)) :
                Math.Min(Math.Max(p.Y, q.Y), Math.Max(r.Y, s.Y)) > Math.Max(Math.Min(p.Y, q.Y), Math.Min(r.Y, s.Y))) return true;
        }
        return false;
    }
    private static bool Intersects(Point64 a, Point64 b, Point64 c, Point64 d)
    {
        if (Math.Max(a.X, b.X) < Math.Min(c.X, d.X) || Math.Max(c.X, d.X) < Math.Min(a.X, b.X) ||
            Math.Max(a.Y, b.Y) < Math.Min(c.Y, d.Y) || Math.Max(c.Y, d.Y) < Math.Min(a.Y, b.Y)) return false;
        var abC = Cross(a, b, c); var abD = Cross(a, b, d); var cdA = Cross(c, d, a); var cdB = Cross(c, d, b);
        return ((abC <= 0 && abD >= 0) || (abC >= 0 && abD <= 0)) &&
            ((cdA <= 0 && cdB >= 0) || (cdA >= 0 && cdB <= 0));
    }
    private static Int128 Cross(Point64 a, Point64 b, Point64 c) =>
        ((Int128)b.X - a.X) * (c.Y - a.Y) - ((Int128)b.Y - a.Y) * (c.X - a.X);
    private sealed record Edge(Point64 A, Point64 B, int Index)
    {
        internal long MinX => Math.Min(A.X, B.X);
        internal long MaxX => Math.Max(A.X, B.X);
        internal long MinY => Math.Min(A.Y, B.Y);
        internal long MaxY => Math.Max(A.Y, B.Y);
    }
}
