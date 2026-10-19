using Clipper2Lib;
using System.Numerics;
using Q = RevitThyme.Core.TimberPrismRational;
using Point = RevitThyme.Core.TimberPrismPoint;

namespace RevitThyme.Core;

internal sealed record TimberJointShadow(Paths64 Cuts, double ClearanceU, double ClearanceV, double ClearanceDepth,
    double GridExcessAreaSum, double AxisBoxExpansionAreaSum)
{
    internal static TimberJointShadow Create(TimberPanel owner, TimberPanel receiver, Paths64 receiverRegion,
        double clearance, TimberPrismBudget budget)
    {
        var frame = new TimberJointFrame(receiver, clearance);
        long gapU = TimberJointHull.Ceiling(frame.ClearanceU * Q.From(TimberContours.Grid));
        long gapV = TimberJointHull.Ceiling(frame.ClearanceV * Q.From(TimberContours.Grid));
        var points = receiverRegion.SelectMany(p => p).ToArray();
        var minU = Q.From(points.Min(p => p.X)) / Q.From(TimberContours.Grid) - frame.ClearanceU;
        var maxU = Q.From(points.Max(p => p.X)) / Q.From(TimberContours.Grid) + frame.ClearanceU;
        var minV = Q.From(points.Min(p => p.Y)) / Q.From(TimberContours.Grid) - frame.ClearanceV;
        var maxV = Q.From(points.Max(p => p.Y)) / Q.From(TimberContours.Grid) + frame.ClearanceV;
        // UV bounds only discard owner material whose clearance box cannot reach this finite
        // receiver. Depth is enlarged before projection so exact contact also produces a cut.
        TimberPrismPlane[] planes = [new(new(0, 0, -1), frame.ClearanceDepth),
            new(new(0, 0, 1), Q.From(receiver.ThicknessInches) + frame.ClearanceDepth),
            new(new(-1, 0, 0), 0 - minU), new(new(1, 0, 0), maxU),
            new(new(0, -1, 0), 0 - minV), new(new(0, 1, 0), maxV)];
        var origin = Point.From(owner.Frame.OriginInches); var u = Point.From(owner.Frame.U); var v = Point.From(owner.Frame.V);
        var depth = frame.Vector(Point.From(owner.Frame.OutwardNormal) * (0 - Q.From(owner.ThicknessInches)));
        Point Local(TimberPoint2 p) => frame.Local(origin + u * Q.From(p.X) + v * Q.From(p.Y));
        var cuts = new Paths64(); Q gridExcess = 0, boxExpansion = 0;
        foreach (var triangle in owner.Triangles)
        {
            budget.Check();
            var cell = new TimberPrismCell(Local(triangle.A), Local(triangle.B), Local(triangle.C), depth);
            var clipped = TimberPrismClipping.ClipVertices(cell, planes, budget);
            if (clipped.Length == 0) continue;
            var exact = TimberJointHull.Exact(clipped, budget);
            if (exact.Length < 3) continue;
            var hull = TimberJointHull.Grid(exact, budget);
            var expanded = TimberJointHull.Expand(hull, gapU, gapV, budget);
            var enclosure = GridArea(hull) - TimberJointHull.Area(exact);
            if (enclosure < 0) throw new TimberPrismUnresolved("joint_enclosure", "Grid enclosure lost exact shadow area.");
            gridExcess += enclosure; boxExpansion += GridArea(expanded) - GridArea(hull);
            cuts.Add(expanded);
        }
        budget.Check();
        return new(Clipper.Union(cuts, FillRule.NonZero), gapU / TimberContours.Grid, gapV / TimberContours.Grid,
            frame.ClearanceDepth.Approximate(), UpperDouble(gridExcess), UpperDouble(boxExpansion));
    }
    private static Q GridArea(Path64 path) => new(BigInteger.CreateChecked(TimberContours.TwiceArea(path)),
        2 * BigInteger.Pow(10, 16));
    private static double UpperDouble(Q area)
    {
        double value = area.Approximate();
        while (Q.From(value) < area) value = Math.BitIncrement(value);
        return value;
    }
}
