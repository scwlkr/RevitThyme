using System.Collections.Immutable;

namespace RevitThyme.Core;

internal sealed record TimberGuidePart(string Alias, TimberPanel Panel, TimberStockPlacement Placement);
internal sealed record TimberGuideVector(TimberGuideDrawing Drawing,
    ImmutableArray<ImmutableArray<TimberPoint2>> Contours, ImmutableArray<TimberGuideLabel> Labels,
    ImmutableArray<ImmutableArray<TimberPoint2>> GeometryContours, ImmutableArray<TimberGuideLabel> IdentificationAnchors);
internal sealed record TimberGuideInventory(ImmutableArray<TimberGuidePart> Parts,
    ImmutableArray<TimberGuideVector> Vectors, ImmutableArray<TimberJointRecord> Joints)
{
    internal static TimberGuideInventory Create(TimberFittedDraft fitted, TimberStockPlan stock,
        TimberFoldProfile profile, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var draft = fitted.Draft;
        var panels = draft.Panels;
        if (draft.Diagnostics.IsDefault || fitted.Diagnostics.IsDefault || fitted.Joints.IsDefault || fitted.RoofExposure.IsDefault ||
            fitted.WallEnvelope.IsDefault || draft.FaceMap.IsDefaultOrEmpty ||
            draft.OpeningMap.IsDefault || stock.Placements.IsDefault || stock.Diagnostics.IsDefault ||
            fitted.Verification.Collisions.IsDefault || fitted.Verification.Unresolved.IsDefault ||
            draft.FaceMap.Any(m => m.PanelIds.IsDefaultOrEmpty))
            throw new InvalidDataException("Guide inventory and source maps must be explicitly initialized and complete.");
        if (fitted.State != TimberFitState.GeometryFitted || draft.State != TimberDraftState.ContoursValidated ||
            fitted.Diagnostics.Concat(draft.Diagnostics).Any(d => d.Severity == TimberSeverity.Error) ||
            !fitted.Verification.Passed || profile.Validate().Length != 0 || !stock.Planned || panels.IsDefaultOrEmpty)
            throw new InvalidDataException("Guides require a complete fitted, verified draft and valid stock/profile.");
        if (panels.Any(p => string.IsNullOrWhiteSpace(p.Id) || string.IsNullOrWhiteSpace(p.SourceId) || string.IsNullOrWhiteSpace(p.FaceId) ||
            p.ThicknessInches != profile.ThicknessInches) || panels.Select(p => p.Id).Distinct(StringComparer.Ordinal).Count() != panels.Length)
            throw new InvalidDataException("Guide panels need unique identities, source/face mapping and the selected thickness.");
        var mapped = draft.FaceMap.SelectMany(m => m.PanelIds.Select(id => (m.SourceId, m.FaceId, Id: id))).ToArray();
        if (draft.FaceMap.IsDefaultOrEmpty || draft.FaceMap.Any(m => m.PanelIds.IsDefaultOrEmpty) || mapped.Length != panels.Length ||
            mapped.Select(m => m.Id).Distinct(StringComparer.Ordinal).Count() != panels.Length ||
            panels.Any(p => !mapped.Contains((p.SourceId, p.FaceId, p.Id))))
            throw new InvalidDataException("Every panel must occur once in the complete matching source/face map.");
        foreach (var opening in draft.OpeningMap)
            if (string.IsNullOrWhiteSpace(opening.SourceId) || string.IsNullOrWhiteSpace(opening.OpeningId) || opening.PanelIds.IsDefaultOrEmpty ||
                opening.PanelIds.Distinct(StringComparer.Ordinal).Count() != opening.PanelIds.Length ||
                opening.PanelIds.Any(id => !panels.Any(p => p.Id == id && p.FaceId == opening.HostFaceId)))
                throw new InvalidDataException("Opening maps must identify existing panels on their host face.");
        if (fitted.Joints.Any(j => j.OwnerPanelId == j.ReceiverPanelId || !panels.Any(p => p.Id == j.OwnerPanelId) ||
            !panels.Any(p => p.Id == j.ReceiverPanelId) || j.RemovedRingsInches.IsDefaultOrEmpty ||
            !double.IsFinite(j.RemovedAreaSquareInches) || j.RemovedAreaSquareInches <= 0))
            throw new InvalidDataException("Joint ownership must refer to distinct existing parts and a recorded trim.");
        var canonical = TimberStockLayout.Create(panels, profile, ct);
        if (!canonical.Planned || canonical.SheetCount != stock.SheetCount || !canonical.Placements.SequenceEqual(stock.Placements))
            throw new InvalidDataException("Guides require the complete deterministic stock layout, with every part placed once.");
        if (!TimberAssemblyVerifier.Verify(panels, ct).Passed)
            throw new InvalidDataException("Guide geometry failed independent assembly/partition verification.");
        var parts = panels.OrderBy(p => p.Id, StringComparer.Ordinal).Select((p, index) => new TimberGuidePart(
            FormattableString.Invariant($"P{index + 1:000}"), p, stock.Placements.Single(s => s.PanelId == p.Id))).ToImmutableArray();
        var vectors = ImmutableArray.CreateBuilder<TimberGuideVector>();
        for (int sheet = 1; sheet <= stock.SheetCount; sheet++)
        {
            ct.ThrowIfCancellationRequested();
            string file = FormattableString.Invariant($"STOCK-REFERENCE-{sheet:000}.dxf");
            string title = FormattableString.Invariant($"Stock {sheet} / {stock.SheetCount} — REFERENCE ONLY");
            var selected = parts.Where(p => p.Placement.Sheet == sheet).ToArray();
            var contours = ImmutableArray.CreateBuilder<ImmutableArray<TimberPoint2>>();
            contours.Add(Rectangle(0, 0, profile.StockWidthInches, profile.StockHeightInches));
            contours.Add(Rectangle(profile.MarginInches, profile.MarginInches,
                profile.StockWidthInches - profile.MarginInches, profile.StockHeightInches - profile.MarginInches));
            var labels = ImmutableArray.CreateBuilder<TimberGuideLabel>();
            labels.Add(new(file, null, null, null, new(0, profile.StockHeightInches + 0.5), title));
            foreach (var part in selected)
            {
                foreach (var ring in part.Panel.RingsInches) contours.Add(ring.Select(part.Placement.Project).ToImmutableArray());
                labels.Add(Label(file, part, part.Placement.Project(MaterialPoint(part.Panel)), part.Alias));
            }
            double y = -0.5;
            foreach (var part in selected)
            {
                labels.Add(Label(file, part, new(0, y), PartLegend(part)));
                y -= 0.55;
            }
            labels.Add(new(file, null, null, null, new(0, y), "Nominal contours; apply kerf compensation in laser software.\nPhysical checks outstanding; not manufacturing ready."));
            vectors.Add(Vector(file, "Stock", title, contours.ToImmutable(), labels.ToImmutable(), selected.Select(p => p.Panel.Id).ToImmutableArray()));
        }
        vectors.Add(TimberGuideProjection.Create(parts, fitted.Joints, ct));
        return new(parts, vectors.ToImmutable(), fitted.Joints);
    }

    internal static string PartLegend(TimberGuidePart part) => $"{part.Alias} | Panel: {part.Panel.Id}\nSource: {part.Panel.SourceId} | Face: {part.Panel.FaceId}";
    internal static TimberPoint2 MaterialPoint(TimberPanel panel)
    {
        // Verification established that every triangle is inside retained material.
        // Its centroid identifies that material even when the bounding-box center is a hole.
        var triangle = panel.Triangles[0];
        return new((triangle.A.X + triangle.B.X + triangle.C.X) / 3, (triangle.A.Y + triangle.B.Y + triangle.C.Y) / 3);
    }
    internal static TimberGuideLabel Label(string file, TimberGuidePart part, TimberPoint2 position, string text) =>
        new(file, part.Panel.Id, part.Panel.SourceId, part.Panel.FaceId, position, text);
    internal static ImmutableArray<TimberPoint2> Rectangle(double x, double y, double maxX, double maxY) =>
        [new(x, y), new(maxX, y), new(maxX, maxY), new(x, maxY)];
    internal static TimberGuideVector Vector(string file, string role, string title,
        ImmutableArray<ImmutableArray<TimberPoint2>> contours, ImmutableArray<TimberGuideLabel> labels, ImmutableArray<string> panelIds)
    {
        var geometry = contours;
        var anchors = labels.Where(l => l.PanelId is not null && !l.Text.Contains('\n') && l.Text.StartsWith('P')).ToImmutableArray();
        double right = contours.SelectMany(r => r).Max(p => p.X) + 0.5;
        double bottom = contours.SelectMany(r => r).Min(p => p.Y);
        double nextY = contours.SelectMany(r => r).Max(p => p.Y);
        var callouts = new List<TimberGuideLabel>();
        var leaders = contours.ToBuilder();
        foreach (var anchor in anchors.OrderByDescending(l => l.PhysicalPositionInches.Y).ThenBy(l => l.Text, StringComparer.Ordinal))
        {
            double y = Math.Min(nextY, anchor.PhysicalPositionInches.Y); nextY = y - 0.3;
            var position = new TimberPoint2(right, y);
            callouts.Add(anchor with { PhysicalPositionInches = position });
            leaders.Add([anchor.PhysicalPositionInches, new(right - 0.05, y), new(right - 0.05, y + 0.001)]);
        }
        double noteShift = Math.Max(0, bottom - nextY);
        labels = labels.Except(anchors).Select(l => l.PhysicalPositionInches.Y < bottom
            ? l with { PhysicalPositionInches = new(l.PhysicalPositionInches.X, l.PhysicalPositionInches.Y - noteShift) } : l)
            .Concat(callouts).ToImmutableArray();
        contours = leaders.ToImmutable();
        // Conservative text envelopes: native notes use the specified height and an
        // unconstrained width. One full character-height per glyph bounds common fonts.
        var points = contours.SelectMany(r => r).Concat(labels.SelectMany(l => new[]
        {
            new TimberPoint2(l.PhysicalPositionInches.X, l.PhysicalPositionInches.Y - l.Text.Split('\n').Length * l.TextHeightInches * 1.5),
            new TimberPoint2(l.PhysicalPositionInches.X + l.Text.Split('\n').Max(s => s.Length) * l.TextHeightInches,
                l.PhysicalPositionInches.Y + l.TextHeightInches * 1.5)
        })).ToArray();
        if (points.Any(p => !double.IsFinite(p.X) || !double.IsFinite(p.Y))) throw new InvalidDataException("Guide projection must be finite.");
        return new(new(file, role, title, new(points.Min(p => p.X), points.Min(p => p.Y)),
            new(points.Max(p => p.X), points.Max(p => p.Y)), panelIds), contours, labels, geometry, anchors);
    }
}
