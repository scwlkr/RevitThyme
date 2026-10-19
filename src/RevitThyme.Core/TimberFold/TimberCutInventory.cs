using System.Collections.Immutable;

namespace RevitThyme.Core;

internal sealed record TimberCutDrawing(string Name, ImmutableArray<ImmutableArray<TimberPoint2>> Contours);
internal sealed record TimberCutInventory(ImmutableArray<TimberCutDrawing> Drawings, ImmutableArray<TimberCutPart> Parts,
    TimberCutCalibration Calibration)
{
    internal static TimberCutInventory Create(IReadOnlyList<TimberPanel> panels, TimberStockPlan stock,
        TimberFoldProfile profile, CancellationToken cancellationToken)
    {
        if (!stock.Planned || profile.Validate().Length != 0) throw new InvalidDataException("A valid profile and complete stock plan are required.");
        var canonical = TimberStockLayout.Create(panels, profile, cancellationToken);
        if (!canonical.Planned || canonical.SheetCount != stock.SheetCount || !canonical.Placements.SequenceEqual(stock.Placements))
            throw new InvalidDataException("Stock placements must match the complete deterministic panel inventory and selected profile.");
        var drawings = ImmutableArray.CreateBuilder<TimberCutDrawing>();
        var parts = ImmutableArray.CreateBuilder<TimberCutPart>();
        for (int sheet = 1; sheet <= stock.SheetCount; sheet++)
        {
            string name = FormattableString.Invariant($"CUT-{sheet:000}.dxf");
            var contours = ImmutableArray.CreateBuilder<ImmutableArray<TimberPoint2>>();
            foreach (var placement in stock.Placements.Where(p => p.Sheet == sheet).OrderBy(p => p.PanelId, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var panel = panels.Single(p => p.Id == placement.PanelId);
                if (string.IsNullOrWhiteSpace(panel.SourceId) || string.IsNullOrWhiteSpace(panel.FaceId) ||
                    panel.RingsInches.IsDefaultOrEmpty || panel.RingsInches.Any(r => r.Length < 3 || r.Length > 16384 ||
                    r.Any(p => !double.IsFinite(p.X) || !double.IsFinite(p.Y))))
                    throw new InvalidDataException("Every cut part needs traceable source/face identities and finite polygon rings.");
                parts.Add(new(panel.Id, panel.SourceId, panel.FaceId, name, contours.Count, panel.RingsInches.Length, placement));
                foreach (var ring in panel.RingsInches) contours.Add(ring.Select(placement.Project).ToImmutableArray());
            }
            drawings.Add(new(name, contours.ToImmutable()));
        }
        // Separate nominal calibration sheet obeys the same physical stock policy.
        var calibration = TimberCutCalibration.Create(profile, cancellationToken);
        drawings.Add(new("CALIBRATION.dxf", calibration.Coupons.Select(coupon => coupon.RingsInches[0]
            .Select(calibration.Layout.Placements.Single(p => p.PanelId == coupon.Id).Project).ToImmutableArray()).ToImmutableArray()));
        return new(drawings.ToImmutable(), parts.ToImmutable(), calibration);
    }
}
