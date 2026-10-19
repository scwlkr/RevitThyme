using Clipper2Lib;

namespace RevitThyme.Core;

internal static class TimberContours
{
    internal const double Grid = 100000000;
    // Keep coordinates under 1e14 grid units, well below Clipper's degraded intersection range.
    private const double MaximumInches = 1000000;
    internal static Paths64 Encode(TimberPoint2[][] rings, double x, double y, double factor, bool orderedHoles)
    {
        var paths = new Paths64();
        foreach (var ring in rings)
        {
            var path = new Path64();
            foreach (var p in ring)
            {
                double px = (p.X - x) * factor, py = (p.Y - y) * factor;
                if (!double.IsFinite(px) || !double.IsFinite(py) || Math.Abs(px) > MaximumInches || Math.Abs(py) > MaximumInches)
                    throw new InvalidOperationException("Local physical contours exceed the qualified integer-grid envelope.");
                path.Add(new Point64(Math.Round(px * Grid), Math.Round(py * Grid)));
            }
            if (path.Count > 1 && path[0] == path[^1]) path.RemoveAt(path.Count - 1);
            if (path.Count < 3 || TwiceArea(path) == 0)
                throw new InvalidOperationException("A contour collapsed at physical grid resolution.");
            TimberContourTopology.Simple(path);
            if (orderedHoles && (TwiceArea(path) > 0) != (paths.Count == 0)) path.Reverse();
            paths.Add(path);
        }
        if (orderedHoles) TimberContourTopology.OuterAndHoles(paths);
        return paths;
    }
    internal static Paths64[] Pieces(Paths64 region)
    {
        var tree = new PolyTree64();
        Clipper.BooleanOp(ClipType.Union, region, new Paths64(), tree, FillRule.NonZero);
        var result = new List<Paths64>();
        Visit(tree);
        return result.OrderBy(p => p[0][0].X).ThenBy(p => p[0][0].Y).ThenBy(p => -Area(p)).ToArray();

        void Visit(PolyPath64 parent)
        {
            for (int i = 0; i < parent.Count; i++)
            {
                var child = parent[i];
                if (!child.IsHole && child.Polygon is { } outside)
                {
                    var paths = new Paths64 { Canonical(outside, true) };
                    for (int j = 0; j < child.Count; j++)
                        if (child[j].Polygon is { } hole) paths.Add(Canonical(hole, false));
                    foreach (var path in paths) TimberContourTopology.Simple(path);
                    TimberContourTopology.OuterAndHoles(paths);
                    result.Add(new Paths64(paths.Take(1).Concat(paths.Skip(1).OrderBy(p => p[0].X).ThenBy(p => p[0].Y))));
                }
                Visit(child);
            }
        }
    }
    private static Path64 Canonical(Path64 original, bool positive)
    {
        var path = new Path64(original);
        if ((TwiceArea(path) > 0) != positive) path.Reverse();
        int start = Enumerable.Range(0, path.Count).OrderBy(i => path[i].X).ThenBy(i => path[i].Y).First();
        return new Path64(Enumerable.Range(0, path.Count).Select(i => path[(start + i) % path.Count]));
    }
    internal static TimberPoint2[][] Decode(Paths64 paths) => paths.Select(p => p.Select(v => new TimberPoint2(v.X / Grid, v.Y / Grid)).ToArray()).ToArray();
    internal static double Area(Paths64 paths) => (double)TwiceArea(paths) / 2 / Grid / Grid;
    internal static bool Relates(Paths64 piece, Paths64 aperture, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        bool area = Clipper.Intersect(piece, aperture, FillRule.NonZero).Count > 0;
        cancellationToken.ThrowIfCancellationRequested();
        return area || piece.Any(a => aperture.Any(b => TimberContourTopology.SharesEdge(a, b, cancellationToken)));
    }
    internal static Int128 TwiceArea(Path64 p)
    {
        Int128 result = 0;
        for (int i = 0; i < p.Count; i++)
        { var a = p[i]; var b = p[(i + 1) % p.Count]; result += (Int128)a.X * b.Y - (Int128)a.Y * b.X; }
        return result;
    }
    internal static Int128 TwiceArea(Paths64 paths) => paths.Aggregate((Int128)0, (a, p) => a + TwiceArea(p));
}
