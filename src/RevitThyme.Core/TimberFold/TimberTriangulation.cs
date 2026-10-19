using Clipper2Lib;

namespace RevitThyme.Core;

internal static class TimberTriangulation
{
    internal static TimberTriangle[] Create(Paths64 contours)
    {
        if (Clipper.Triangulate(contours, out var triangles) != TriangulateResult.success || triangles.Count == 0)
            throw new InvalidOperationException("Constrained triangulation failed; no collision decomposition is available.");
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
}
