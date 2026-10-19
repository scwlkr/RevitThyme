using Q = RevitThyme.Core.TimberPrismRational;

namespace RevitThyme.Core;

internal readonly record struct TimberPrismPoint(Q X, Q Y, Q Z)
{
    internal Q this[int axis] => axis == 0 ? X : axis == 1 ? Y : Z;
    internal static TimberPrismPoint From(TimberPoint3 p) => new(Q.From(p.X), Q.From(p.Y), Q.From(p.Z));
    internal Q Dot(TimberPrismPoint b) => X * b.X + Y * b.Y + Z * b.Z;
    internal TimberPrismPoint Cross(TimberPrismPoint b) => new(Y * b.Z - Z * b.Y, Z * b.X - X * b.Z, X * b.Y - Y * b.X);
    public static TimberPrismPoint operator +(TimberPrismPoint a, TimberPrismPoint b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static TimberPrismPoint operator -(TimberPrismPoint a, TimberPrismPoint b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static TimberPrismPoint operator *(TimberPrismPoint a, Q factor) => new(a.X * factor, a.Y * factor, a.Z * factor);
}

internal sealed record TimberPrismPlane(TimberPrismPoint Normal, Q Offset)
{
    internal Q Distance(TimberPrismPoint point) => Normal.Dot(point) - Offset;
    internal static TimberPrismPlane From(IReadOnlyList<TimberPrismPoint> face)
    {
        var normal = (face[1] - face[0]).Cross(face[2] - face[0]);
        return new(normal, normal.Dot(face[0]));
    }
}

internal sealed class TimberPrismBounds
{
    private readonly Q[] minimum;
    private readonly Q[] maximum;
    internal TimberPrismBounds(IEnumerable<TimberPrismPoint> points)
    {
        var vertices = points.ToArray();
        minimum = Enumerable.Range(0, 3).Select(axis => vertices.Min(p => p[axis])).ToArray();
        maximum = Enumerable.Range(0, 3).Select(axis => vertices.Max(p => p[axis])).ToArray();
    }
    internal bool Overlaps(TimberPrismBounds other) => Enumerable.Range(0, 3).All(axis =>
        maximum[axis] > other.minimum[axis] && other.maximum[axis] > minimum[axis]);
}

internal sealed class TimberPrismCell
{
    internal TimberPrismPoint[][] Faces { get; }
    internal TimberPrismPlane[] Planes { get; }
    internal TimberPrismBounds Bounds { get; }
    internal TimberPrismCell(TimberPrismPoint a, TimberPrismPoint b, TimberPrismPoint c, TimberPrismPoint depth)
    {
        var d = a + depth; var e = b + depth; var f = c + depth;
        TimberPrismPoint[] vertices = [a, b, c, d, e, f];
        var center = vertices.Aggregate(new TimberPrismPoint(0, 0, 0), (sum, p) => sum + p) * ((Q)1 / 6);
        Faces = [[a, b, c], [f, e, d], [a, d, e, b], [b, e, f, c], [c, f, d, a]];
        foreach (var face in Faces)
        {
            var plane = TimberPrismPlane.From(face);
            if (plane.Normal.Dot(plane.Normal) == 0) throw new TimberPrismUnresolved("degenerate_triangle", "A prism cell has a degenerate triangular face.");
            if (plane.Distance(center) > 0) Array.Reverse(face);
        }
        Planes = Faces.Select(TimberPrismPlane.From).ToArray();
        Bounds = new(vertices);
    }
}
