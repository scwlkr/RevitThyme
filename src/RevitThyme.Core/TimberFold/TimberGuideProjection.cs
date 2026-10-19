using System.Collections.Immutable;

namespace RevitThyme.Core;

internal static class TimberGuideProjection
{
    internal static TimberGuideVector Create(ImmutableArray<TimberGuidePart> parts,
        ImmutableArray<TimberJointRecord> joints, CancellationToken ct)
    {
        const string file = "ASSEMBLY-REFERENCE.dxf";
        var contours = ImmutableArray.CreateBuilder<ImmutableArray<TimberPoint2>>();
        var labels = ImmutableArray.CreateBuilder<TimberGuideLabel>();
        // Orthonormal camera: right=(1,-1,0)/sqrt(2), up=(-1,-1,2)/sqrt(6).
        // No hidden-line removal: every actual outer/hole ring, front and back, is retained.
        var frontPoints = parts.SelectMany(p => p.Panel.RingsInches.SelectMany(r => r)
            .Select(v => World(p.Panel, v, 0, 0))).ToArray();
        var projections = frontPoints.Select(Project).ToArray();
        double minX = projections.Min(p => p.X), maxX = projections.Max(p => p.X);
        double minY = projections.Min(p => p.Y), maxY = projections.Max(p => p.Y);
        double spread = Math.Max(1, Math.Max(maxX - minX, maxY - minY));
        double explode = spread * 0.2;
        double assembledMax = double.NegativeInfinity;
        var exploded = new List<(TimberGuidePart Part, ImmutableArray<ImmutableArray<TimberPoint2>> Rings, TimberPoint2 Center)>();
        foreach (var part in parts)
        {
            ct.ThrowIfCancellationRequested();
            var panel = part.Panel;
            AddPanel(panel, 0, contours);
            var points = contours.SelectMany(r => r);
            assembledMax = Math.Max(assembledMax, points.Max(p => p.X));
            var center = TimberGuideInventory.MaterialPoint(panel);
            labels.Add(TimberGuideInventory.Label(file, part, Project(World(panel, center, 0, 0)), part.Alias));
            var rings = ImmutableArray.CreateBuilder<ImmutableArray<TimberPoint2>>();
            double offset = explode * (exploded.Count + 1);
            AddPanel(panel, offset, rings);
            exploded.Add((part, rings.ToImmutable(), Project(World(panel, center, 0, offset))));
        }
        double explodedMin = exploded.SelectMany(p => p.Rings.SelectMany(r => r)).Min(p => p.X);
        double shift = assembledMax - explodedMin + spread * 0.5 + 1;
        foreach (var (part, rings, center) in exploded)
        {
            foreach (var ring in rings) contours.Add(ring.Select(p => new TimberPoint2(p.X + shift, p.Y)).ToImmutableArray());
            labels.Add(TimberGuideInventory.Label(file, part, new(center.X + shift, center.Y), part.Alias));
        }
        double top = contours.SelectMany(r => r).Max(p => p.Y) + 0.5;
        labels.Add(new(file, null, null, null, new(minX, top), "Assembled — actual panel frames and thickness"));
        labels.Add(new(file, null, null, null, new(explodedMin + shift, top), "Exploded — display offsets along outward normals"));
        double y = contours.SelectMany(r => r).Min(p => p.Y) - 0.5;
        Note("Orientation: +X lower-right; +Y lower-left; +Z up. Depth is inward (-normal).");
        Note("All front/back and opening outlines visible; crossings are reference projections, not new joints.");
        foreach (var part in parts) Note(TimberGuideInventory.PartLegend(part));
        int step = 1;
        foreach (var joint in joints.OrderBy(j => j.OwnerPanelId, StringComparer.Ordinal).ThenBy(j => j.ReceiverPanelId, StringComparer.Ordinal))
        {
            var owner = parts.Single(p => p.Panel.Id == joint.OwnerPanelId);
            var receiver = parts.Single(p => p.Panel.Id == joint.ReceiverPanelId);
            Note(FormattableString.Invariant($"Joint {step++}: {owner.Alias} owns contact; {receiver.Alias} receives trim ({joint.RemovedAreaSquareInches:G8} in²).\nOwner source: {owner.Panel.SourceId} | Receiver source: {receiver.Panel.SourceId}"));
        }
        if (joints.IsEmpty) Note("No fitted joints recorded; no assembly connection is implied.");
        else Note("Joint list order identifies recorded ownership; physical assembly sequence remains untested.");
        Note("Reference only. Physical checks outstanding; not manufacturing ready.\nNominal contours; apply kerf compensation in laser software.");
        return TimberGuideInventory.Vector(file, "Assembly", "Assembly and exploded reference", contours.ToImmutable(), labels.ToImmutable(),
            parts.Select(p => p.Panel.Id).ToImmutableArray());

        void Note(string text)
        { labels.Add(new(file, null, null, null, new(minX, y), text)); y -= (text.Split('\n').Length + 1) * 0.2; }
    }

    private static void AddPanel(TimberPanel panel, double outwardOffset,
        ImmutableArray<ImmutableArray<TimberPoint2>>.Builder contours)
    {
        foreach (var ring in panel.RingsInches)
        {
            contours.Add(ring.Select(v => Project(World(panel, v, 0, outwardOffset))).ToImmutableArray());
            contours.Add(ring.Select(v => Project(World(panel, v, panel.ThicknessInches, outwardOffset))).ToImmutableArray());
            // Closed quadrilateral side faces show actual material depth, including hole walls.
            for (int i = 0; i < ring.Length; i++)
            {
                var a = ring[i]; var b = ring[(i + 1) % ring.Length];
                contours.Add([Project(World(panel, a, 0, outwardOffset)), Project(World(panel, b, 0, outwardOffset)),
                    Project(World(panel, b, panel.ThicknessInches, outwardOffset)), Project(World(panel, a, panel.ThicknessInches, outwardOffset))]);
            }
        }
    }
    private static TimberPoint3 World(TimberPanel p, TimberPoint2 v, double depth, double offset)
    {
        var f = p.Frame;
        return new(f.OriginInches.X + f.U.X * v.X + f.V.X * v.Y + f.OutwardNormal.X * (offset - depth),
            f.OriginInches.Y + f.U.Y * v.X + f.V.Y * v.Y + f.OutwardNormal.Y * (offset - depth),
            f.OriginInches.Z + f.U.Z * v.X + f.V.Z * v.Y + f.OutwardNormal.Z * (offset - depth));
    }
    private static TimberPoint2 Project(TimberPoint3 p) => new((p.X - p.Y) / Math.Sqrt(2), (-p.X - p.Y + 2 * p.Z) / Math.Sqrt(6));
}
