using Clipper2Lib;

namespace RevitThyme.Core;

internal static class TimberPanelWebs
{
    // Checks actual remaining frame/web material, including holes belonging to different
    // windows. Outer-detail detection certifies witnesses, not physical strength.
    internal static void Validate(Paths64 piece, double minimumBar, TimberFace face,
        List<TimberDiagnostic> diagnostics, CancellationToken cancellationToken)
    {
        for (int i = 1; i < piece.Count; i++)
        for (int j = 0; j < i; j++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            double width = TimberContourDistance.Between(piece[i], piece[j], cancellationToken);
            if (width < minimumBar - 0.00001)
                diagnostics.Add(new("panel_web", TimberSeverity.Error,
                    FormattableString.Invariant($"Retained panel material between {(j == 0 ? "a cut and its outside edge" : "cuts")} is {width:G9} in; requires {minimumBar:G9} in (0.00001 in numeric allowance)."),
                    face.SourceId, face.Id));
        }
        try
        {
            var budget = new TimberFragilityGeometry.Budget(cancellationToken);
            var rings = piece.Select(r => r.Select(p => { budget.Check(); return TimberFragilityGeometry.P.From(p); }).ToArray()).ToArray();
            if (TimberOuterNecks.Find(rings, minimumBar, budget) is { } neck)
                diagnostics.Add(new("panel_neck", TimberSeverity.Error,
                    FormattableString.Invariant($"Certified outer material neck between edges {neck.FirstEdge}/{neck.SecondEdge} is {neck.Width:G9} in; requires {minimumBar:G9} in (0.00001 in numeric allowance). Panel-local inch cut {TimberFragilityGeometry.Inches(neck.CutStart)} to {TimberFragilityGeometry.Inches(neck.CutEnd)} separates material discs centered at {TimberFragilityGeometry.Inches(neck.FirstDisc)} and {TimberFragilityGeometry.Inches(neck.SecondDisc)}, radius {minimumBar / 2:G9} in. Physical strength remains unqualified."), face.SourceId, face.Id));
            if (TimberTaperedTips.Find(rings, minimumBar, budget) is { } tip)
                diagnostics.Add(new("panel_tip", TimberSeverity.Warning,
                    FormattableString.Invariant($"Potential fragile tapered tip at outer vertex {tip.Vertex}: finite material section {tip.Width:G9} in wide at {tip.Depth:G9} in inward depth (nominal depth {minimumBar:G9} in, existing 0.00001 in numeric allowance). Physical cutting and strength remain unqualified; absence of a witness does not certify safety."), face.SourceId, face.Id));
        }
        catch (TimberPrismUnresolved ex)
        {
            cancellationToken.ThrowIfCancellationRequested();
            diagnostics.Add(new("panel_fragility_unresolved", TimberSeverity.Error, ex.Message, face.SourceId, face.Id));
        }
    }
}
