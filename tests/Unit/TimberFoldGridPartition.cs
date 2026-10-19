using System.Text.Json;
using RevitThyme.Core;

namespace RevitThyme.Unit;

internal static class TimberFoldGridPartition
{
    internal static void Run(Action<string, bool> check)
    {
        // All three sloping vertices are collinear on the 1e-8-inch integer grid.
        // Their decoded binary values are not collinear, so every boundary vertex matters.
        var snapshot = Source();
        var result = TimberFabrication.Fit(snapshot, new(ScaleDenominator: 12), new("phase", []));
        var panel = result.Draft.Panels.Single(p => p.FaceId == "W1");
        check("public fitting retains grid-collinear boundary vertices in an exact binary triangle partition",
            result.State == TimberFitState.GeometryFitted && result.Verification.Passed &&
            panel.RingsInches[0].Length == 4 && panel.RingsInches[0].Contains(new(2.2, 1.3)) &&
            panel.Triangles.Length == 2 && panel.Triangles.Any(t => t.A == new TimberPoint2(2.2, 1.3) ||
                t.B == new TimberPoint2(2.2, 1.3) || t.C == new TimberPoint2(2.2, 1.3)));
        var reversed = snapshot with { Faces = [snapshot.Faces[0] with { RingsFeet = [snapshot.Faces[0].RingsFeet[0].Reverse().ToArray()] }, snapshot.Faces[1]] };
        check("restored binary boundary partitions are deterministic across source contour orientation",
            JsonSerializer.Serialize(result, Protocol.Json) == JsonSerializer.Serialize(
                TimberFabrication.Fit(reversed, new(ScaleDenominator: 12), new("phase", [])), Protocol.Json));
        var booleanSource = BooleanSource(); var before = JsonSerializer.Serialize(booleanSource, Protocol.Json);
        var partitioned = TimberFabrication.Fit(booleanSource, new(ScaleDenominator: 12), new("phase", []));
        check("public fitting qualifies bounded rounded boundary intersections without losing exact material accounting",
            partitioned.State == TimberFitState.GeometryFitted && partitioned.Verification.Passed &&
            JsonSerializer.Serialize(booleanSource, Protocol.Json) == before);
        check("rounded source boundary changes retain signed exact area provenance",
            partitioned.Joints.Select(j => j.Refinement).Concat(partitioned.RoofExposure.Select(r => r.Refinement)).Any(r =>
                r is { SignedTwiceGridAdjustment: not "0", MaximumDisplacementGridUnits: 1 } proof &&
                Int128.Parse(proof.OriginalTwiceGridArea) + Int128.Parse(proof.SignedTwiceGridAdjustment) == Int128.Parse(proof.RefinedTwiceGridArea) &&
                Int128.Parse(proof.RefinedTwiceGridArea) == Int128.Parse(proof.OutputTwiceGridArea) + Int128.Parse(proof.ChangedTwiceGridArea) &&
                proof.Points.Length > 0));
    }

    private static TimberSnapshot Source() => new(new(1, "start", "session", "doc", "view", 0), "source", "phase", [],
        [new("1", "source-1", TimberCategory.Wall, TimberDisposition.Included, "Wall", ["W1"], [], "s1"),
         new("2", "source-2", TimberCategory.Wall, TimberDisposition.Included, "Wall", ["W2"], [], "s2")],
        [new("W1", "1", TimberFaceKind.Wall, new(0, 0, 0), new(1, 0, 0), new(0, 0, 1), new(0, -1, 0),
            [[new(0, 0), new(2.3, 1.4), new(2.2, 1.3), new(2.1, 1.2)]]),
         new("W2", "2", TimberFaceKind.Wall, new(0, 100, 0), new(1, 0, 0), new(0, 0, 1), new(0, -1, 0),
            [[new(0, 0), new(2, 0), new(2, 2), new(0, 2)]])], [], []);

    private static TimberSnapshot BooleanSource() => new(new(1, "start", "session", "doc", "view", 0), "source", "phase", [],
        [new("1", "source-1", TimberCategory.Roof, TimberDisposition.Included, "Roof", ["R2"], [], "s1"),
         new("3", "source-3", TimberCategory.Roof, TimberDisposition.Included, "Roof", ["A1"], [], "s3")],
        [new("R2", "1", TimberFaceKind.Roof, new(0, 0, 0), new(1, 0, 0), new(0, 1, 0), new(0, 0, 1),
            [[new(0, 0), new(7, 0), new(2, 5)]]),
         new("A1", "3", TimberFaceKind.Roof, new(-1, 1.00000003, 0), new(1, 0, 0), new(0, .6, .8), new(0, -.8, .6),
            [[new(0, 0), new(9, 0), new(9, 10), new(0, 10)]])], [], []);
}
