using Clipper2Lib;
using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using Q = RevitThyme.Core.TimberPrismRational;
using Point = RevitThyme.Core.TimberPrismPoint;

namespace RevitThyme.Core;

internal static class TimberRoofExposure
{
    internal static ImmutableArray<TimberPanel> Apply(TimberPanelDraft draft, TimberSnapshot snapshot, TimberFoldProfile profile,
        List<TimberRoofExposureRecord> records, List<TimberDiagnostic> diagnostics, TimberBoundaryBudget boundaryBudget, CancellationToken ct)
    {
        var roofs = draft.Panels.Where(p => p.Kind == TimberFaceKind.Roof).OrderBy(p => p.Id, StringComparer.Ordinal).ToArray();
        var output = draft.Panels.ToDictionary(p => p.Id); var budget = new TimberPrismBudget(ct);
        foreach (var receiver in roofs)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var frame = new TimberRoofExposureFrame(receiver); var cuts = new Paths64();
                var owners = new List<string>(); Q enclosureExcess = 0;
                var before = Encode(receiver);
                foreach (var owner in roofs.Where(p => p.Id != receiver.Id))
                {
                    ct.ThrowIfCancellationRequested(); var ownerCuts = new Paths64();
                    var origin = Point.From(owner.Frame.OriginInches); var u = Point.From(owner.Frame.U); var v = Point.From(owner.Frame.V);
                    Point Project(TimberPoint2 p) => frame.Project(origin + u * Q.From(p.X) + v * Q.From(p.Y));
                    foreach (var triangle in owner.Triangles)
                    {
                        budget.Check();
                        var higher = TimberRoofExposureFrame.Higher([Project(triangle.A), Project(triangle.B), Project(triangle.C)],
                            string.CompareOrdinal(owner.Id, receiver.Id) < 0, budget);
                        var exact = TimberJointHull.Exact(higher, budget);
                        if (exact.Length < 3 || TimberJointHull.Area(exact) == 0) continue;
                        var hull = TimberJointHull.Grid(exact, budget);
                        ownerCuts.Add(hull);
                        var gridArea = new Q(BigInteger.CreateChecked(TimberContours.TwiceArea(hull)), 2 * BigInteger.Pow(10, 16));
                        var excess = gridArea - TimberJointHull.Area(exact);
                        if (excess < 0) throw new TimberPrismUnresolved("roof_exposure_enclosure", "Exposure grid enclosure lost exact projected area.");
                        enclosureExcess += excess;
                    }
                    if (ownerCuts.Count == 0 || Clipper.Intersect(before, ownerCuts, FillRule.NonZero).Count == 0) continue;
                    cuts.AddRange(ownerCuts); owners.Add(owner.Id);
                }
                budget.Check();
                var partition = TimberGridBoolean.Difference(before, Clipper.Union(cuts, FillRule.NonZero), budget);
                var after = partition.Output;
                var pieces = TimberContours.Pieces(after);
                if (pieces.Length != 1) throw new TimberPrismUnresolved("roof_exposure_topology", "Exposure would erase or split a source roof panel; explicit coverage/splitting policy is required.");
                after = pieces[0];
                var removed = partition.Changed;
                var delta = TimberContours.TwiceArea(partition.OriginalRefined) - TimberContours.TwiceArea(after);
                var measured = TimberContours.TwiceArea(removed);
                boundaryBudget.Charge(receiver.Id, partition.Refinement);
                var face = snapshot.Faces.Single(f => f.Id == receiver.FaceId);
                TimberPanelWebs.Validate(after, profile.MinimumBarInches, face, diagnostics, ct);
                output[receiver.Id] = receiver with { RingsInches = Decode(after), Triangles = TimberTriangulation.Create(after).ToImmutableArray(), AreaSquareInches = TimberContours.Area(after) };
                records.Add(new(receiver.Id, receiver.SourceId, receiver.FaceId, owners.ToImmutableArray(), receiver.AreaSquareInches,
                    TimberContours.Area(after), Decode(removed), TimberContours.Area(removed), measured.ToString(CultureInfo.InvariantCulture),
                    (delta - measured).ToString(CultureInfo.InvariantCulture), UpperDouble(enclosureExcess)) { Refinement = partition.Refinement });
            }
            catch (TimberPrismUnresolved error)
            {
                ct.ThrowIfCancellationRequested();
                diagnostics.Add(new(error.Code, TimberSeverity.Error, error.Message, receiver.SourceId, receiver.Id));
            }
            catch (Exception error) when (error is InvalidOperationException or ClipperLibException or OverflowException)
            { diagnostics.Add(new("roof_exposure_geometry", TimberSeverity.Error, error.Message, receiver.SourceId, receiver.Id)); }
        }
        return output.Values.OrderBy(p => p.Id, StringComparer.Ordinal).ToImmutableArray();
    }
    private static Paths64 Encode(TimberPanel panel) => TimberContours.Encode(panel.RingsInches.Select(r => r.ToArray()).ToArray(), 0, 0, 1, true);
    private static ImmutableArray<ImmutableArray<TimberPoint2>> Decode(Paths64 paths) => TimberContours.Decode(paths).Select(r => r.ToImmutableArray()).ToImmutableArray();
    private static double UpperDouble(Q area)
    {
        double value = area.Approximate();
        while (Q.From(value) < area) value = Math.BitIncrement(value);
        return value;
    }
}
