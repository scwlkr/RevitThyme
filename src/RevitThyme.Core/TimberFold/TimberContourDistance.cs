using Clipper2Lib;

namespace RevitThyme.Core;

internal static class TimberContourDistance
{
    internal static double Between(Path64 a, Path64 b, CancellationToken cancellationToken = default)
    {
        if (TimberContourTopology.Touch(a, b, cancellationToken)) return 0;
        double distance = double.PositiveInfinity;
        for (int i = 0; i < a.Count; i++)
        for (int j = 0; j < b.Count; j++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var p = a[i]; var q = a[(i + 1) % a.Count]; var r = b[j]; var s = b[(j + 1) % b.Count];
            distance = Math.Min(distance, Math.Min(Math.Min(PointSegment(p, r, s), PointSegment(q, r, s)),
                Math.Min(PointSegment(r, p, q), PointSegment(s, p, q))));
        }
        return distance / TimberContours.Grid;
    }
    private static double PointSegment(Point64 p, Point64 a, Point64 b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / (dx * dx + dy * dy), 0, 1);
        double x = p.X - a.X - t * dx, y = p.Y - a.Y - t * dy;
        return Math.Sqrt(x * x + y * y);
    }
}
