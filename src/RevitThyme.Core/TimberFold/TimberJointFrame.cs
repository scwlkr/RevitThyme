using Q = RevitThyme.Core.TimberPrismRational;
using Point = RevitThyme.Core.TimberPrismPoint;

namespace RevitThyme.Core;

internal sealed class TimberJointFrame
{
    private readonly Point origin;
    private readonly Point rowU;
    private readonly Point rowV;
    private readonly Point rowDepth;
    internal Q ClearanceU { get; }
    internal Q ClearanceV { get; }
    internal Q ClearanceDepth { get; }
    internal TimberJointFrame(TimberPanel panel, double clearance)
    {
        origin = Point.From(panel.Frame.OriginInches);
        var u = Point.From(panel.Frame.U); var v = Point.From(panel.Frame.V);
        var depth = Point.From(panel.Frame.OutwardNormal) * -1;
        var determinant = u.Dot(v.Cross(depth));
        if (determinant == 0) throw new TimberPrismUnresolved("joint_frame", "Joint receiver frame is singular.");
        rowU = v.Cross(depth) * ((Q)1 / determinant);
        rowV = depth.Cross(u) * ((Q)1 / determinant);
        rowDepth = u.Cross(v) * ((Q)1 / determinant);
        ClearanceU = Q.From(clearance) * NormUpper(rowU);
        ClearanceV = Q.From(clearance) * NormUpper(rowV);
        ClearanceDepth = Q.From(clearance) * NormUpper(rowDepth);
    }
    internal Point Local(Point world)
    {
        var delta = world - origin;
        return new(delta.Dot(rowU), delta.Dot(rowV), delta.Dot(rowDepth));
    }
    internal Point Vector(Point world) => new(world.Dot(rowU), world.Dot(rowV), world.Dot(rowDepth));
    private static Q NormUpper(Point row)
    {
        var square = row.Dot(row);
        double value = Math.Sqrt(square.Approximate());
        if (!double.IsFinite(value) || value <= 0)
            throw new TimberPrismUnresolved("joint_frame", "Joint frame has no bounded physical clearance conversion.");
        var upper = Q.From(value);
        while (upper * upper < square) { value = Math.BitIncrement(value); upper = Q.From(value); }
        return upper;
    }
}
