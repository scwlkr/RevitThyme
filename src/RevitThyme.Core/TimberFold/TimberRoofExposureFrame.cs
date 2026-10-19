using Q = RevitThyme.Core.TimberPrismRational;
using Point = RevitThyme.Core.TimberPrismPoint;

namespace RevitThyme.Core;

// Exact vertical projection onto a finite, nonvertical roof's affine UV plane.
internal sealed class TimberRoofExposureFrame
{
    private readonly Point origin, u, v;
    private readonly Q determinant;
    internal TimberRoofExposureFrame(TimberPanel receiver)
    {
        origin = Point.From(receiver.Frame.OriginInches); u = Point.From(receiver.Frame.U); v = Point.From(receiver.Frame.V);
        determinant = u.X * v.Y - u.Y * v.X;
        if (determinant == 0) throw new TimberPrismUnresolved("roof_exposure_frame", "A vertical roof cannot define a world-up exposure envelope.");
    }
    internal Point Project(Point world)
    {
        var delta = world - origin;
        var x = (delta.X * v.Y - delta.Y * v.X) / determinant;
        var y = (u.X * delta.Y - u.Y * delta.X) / determinant;
        return new(x, y, world.Z - (origin.Z + u.Z * x + v.Z * y));
    }
    internal static Point[] Higher(Point[] triangle, bool ownsCoplanar, TimberPrismBudget budget)
    {
        if (triangle.All(p => p.Z == 0)) return ownsCoplanar ? triangle : [];
        if (triangle.All(p => p.Z <= 0)) return [];
        var result = new List<Point>();
        for (int i = 0; i < triangle.Length; i++)
        {
            budget.Check(); var a = triangle[i]; var b = triangle[(i + 1) % triangle.Length];
            if (a.Z >= 0) result.Add(a);
            if ((a.Z < 0 && b.Z > 0) || (a.Z > 0 && b.Z < 0))
                result.Add(a + (b - a) * ((0 - a.Z) / (b.Z - a.Z)));
        }
        return result.Distinct().ToArray();
    }
}
