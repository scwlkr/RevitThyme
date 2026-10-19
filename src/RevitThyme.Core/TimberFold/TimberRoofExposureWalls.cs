using Clipper2Lib;
using System.Collections.Immutable;
using System.Globalization;
using Q = RevitThyme.Core.TimberPrismRational;
using Point = RevitThyme.Core.TimberPrismPoint;

namespace RevitThyme.Core;

internal static class TimberRoofExposureWalls
{
    internal static ImmutableArray<TimberPanel> Apply(ImmutableArray<TimberPanel> panels, TimberSnapshot snapshot, TimberFoldProfile profile,
        List<TimberWallEnvelopeRecord> records, List<TimberDiagnostic> diagnostics, TimberBoundaryBudget boundaryBudget, CancellationToken ct)
    {
        var roofs = panels.Where(p => p.Kind == TimberFaceKind.Roof).OrderBy(p => p.Id, StringComparer.Ordinal).ToArray();
        if (roofs.Length == 0) return panels;
        var output = panels.ToDictionary(p => p.Id); var budget = new TimberPrismBudget(ct);
        foreach (var wall in panels.Where(p => p.Kind == TimberFaceKind.Wall).OrderBy(p => p.Id, StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var frame = new TimberJointFrame(wall, profile.JointClearanceInches);
                var up = frame.Vector(new(0, 0, 1));
                if (up.Z != 0) throw new TimberPrismUnresolved("wall_envelope_frame", "World-up wall fitting requires an exactly vertical wall plane.");
                double Height(TimberPanel p, TimberPoint2 point) => p.Frame.OriginInches.Z + p.Frame.U.Z * point.X + p.Frame.V.Z * point.Y;
                double extent = Math.Max(1, roofs.Max(r => r.RingsInches.SelectMany(p => p).Max(p => Height(r, p))) -
                    wall.RingsInches[0].Min(p => Height(wall, p)) + 2 * (profile.ThicknessInches + profile.JointClearanceInches));
                var delta = up * Q.From(extent); var before = Encode(wall);
                var upper = new List<Path64>();
                foreach (var other in panels.Where(p => p.Kind == TimberFaceKind.Wall && p.Id != wall.Id))
                {
                    ct.ThrowIfCancellationRequested();
                    var a = Point.From(other.Frame.OutwardNormal); var b = Point.From(wall.Frame.OutwardNormal);
                    if ((a - b).Dot(a - b).Approximate() > 1e-18) continue;
                    var origin = Point.From(other.Frame.OriginInches); var u = Point.From(other.Frame.U); var v = Point.From(other.Frame.V);
                    var projected = other.RingsInches[0].Select(p => frame.Local(origin + u * Q.From(p.X) + v * Q.From(p.Y))).ToArray();
                    if (projected.Any(p => Math.Abs(p.Z.Approximate()) > .00000001)) continue;
                    var paths = TimberContours.Encode([projected.Select(p => new TimberPoint2(p.X.Approximate(), p.Y.Approximate())).ToArray()], 0, 0, 1, true);
                    upper.Add(paths[0]);
                }
                var continued = TimberRoofExposureSweep.Continued(before[0], upper, delta, budget);
                var fullBand = Clipper.Difference(TimberRoofExposureSweep.TopBands(before[0], delta, budget), before, FillRule.NonZero);
                fullBand = Clipper.Difference(fullBand, continued, FillRule.NonZero);
                if (fullBand.Count == 0) continue;
                var allowed = Clipper.Difference(TimberRoofExposureSweep.TopBands(before[0], up * Q.From(profile.RoofFitSearchInches), budget), before, FillRule.NonZero);
                allowed = Clipper.Difference(allowed, continued, FillRule.NonZero);
                var roofCuts = new Paths64(); var owners = new List<string>();
                foreach (var roof in roofs)
                {
                    ct.ThrowIfCancellationRequested();
                    var shadow = TimberJointShadow.Create(roof, wall, fullBand, profile.JointClearanceInches, budget);
                    if (shadow.Cuts.Count == 0) continue;
                    roofCuts.AddRange(shadow.Cuts); owners.Add(roof.Id);
                }
                if (roofCuts.Count == 0) continue;
                roofCuts = Clipper.Union(roofCuts, FillRule.NonZero);
                var below = TimberRoofExposureSweep.Sweep(roofCuts, delta * -1, budget);
                var above = TimberRoofExposureSweep.Sweep(roofCuts, delta, budget);
                var addition = Clipper.Difference(Clipper.Intersect(fullBand, below, FillRule.NonZero), above, FillRule.NonZero);
                if (addition.Count == 0) continue;
                if (Clipper.Difference(addition, allowed, FillRule.NonZero).Count != 0)
                    throw new TimberPrismUnresolved("wall_roof_search", "Required finite world-up wall growth exceeds RoofFitSearchInches; no clamped wall is accepted.");
                var partition = TimberGridBoolean.Union(before, addition, budget);
                var after = partition.Output; var pieces = TimberContours.Pieces(after);
                if (pieces.Length != 1) throw new TimberPrismUnresolved("wall_envelope_topology", "Roof fitting would split or detach its wall panel.");
                after = pieces[0]; addition = partition.Changed;
                var deltaArea = TimberContours.TwiceArea(after) - TimberContours.TwiceArea(partition.OriginalRefined);
                boundaryBudget.Charge(wall.Id, partition.Refinement);
                if (before.Count > 1 && Clipper.Intersect(addition, new Paths64(before.Skip(1)), FillRule.NonZero).Count != 0)
                    throw new TimberPrismUnresolved("wall_envelope_opening", "Wall growth would fill an original captured glazing or opening cut.");
                var face = snapshot.Faces.Single(f => f.Id == wall.FaceId);
                TimberPanelWebs.Validate(after, profile.MinimumBarInches, face, diagnostics, ct);
                if (!TimberJointOpenings.PreserveWindows(snapshot, profile, wall, [], after, diagnostics, ct))
                    throw new TimberPrismUnresolved("wall_envelope_opening", "Wall growth cannot preserve its original window architecture.");
                output[wall.Id] = wall with { RingsInches = Decode(after), Triangles = TimberTriangulation.Create(after).ToImmutableArray(), AreaSquareInches = TimberContours.Area(after) };
                records.Add(new(wall.Id, wall.SourceId, wall.FaceId, owners.ToImmutableArray(), Decode(addition), TimberContours.Area(addition),
                    deltaArea.ToString(CultureInfo.InvariantCulture), profile.RoofFitSearchInches, "World +Z; finite roof prism across complete receiver depth; UV/depth clearance box") { Refinement = partition.Refinement });
            }
            catch (TimberPrismUnresolved error)
            { ct.ThrowIfCancellationRequested(); diagnostics.Add(new(error.Code, TimberSeverity.Error, error.Message, wall.SourceId, wall.Id)); }
            catch (Exception error) when (error is InvalidOperationException or ClipperLibException or OverflowException)
            { diagnostics.Add(new("wall_envelope_geometry", TimberSeverity.Error, error.Message, wall.SourceId, wall.Id)); }
        }
        return output.Values.OrderBy(p => p.Id, StringComparer.Ordinal).ToImmutableArray();
    }
    private static Paths64 Encode(TimberPanel panel) => TimberContours.Encode(panel.RingsInches.Select(r => r.ToArray()).ToArray(), 0, 0, 1, true);
    private static ImmutableArray<ImmutableArray<TimberPoint2>> Decode(Paths64 paths) => TimberContours.Decode(paths).Select(r => r.ToImmutableArray()).ToImmutableArray();
}
