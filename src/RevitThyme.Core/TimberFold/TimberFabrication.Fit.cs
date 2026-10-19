using Clipper2Lib;
using System.Collections.Immutable;
using System.Globalization;

namespace RevitThyme.Core;

public static partial class TimberFabrication
{
    public static TimberFittedDraft Fit(TimberSnapshot snapshot, TimberFoldProfile profile, TimberScope scope,
        CancellationToken cancellationToken = default)
    {
        var draft = Draft(snapshot, profile, scope, cancellationToken);
        var diagnostics = draft.Diagnostics.ToList(); var joints = new List<TimberJointRecord>();
        var exposure = new List<TimberRoofExposureRecord>();
        var wallEnvelope = new List<TimberWallEnvelopeRecord>();
        var fitted = new List<TimberPanel>();
        if (draft.State == TimberDraftState.Blocked) return Result();
        if (profile.JointClearanceInches <= 0)
        { diagnostics.Add(new("joint_clearance", TimberSeverity.Error, "Joint fitting requires positive physical clearance.")); return Result(); }
        var budget = new TimberPrismBudget(cancellationToken);
        var boundaryBudget = new TimberBoundaryBudget();
        var exposed = TimberRoofExposure.Apply(draft, snapshot, profile, exposure, diagnostics, boundaryBudget, cancellationToken);
        Dictionary<string, TimberPanel>? grownWalls = null;
        foreach (var source in exposed.OrderBy(p => p.Kind == TimberFaceKind.Roof ? 0 : 1).ThenBy(p => p.Id, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var receiver = source;
            try
            {
                if (source.Kind == TimberFaceKind.Wall)
                {
                    // Roof ownership is finalized before its finite material can cap wall growth.
                    // Original wall contours remain the evidence for upper-storey continuation.
                    grownWalls ??= TimberRoofExposureWalls.Apply(fitted.Concat(exposed.Where(p => p.Kind == TimberFaceKind.Wall)).ToImmutableArray(),
                        snapshot, profile, wallEnvelope, diagnostics, boundaryBudget, cancellationToken).Where(p => p.Kind == TimberFaceKind.Wall).ToDictionary(p => p.Id);
                    receiver = grownWalls[source.Id];
                }
                foreach (var owner in fitted)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var before = Encode(receiver);
                    var shadow = TimberJointShadow.Create(owner, receiver, before, profile.JointClearanceInches, budget);
                    var partition = TimberGridBoolean.Difference(before, shadow.Cuts, budget);
                    var after = partition.Output; var removed = partition.Changed;
                    if (removed.Count == 0) continue;
                    boundaryBudget.Charge(receiver.Id, partition.Refinement);
                    var pieces = TimberContours.Pieces(after);
                    if (pieces.Length != 1) throw new TimberPrismUnresolved("joint_topology_loss",
                        $"Joint {owner.Id} → {receiver.Id} would erase or split its receiving panel.");
                    var piece = pieces[0];
                    if (TimberContours.Area(piece) <= .0000000001) throw new TimberPrismUnresolved("joint_sliver", "Joint leaves an unresolved panel sliver.");
                    var face = snapshot.Faces.Single(f => f.Id == receiver.FaceId);
                    if (!TimberJointOpenings.PreserveWindows(snapshot, profile, receiver, removed, piece, diagnostics, cancellationToken))
                        throw new TimberPrismUnresolved("joint_opening_preservation", $"Joint {owner.Id} → {receiver.Id} cannot preserve its window architecture.");
                    TimberPanelWebs.Validate(piece, profile.MinimumBarInches, face, diagnostics, cancellationToken);
                    receiver = receiver with { RingsInches = Decode(piece), Triangles = TimberTriangulation.Create(piece).ToImmutableArray(),
                        AreaSquareInches = TimberContours.Area(piece) };
                    var canonicalRemoved = new Paths64(TimberContours.Pieces(removed).SelectMany(p => p));
                    joints.Add(new(owner.Id, receiver.Id, Decode(canonicalRemoved), TimberContours.Area(removed),
                        TimberContours.TwiceArea(removed).ToString(CultureInfo.InvariantCulture), shadow.ClearanceU, shadow.ClearanceV,
                        shadow.ClearanceDepth, shadow.GridExcessAreaSum, shadow.AxisBoxExpansionAreaSum) { Refinement = partition.Refinement });
                }
            }
            catch (TimberPrismUnresolved ex)
            {
                cancellationToken.ThrowIfCancellationRequested();
                diagnostics.Add(new(ex.Code, TimberSeverity.Error, ex.Message, receiver.SourceId, receiver.Id));
            }
            catch (Exception ex) when (ex is InvalidOperationException or ClipperLibException or OverflowException)
            { diagnostics.Add(new("joint_geometry", TimberSeverity.Error, ex.Message, receiver.SourceId, receiver.Id)); }
            fitted.Add(receiver);
        }
        return Result();

        TimberFittedDraft Result()
        {
            var output = fitted.Count == 0 ? draft.Panels : fitted.OrderBy(p => p.Id, StringComparer.Ordinal).ToImmutableArray();
            var verification = TimberAssemblyVerifier.Verify(output, cancellationToken);
            var openingMap = draft.State == TimberDraftState.Blocked ? draft.OpeningMap :
                TimberJointOpenings.Remap(snapshot, profile, output, diagnostics, cancellationToken);
            foreach (var issue in verification.Unresolved) diagnostics.Add(new(issue.Code, TimberSeverity.Error, issue.Message, GeometryId: issue.FirstPanelId));
            foreach (var collision in verification.Collisions) diagnostics.Add(new("joint_collision", TimberSeverity.Error,
                $"Residual material collision: {collision.FirstPanelId} / {collision.SecondPanelId}.", GeometryId: collision.FirstPanelId));
            bool passed = diagnostics.All(d => d.Severity != TimberSeverity.Error) && verification.Passed;
            var resultDraft = draft with { State = passed ? TimberDraftState.ContoursValidated : TimberDraftState.Blocked,
                Panels = output, OpeningMap = openingMap, Diagnostics = diagnostics.ToImmutableArray() };
            return new(passed ? TimberFitState.GeometryFitted : TimberFitState.Blocked, resultDraft, joints.ToImmutableArray(),
                verification, diagnostics.ToImmutableArray()) { RoofExposure = exposure.ToImmutableArray(), WallEnvelope = wallEnvelope.ToImmutableArray() };
        }
    }
    private static Paths64 Encode(TimberPanel panel) => TimberContours.Encode(panel.RingsInches.Select(r => r.ToArray()).ToArray(), 0, 0, 1, true);
    private static ImmutableArray<ImmutableArray<TimberPoint2>> Decode(Paths64 paths) =>
        TimberContours.Decode(paths).Select(r => r.ToImmutableArray()).ToImmutableArray();
}
