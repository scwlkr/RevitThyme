using System.Collections.Immutable;

namespace RevitThyme.Core;

public static class TimberFabrication
{
    public static TimberPanelDraft Draft(TimberSnapshot snapshot, TimberFoldProfile profile, TimberScope scope,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var diagnostics = TimberInspection.GetDiagnostics(snapshot, profile, scope).ToList();
        var panels = new List<TimberPanel>();
        var faces = new List<TimberFacePartMap>();
        var openings = new List<TimberOpeningPartMap>();
        var datum = snapshot.Faces.OrderBy(f => f.Id, StringComparer.Ordinal).FirstOrDefault()?.OriginFeet ?? new(0, 0, 0);
        if (diagnostics.Any(d => d.Severity == TimberSeverity.Error)) return Result();
        double factor = 12 / profile.ScaleDenominator;
        foreach (var face in snapshot.Faces.OrderBy(f => f.Id, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var x = face.RingsFeet[0].Min(p => p.X); var y = face.RingsFeet[0].Min(p => p.Y);
                var origin = new TimberPoint3((face.OriginFeet.X - datum.X + x * face.U.X + y * face.V.X) * factor,
                    (face.OriginFeet.Y - datum.Y + x * face.U.Y + y * face.V.Y) * factor,
                    (face.OriginFeet.Z - datum.Z + x * face.U.Z + y * face.V.Z) * factor);
                var frame = new TimberPanelFrame(origin, face.U, face.V, face.Normal);
                var region = TimberContours.Encode(face.RingsFeet, x, y, factor, true);
                var hosted = snapshot.Openings.Where(o => o.HostFaceId == face.Id).OrderBy(o => o.Id, StringComparer.Ordinal).ToArray();
                region = TimberWindowContours.Cut(region, hosted, x, y, factor, profile.MinimumBarInches, diagnostics, cancellationToken);
                var pieces = TimberContours.Pieces(region);
                if (pieces.Length == 0) throw new InvalidOperationException("Supported source face produced no connected panels.");
                if (pieces.Aggregate((Int128)0, (a, p) => a + TimberContours.TwiceArea(p)) != TimberContours.TwiceArea(region))
                    throw new InvalidOperationException("Connected-panel decomposition lost source material area.");
                cancellationToken.ThrowIfCancellationRequested();
                var ids = new List<string>();
                for (int i = 0; i < pieces.Length; i++)
                {
                    var piece = pieces[i]; var id = $"{face.Id}:P{i + 1}";
                    if (TimberContours.Area(piece) <= 0.0000000001)
                        throw new InvalidOperationException("A connected panel is no larger than the 0.00001 in contour-tolerance square; its sliver is unresolved.");
                    TimberPanelWebs.Validate(piece, profile.MinimumBarInches, face, diagnostics, cancellationToken);
                    panels.Add(new(id, face.SourceId, face.Id, face.Kind, frame, profile.ThicknessInches,
                        TimberContours.Decode(piece).Select(r => r.ToImmutableArray()).ToImmutableArray(),
                        TimberTriangulation.Create(piece).ToImmutableArray(), TimberContours.Area(piece)));
                    cancellationToken.ThrowIfCancellationRequested();
                    ids.Add(id);
                }
                faces.Add(new(face.SourceId, face.Id, ids.ToImmutableArray()));
                foreach (var opening in hosted)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var aperture = TimberContours.Encode(opening.VoidRingsFeet, x, y, factor, true);
                    var related = Enumerable.Range(0, pieces.Length).Where(i => TimberContours.Relates(pieces[i], aperture, cancellationToken)).Select(i => ids[i]).ToArray();
                    if (related.Length == 0) diagnostics.Add(new("unaccounted_opening", TimberSeverity.Error,
                        "Opening has no contributing connected fabrication panel.", opening.SourceId, opening.Id));
                    openings.Add(new(opening.SourceId, opening.Id, face.Id, related.ToImmutableArray()));
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or Clipper2Lib.ClipperLibException)
            { diagnostics.Add(new("invalid_contour", TimberSeverity.Error, ex.Message, face.SourceId, face.Id)); }
        }
        return Result();

        TimberPanelDraft Result() => new(diagnostics.Any(d => d.Severity == TimberSeverity.Error) ? TimberDraftState.Blocked :
            TimberDraftState.ContoursValidated, datum, panels.ToImmutableArray(), faces.ToImmutableArray(), openings.ToImmutableArray(), diagnostics.ToImmutableArray());
    }
}
