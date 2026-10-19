using System.Text.Json;
using System.IO;
using RevitThyme.Core;

namespace RevitThyme.Addin;

// Independent canonical stock transform and literal calibration geometry; no writer rings.
internal static class NativeTimberKitContours
{
    internal sealed record Ring(string PanelId, int Index, TimberPoint2[] Points);
    internal static Dictionary<string, Ring[]> Expected(TimberNativeKitPlan plan, string directory, CancellationToken ct)
    {
        var files = new Dictionary<string, List<Ring>>(StringComparer.Ordinal);
        foreach (var placement in plan.Stock.Placements)
        {
            ct.ThrowIfCancellationRequested();
            string file = FormattableString.Invariant($"CUT-{placement.Sheet:000}.dxf");
            if (!files.TryGetValue(file, out var rings)) files.Add(file, rings = []);
            var panel = plan.Fitted.Draft.Panels.Single(p => p.Id == placement.PanelId);
            for (int i = 0; i < panel.RingsInches.Length; i++)
                rings.Add(new(panel.Id, i, panel.RingsInches[i].Select(p => Transform(p, placement)).ToArray()));
        }
        var calibration = new List<Ring>(); files.Add("CALIBRATION.dxf", calibration);
        using var receipt = JsonDocument.Parse(System.IO.File.ReadAllBytes(Path.Combine(directory, "cutting", "cut-staging.json")));
        foreach (var item in Property(receipt.RootElement, "calibrationPlacements").EnumerateArray())
        {
            ct.ThrowIfCancellationRequested();
            string id = Property(item, "panelId").GetString() ?? throw new InvalidDataException("Calibration identity is missing.");
            double bar = plan.Profile.MinimumBarInches, width = plan.Profile.ThicknessInches + plan.Profile.JointClearanceInches, arm = Math.Max(1, 2 * width);
            TimberPoint2[] ring = id switch
            {
                "calibration-square" => [new(0, 0), new(1, 0), new(1, 1), new(0, 1)],
                "calibration-bar" => [new(0, 0), new(1, 0), new(1, bar), new(0, bar)],
                "calibration-corner" => [new(0, 0), new(arm, 0), new(arm, width), new(width, width), new(width, arm), new(0, arm)],
                _ => throw new InvalidDataException("Unexpected calibration coupon.")
            };
            var min = Property(item, "localMinimum"); var max = Property(item, "localMaximum");
            var placement = new TimberStockPlacement(id, 1, Number(item, "xInches"), Number(item, "yInches"), Property(item, "quarterTurn").GetBoolean(),
                Number(item, "widthInches"), Number(item, "heightInches"), new(Number(min, "x"), Number(min, "y")), new(Number(max, "x"), Number(max, "y")));
            calibration.Add(new(id, 0, ring.Select(p => Transform(p, placement)).ToArray()));
        }
        if (calibration.Count != 3 || calibration.Select(r => r.PanelId).Distinct(StringComparer.Ordinal).Count() != 3)
            throw new InvalidDataException("Native calibration must account for all three coupons exactly once.");
        return files.ToDictionary(p => p.Key, p => p.Value.ToArray(), StringComparer.Ordinal);
    }
    private static TimberPoint2 Transform(TimberPoint2 p, TimberStockPlacement placement) => placement.QuarterTurn
        ? new(placement.XInches + placement.LocalMaximum.Y - p.Y, placement.YInches + p.X - placement.LocalMinimum.X)
        : new(placement.XInches + p.X - placement.LocalMinimum.X, placement.YInches + p.Y - placement.LocalMinimum.Y);
    private static JsonElement Property(JsonElement value, string name) => value.EnumerateObject()
        .Single(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)).Value;
    private static double Number(JsonElement value, string name) => Property(value, name).GetDouble();
}
