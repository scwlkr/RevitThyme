using Clipper2Lib;

namespace RevitThyme.Core;

internal static class TimberWindowContours
{
    internal static Paths64 Cut(Paths64 face, TimberOpening[] openings, double x, double y, double factor,
        double minimumBar, List<TimberDiagnostic> diagnostics, CancellationToken cancellationToken)
    {
        var envelope = new Paths64 { face[0] };
        var region = face;
        var cuts = new Paths64();
        foreach (var opening in openings)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var aperture = TimberContours.Encode(opening.VoidRingsFeet, x, y, factor, true);
            // Native ground-level doorways already notch the outer wall boundary. Their void
            // is outside the retained material, so containment/restoration would be incorrect.
            if (opening.Kind != TimberOpeningKind.Window) { cuts.AddRange(aperture); continue; }
            if (Clipper.Difference(aperture, envelope, FillRule.NonZero).Count != 0)
                throw new InvalidOperationException($"Opening {opening.Id} extends beyond its captured host outline.");
            // Revit's finish face already contains full apertures. Restore each to obtain the
            // common host envelope, then subtract only actual cuts (pane minus opaque material).
            region = Clipper.Union(region, aperture, FillRule.NonZero);
            var panes = Clipper.Union(TimberContours.Encode(opening.PaneRingsFeet, x, y, factor, false), FillRule.NonZero);
            var opaque = Clipper.Union(TimberContours.Encode(opening.OpaqueRingsFeet ?? [], x, y, factor, false), FillRule.NonZero);
            if (Clipper.Difference(panes, aperture, FillRule.NonZero).Count != 0)
                throw new InvalidOperationException($"Glazing for {opening.Id} extends outside its native aperture.");
            var glazingCuts = Clipper.Difference(panes, opaque, FillRule.NonZero);
            if (glazingCuts.Count == 0)
                throw new InvalidOperationException($"Window {opening.Id} has no resolved pane area after opaque projection.");
            var cutouts = TimberContours.Pieces(glazingCuts);
            for (int i = 0; i < cutouts.Length; i++)
            for (int j = 0; j < i; j++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                double width = TimberContourDistance.Between(cutouts[i][0], cutouts[j][0], cancellationToken);
                if (width < minimumBar - 0.00001)
                    diagnostics.Add(new("window_web", TimberSeverity.Error,
                        FormattableString.Invariant($"Retained glazing web is {width:G9} in; requires at least {minimumBar:G9} in (0.00001 in numeric allowance)."),
                        opening.SourceId, opening.Id));
            }
            cuts.AddRange(glazingCuts);
        }
        var result = Clipper.Difference(region, Clipper.Union(cuts, FillRule.NonZero), FillRule.NonZero);
        cancellationToken.ThrowIfCancellationRequested();
        if (result.Count == 0) throw new InvalidOperationException("Opening cuts remove the complete source face.");
        return result;
    }
}
