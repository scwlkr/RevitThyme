using Clipper2Lib;

namespace RevitThyme.Core;

internal static class TimberPanelWebs
{
    // Checks actual remaining frame/web material, including holes belonging to different
    // windows. Tapered tips and concave outer-only necks still need separate qualification.
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
    }
}
