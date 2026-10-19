using Clipper2Lib;
using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using Q = RevitThyme.Core.TimberPrismRational;

namespace RevitThyme.Core;

internal sealed record TimberGridPartition(Paths64 OriginalRefined, Paths64 Output, Paths64 Changed, TimberBoundaryRefinement Refinement);

// Rounded Boolean intersections explicitly refine the original boundary. Exact partition
// and area proofs apply after that recorded change; the source contour is never mutated.
internal static class TimberGridBoolean
{
    internal static TimberGridPartition Difference(Paths64 original, Paths64 cuts, TimberPrismBudget budget)
    {
        var output = Clipper.Difference(original, cuts, FillRule.NonZero);
        return Qualify(original, output, Clipper.Difference(original, output, FillRule.NonZero), false, budget);
    }

    internal static TimberGridPartition Union(Paths64 original, Paths64 addition, TimberPrismBudget budget)
    {
        var output = Clipper.Union(original, addition, FillRule.NonZero);
        return Qualify(original, output, Clipper.Difference(output, original, FillRule.NonZero), true, budget);
    }

    private static TimberGridPartition Qualify(Paths64 original, Paths64 output, Paths64 changed, bool union, TimberPrismBudget budget)
    {
        budget.Check();
        if (original.Count == 0) Fail("Boundary refinement requires a nonempty original panel.");
        foreach (var path in original.Concat(output).Concat(changed))
        {
            budget.Check(path.Count);
            if (path.Count < 3 || TimberContours.TwiceArea(path) == 0) Fail("Boolean refinement produced a collapsed contour.");
            TimberContourTopology.Simple(path);
        }
        TimberContourTopology.OuterAndHoles(original);
        var originalArea = TimberContours.TwiceArea(original);
        var outputArea = TimberContours.TwiceArea(output); var changedArea = TimberContours.TwiceArea(changed);
        if ((union ? originalArea + changedArea == outputArea : originalArea == outputArea + changedArea) &&
            TimberGridBoundaryProof.Matches(original, output, changed, union, budget))
            return Result(new Paths64(original.Select(p => new Path64(p))), []);
        var originals = original.SelectMany(p => p).ToHashSet();
        // Signed cancellation removes internal cut edges. For growth the old source
        // boundary can be inside Output, so looking only at Output's outside is wrong.
        var points = TimberGridBoundaryProof.Nodes(output, changed, union, budget).Where(p => !originals.Contains(p))
            .OrderBy(p => p.X).ThenBy(p => p.Y).ToArray();
        var nodes = new Dictionary<(int Ring, int Edge), List<(Point64 Point, Int128 Projection)>>();
        var evidence = ImmutableArray.CreateBuilder<TimberBoundaryRefinementPoint>();
        foreach (var point in points)
        {
            budget.Check();
            var matches = new List<(int Ring, int Edge, Int128 Projection)>();
            for (int r = 0; r < original.Count; r++)
            for (int e = 0; e < original[r].Count; e++)
            {
                budget.Check();
                if (Near(point, original[r][e], original[r][(e + 1) % original[r].Count], out var projection)) matches.Add((r, e, projection));
            }
            if (matches.Count == 0) continue; // Interior cut vertices do not refine source edges.
            if (matches.Count != 1) Fail("A rounded intersection has ambiguous original-edge ownership.");
            var match = matches[0]; var a = original[match.Ring][match.Edge]; var b = original[match.Ring][(match.Edge + 1) % original[match.Ring].Count];
            if (!nodes.TryGetValue((match.Ring, match.Edge), out var edge)) nodes.Add((match.Ring, match.Edge), edge = []);
            edge.Add((point, match.Projection));
            evidence.Add(new(new(a.X, a.Y), new(b.X, b.Y), new(point.X, point.Y)));
        }
        var refined = new Paths64();
        for (int r = 0; r < original.Count; r++)
        {
            var path = new Path64();
            for (int e = 0; e < original[r].Count; e++)
            {
                path.Add(original[r][e]);
                if (!nodes.TryGetValue((r, e), out var edge)) continue;
                var ordered = edge.OrderBy(p => p.Projection).ToArray();
                if (ordered.Select(p => p.Projection).Distinct().Count() != ordered.Length) Fail("Refined source-edge projections are not strictly ordered.");
                path.AddRange(ordered.Select(p => p.Point));
            }
            if ((TimberContours.TwiceArea(path) > 0) != (TimberContours.TwiceArea(original[r]) > 0)) Fail("Boundary refinement changed contour orientation.");
            TimberContourTopology.Simple(path); refined.Add(path);
        }
        TimberContourTopology.OuterAndHoles(refined);
        var refinedArea = TimberContours.TwiceArea(refined);
        if (union ? refinedArea + changedArea != outputArea : refinedArea != outputArea + changedArea)
            Fail("Recorded boundary refinement does not conserve exact grid material area.");
        TimberGridBoundaryProof.Require(refined, output, changed, union, budget);
        return Result(refined, evidence.ToImmutable());

        TimberGridPartition Result(Paths64 source, ImmutableArray<TimberBoundaryRefinementPoint> proof)
        {
            var area = TimberContours.TwiceArea(source);
            string Text(Int128 value) => value.ToString(CultureInfo.InvariantCulture);
            return new(source, output, changed, new(union ? "Union" : "Difference", Text(originalArea), Text(area - originalArea),
                Text(area), Text(outputArea), Text(changedArea), proof.Length == 0 ? 0 : 1, proof));
        }
    }

    private static bool Near(Point64 p, Point64 a, Point64 b, out Int128 projection)
    {
        Int128 dx = (Int128)b.X - a.X, dy = (Int128)b.Y - a.Y;
        Int128 x = (Int128)p.X - a.X, y = (Int128)p.Y - a.Y, length = dx * dx + dy * dy;
        projection = x * dx + y * dy;
        if (projection <= 0 || projection >= length) return false;
        var cross = BigInteger.CreateChecked(dx * y - dy * x);
        return cross * cross <= BigInteger.CreateChecked(length) && PhysicalNear(p, a, b);
    }

    private static bool PhysicalNear(Point64 p, Point64 a, Point64 b)
    {
        // Also prove the bound for the actual binary coordinates delivered to consumers,
        // rather than assuming decimal-grid division preserves distances exactly.
        var dx = Q.From(b.X / TimberContours.Grid) - Q.From(a.X / TimberContours.Grid);
        var dy = Q.From(b.Y / TimberContours.Grid) - Q.From(a.Y / TimberContours.Grid);
        var x = Q.From(p.X / TimberContours.Grid) - Q.From(a.X / TimberContours.Grid);
        var y = Q.From(p.Y / TimberContours.Grid) - Q.From(a.Y / TimberContours.Grid);
        var length = dx * dx + dy * dy; var projection = x * dx + y * dy;
        var cross = dx * y - dy * x; Q bound = new(1, 100000000);
        return projection > 0 && projection < length && cross * cross <= length * bound * bound;
    }

    private static void Fail(string message) => throw new TimberPrismUnresolved("grid_refinement", message);
}
