using System.Collections.Immutable;

namespace RevitThyme.Core;

internal sealed class TimberCutFailure(string code, string message) : Exception(message)
{
    internal string Code { get; } = code;
}

internal sealed record TimberCutCalibration(ImmutableArray<TimberPanel> Coupons, TimberStockPlan Layout)
{
    internal static TimberCutCalibration Create(TimberFoldProfile profile, CancellationToken ct)
    {
        double bar = profile.MinimumBarInches, depth = profile.ThicknessInches + profile.JointClearanceInches;
        double arm = Math.Max(1, depth * 2);
        var square = Rectangle("calibration-square", 1, 1);
        var minimumBar = Rectangle("calibration-bar", 1, bar);
        var corner = Panel("calibration-corner",
            [new(0, 0), new(arm, 0), new(arm, depth), new(depth, depth), new(depth, arm), new(0, arm)],
            [new(new(0, 0), new(arm, 0), new(arm, depth)), new(new(0, 0), new(arm, depth), new(0, depth)),
             new(new(0, depth), new(depth, depth), new(depth, arm)), new(new(0, depth), new(depth, arm), new(0, arm))],
            arm * depth + depth * (arm - depth));
        ImmutableArray<TimberPanel> coupons = [square, minimumBar, corner];
        var layout = TimberStockLayout.Create(coupons, profile, ct);
        if (!layout.Planned || layout.SheetCount != 1 || layout.Placements.Length != coupons.Length)
            throw new TimberCutFailure("calibration_stock", "Calibration square, minimum-bar and corner coupons must fit one sheet with the configured border, gap, rotation and grain constraints.");
        return new(coupons, layout);

        TimberPanel Rectangle(string id, double width, double height) => Panel(id,
            [new(0, 0), new(width, 0), new(width, height), new(0, height)],
            [new(new(0, 0), new(width, 0), new(width, height)), new(new(0, 0), new(width, height), new(0, height))], width * height);
        TimberPanel Panel(string id, ImmutableArray<TimberPoint2> ring, ImmutableArray<TimberTriangle> triangles, double area) =>
            new(id, "calibration", id, TimberFaceKind.Wall,
                new(new(0, 0, 0), new(1, 0, 0), new(0, 1, 0), new(0, 0, 1)), profile.ThicknessInches, [ring], triangles, area);
    }
}
