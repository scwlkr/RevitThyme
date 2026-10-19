using Clipper2Lib;
using System.Collections.Immutable;

namespace RevitThyme.Core;

internal static class TimberJointOpenings
{
    internal static bool PreserveWindows(TimberSnapshot snapshot, TimberFoldProfile profile, TimberPanel receiver,
        Paths64 removed, Paths64 after, List<TimberDiagnostic> diagnostics, CancellationToken cancellationToken)
    {
        bool retained = true;
        var face = snapshot.Faces.Single(f => f.Id == receiver.FaceId);
        double x = face.RingsFeet[0].Min(p => p.X), y = face.RingsFeet[0].Min(p => p.Y), factor = 12 / profile.ScaleDenominator;
        foreach (var opening in snapshot.Openings.Where(o => o.HostFaceId == receiver.FaceId && o.Kind == TimberOpeningKind.Window))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var aperture = TimberContours.Encode(opening.VoidRingsFeet, x, y, factor, true);
            var glass = TimberWindowContours.GlazingCut(opening, x, y, factor);
            bool exposed = TimberContours.Pieces(glass).Any(p =>
                Clipper.PointInPolygon(p[0][0], after[0]) != PointInPolygonResult.IsInside ||
                TimberContourTopology.Touch(p[0], after[0], cancellationToken));
            if (Clipper.Intersect(removed, aperture, FillRule.NonZero).Count == 0 && !exposed) continue;
            retained = false;
            diagnostics.Add(new("joint_window_loss", TimberSeverity.Error,
                "Joint would remove retained window material or expose a glazing cut to the panel outside; its architecture is not preserved.", opening.SourceId, opening.Id));
        }
        return retained;
    }
    internal static ImmutableArray<TimberOpeningPartMap> Remap(TimberSnapshot snapshot, TimberFoldProfile profile,
        IReadOnlyList<TimberPanel> panels, List<TimberDiagnostic> diagnostics, CancellationToken cancellationToken)
    {
        var map = ImmutableArray.CreateBuilder<TimberOpeningPartMap>();
        foreach (var opening in snapshot.Openings.OrderBy(o => o.Id, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var face = snapshot.Faces.Single(f => f.Id == opening.HostFaceId);
            double x = face.RingsFeet[0].Min(p => p.X), y = face.RingsFeet[0].Min(p => p.Y);
            var aperture = TimberContours.Encode(opening.VoidRingsFeet, x, y, 12 / profile.ScaleDenominator, true);
            var related = panels.Where(p => p.FaceId == face.Id && TimberContours.Relates(Encode(p), aperture, cancellationToken))
                .Select(p => p.Id).Order(StringComparer.Ordinal).ToImmutableArray();
            if (related.IsEmpty) diagnostics.Add(new("joint_opening_loss", TimberSeverity.Error,
                "No fitted panel has retained material or positive-length cut boundary corresponding to this opening.", opening.SourceId, opening.Id));
            map.Add(new(opening.SourceId, opening.Id, face.Id, related));
        }
        return map.ToImmutable();
    }
    private static Paths64 Encode(TimberPanel panel) => TimberContours.Encode(panel.RingsInches.Select(r => r.ToArray()).ToArray(), 0, 0, 1, true);
}
