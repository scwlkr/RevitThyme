using Clipper2Lib;

namespace RevitThyme.Core;

internal static class TimberTriangulation
{
    internal static TimberTriangle[] Create(Paths64 contours)
    {
        if (Clipper.Triangulate(contours, out var triangles) != TriangulateResult.success || triangles.Count == 0)
            throw new InvalidOperationException("Constrained triangulation failed; no collision decomposition is available.");
        triangles = RestoreBoundaryVertices(contours, triangles);
        Int128 area = 0;
        foreach (var triangle in triangles)
        {
            if (triangle.Count != 3 || TimberContours.TwiceArea(triangle) == 0)
                throw new InvalidOperationException("Triangulation produced a degenerate cell.");
            if (TimberContours.TwiceArea(triangle) < 0) triangle.Reverse();
            area += TimberContours.TwiceArea(triangle);
        }
        // Exact integer area conservation and coverage expose overlapping cells or filled holes.
        if (area != contours.Aggregate((Int128)0, (a, p) => a + TimberContours.TwiceArea(p)) ||
            Clipper.Xor(contours, Clipper.Union(triangles, FillRule.NonZero), FillRule.NonZero).Count != 0)
            throw new InvalidOperationException("Triangulation does not conserve the canonical perforated region.");
        return triangles.Select(t =>
        {
            var p = t.Select(v => new TimberPoint2(v.X / TimberContours.Grid, v.Y / TimberContours.Grid)).ToArray();
            int start = Enumerable.Range(0, 3).OrderBy(i => p[i].X).ThenBy(i => p[i].Y).First();
            return new TimberTriangle(p[start], p[(start + 1) % 3], p[(start + 2) % 3]);
        }).OrderBy(t => t.A.X).ThenBy(t => t.A.Y).ThenBy(t => t.B.X).ThenBy(t => t.B.Y)
            .ThenBy(t => t.C.X).ThenBy(t => t.C.Y).ToArray();
    }

    private static Paths64 RestoreBoundaryVertices(Paths64 contours, Paths64 triangles)
    {
        var boundary = contours.SelectMany(p => p).Distinct().ToArray();
        var result = new Paths64();
        foreach (var triangle in triangles)
        {
            var cells = new Paths64 { new Path64(triangle) };
            foreach (var point in boundary.Where(p => Enumerable.Range(0, 3).Any(i =>
                OnEdge(p, triangle[i], triangle[(i + 1) % 3]))))
            {
                bool inserted = false;
                for (int i = 0; i < cells.Count && !inserted; i++)
                for (int edge = 0; edge < 3; edge++)
                {
                    var a = cells[i][edge]; var b = cells[i][(edge + 1) % 3]; var c = cells[i][(edge + 2) % 3];
                    if (!OnEdge(point, a, b)) continue;
                    // Clipper may omit an integer-collinear contour vertex. After division
                    // by the decimal grid it can bend in exact binary geometry. Retain it
                    // in both contour and cells instead of changing the delivered contour.
                    cells[i] = new Path64 { a, point, c };
                    cells.Add(new Path64 { point, b, c }); inserted = true; break;
                }
            }
            result.AddRange(cells);
        }
        return result;
    }

    private static bool OnEdge(Point64 p, Point64 a, Point64 b) => p != a && p != b &&
        ((Int128)b.X - a.X) * ((Int128)p.Y - a.Y) == ((Int128)b.Y - a.Y) * ((Int128)p.X - a.X) &&
        p.X >= Math.Min(a.X, b.X) && p.X <= Math.Max(a.X, b.X) && p.Y >= Math.Min(a.Y, b.Y) && p.Y <= Math.Max(a.Y, b.Y);
}
