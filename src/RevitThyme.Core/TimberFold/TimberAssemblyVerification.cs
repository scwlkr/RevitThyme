using System.Collections.Immutable;
using System.Globalization;
using Q = RevitThyme.Core.TimberPrismRational;
using Point = RevitThyme.Core.TimberPrismPoint;

namespace RevitThyme.Core;

public sealed record TimberPanelCollision(string FirstPanelId, string SecondPanelId, double VolumeCubicInches,
    string VolumeNumerator, string VolumeDenominator);
public sealed record TimberAssemblyIssue(string? FirstPanelId, string? SecondPanelId, string Code, string Message);
public sealed record TimberAssemblyVerification(ImmutableArray<TimberPanelCollision> Collisions,
    ImmutableArray<TimberAssemblyIssue> Unresolved, int PanelPairsChecked, long CandidateCellPairs)
{
    public bool Passed => Collisions.IsEmpty && Unresolved.IsEmpty;
}

/// <summary>Verifies the exact affine binary geometry supplied, independently of the contour kernel.
/// Positive stock depth points inward along -OutwardNormal. Extraction/chord/grid errors remain separate.
/// Cancellation or arithmetic/work limits produce unresolved results, never a zero-volume assumption.</summary>
public static class TimberAssemblyVerifier
{
    public static TimberAssemblyVerification Verify(IEnumerable<TimberPanel> panels, CancellationToken cancellationToken = default)
    {
        var collisions = ImmutableArray.CreateBuilder<TimberPanelCollision>();
        var issues = ImmutableArray.CreateBuilder<TimberAssemblyIssue>();
        var budget = new TimberPrismBudget(cancellationToken);
        var input = panels.Take(257).ToArray();
        if (input.Length == 0)
        {
            issues.Add(new(null, null, "empty_inventory", "Assembly verification requires at least one fabrication panel."));
            return new(collisions.ToImmutable(), issues.ToImmutable(), 0, 0);
        }
        if (input.Length > 256)
        {
            issues.Add(new(null, null, "panel_limit", "Exact assembly verification supports at most 256 panels per run."));
            return new(collisions.ToImmutable(), issues.ToImmutable(), 0, 0);
        }
        if (input.Any(p => string.IsNullOrWhiteSpace(p.Id)) || input.Select(p => p.Id).Distinct(StringComparer.Ordinal).Count() != input.Length)
        {
            issues.Add(new(null, null, "invalid_panel_identity", "Every assembly panel needs a unique nonempty identity."));
            return new(collisions.ToImmutable(), issues.ToImmutable(), 0, 0);
        }
        var captured = input.OrderBy(p => p.Id, StringComparer.Ordinal).Select(p => Capture(p, budget)).ToArray();
        if (input.Length < 2)
        {
            try { budget.Check(); }
            catch (TimberPrismUnresolved error) { issues.Add(new(null, null, error.Code, error.Message)); }
        }
        int pairs = 0;
        for (int i = 0; i < captured.Length; i++)
        for (int j = i + 1; j < captured.Length; j++)
        {
            var a = captured[i]; var b = captured[j]; pairs++;
            try
            {
                budget.Check();
                if (a.Error is { } ae) throw ae;
                if (b.Error is { } be) throw be;
                if (!a.Bounds!.Overlaps(b.Bounds!)) continue;
                Q volume = 0;
                foreach (var first in a.Cells)
                foreach (var second in b.Cells)
                {
                    budget.Check();
                    if (!first.Bounds.Overlaps(second.Bounds)) continue;
                    budget.Candidate();
                    volume += TimberPrismClipping.Intersection(first, second, budget);
                }
                if (volume > new Q(1, 1000000)) collisions.Add(new(a.Id, b.Id, volume.Approximate(),
                    volume.Numerator.ToString(CultureInfo.InvariantCulture), volume.Denominator.ToString(CultureInfo.InvariantCulture)));
            }
            catch (TimberPrismUnresolved error) { issues.Add(new(a.Id, b.Id, error.Code, error.Message)); }
        }
        foreach (var panel in captured.Where(p => p.Error is not null))
            if (!issues.Any(i => i.FirstPanelId == panel.Id || i.SecondPanelId == panel.Id))
                issues.Add(new(panel.Id, null, panel.Error!.Code, panel.Error.Message));
        return new(collisions.ToImmutable(), issues.ToImmutable(), pairs, budget.Candidates);
    }

    private sealed record Captured(string Id, TimberPrismCell[] Cells, TimberPrismBounds? Bounds, TimberPrismUnresolved? Error);
    private static Captured Capture(TimberPanel panel, TimberPrismBudget budget)
    {
        try
        {
            budget.Check();
            if (!double.IsFinite(panel.ThicknessInches) || panel.ThicknessInches <= 0 || panel.Triangles.IsDefaultOrEmpty || panel.Triangles.Length > 4096)
                throw new TimberPrismUnresolved("invalid_panel", "Each prism panel needs positive thickness and between 1 and 4096 triangles.");
            TimberPrismPartition.Validate(panel, budget);
            var origin = Point.From(panel.Frame.OriginInches); var u = Point.From(panel.Frame.U);
            var v = Point.From(panel.Frame.V); var normal = Point.From(panel.Frame.OutwardNormal);
            if (u.Cross(v).Dot(normal) <= 0) throw new TimberPrismUnresolved("invalid_frame", "Panel frame must define a nonsingular right-handed affine prism.");
            var depth = normal * (0 - Q.From(panel.ThicknessInches));
            var vertices = new Dictionary<TimberPoint2, Point>();
            Point World(TimberPoint2 p)
            {
                if (!vertices.TryGetValue(p, out var world))
                    vertices.Add(p, world = origin + u * Q.From(p.X) + v * Q.From(p.Y));
                return world;
            }
            var cells = panel.Triangles.Select(t =>
            {
                budget.Check();
                return new TimberPrismCell(World(t.A), World(t.B), World(t.C), depth);
            }).ToArray();
            return new(panel.Id, cells, new(cells.SelectMany(c => c.Faces.SelectMany(f => f))), null);
        }
        catch (TimberPrismUnresolved error) { return new(panel.Id, [], null, error); }
    }
}
