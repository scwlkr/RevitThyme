using Autodesk.Revit.DB;
using RevitThyme.Core;
using System.Globalization;

namespace RevitThyme.Addin;

/// <summary>Read-only extraction. Call synchronously inside the pinned Revit API callback.</summary>
public static class NativeTimberExtraction
{
    public static TimberSnapshot Extract(Document doc, Target target, TimberFoldProfile profile, TimberScope? scope)
    {
        var diagnostics = new List<TimberDiagnostic>(profile.Validate());
        var sources = new List<TimberSource>(); var faces = new List<TimberFace>();
        var openings = new List<TimberOpening>();
        var chosen = ResolveScope(doc, scope, diagnostics);
        if (doc.IsFamilyDocument)
            diagnostics.Add(new("family_document", TimberSeverity.Error, "TimberFold requires a native project document."));
        if (diagnostics.Any(d => d.Severity == TimberSeverity.Error))
            return new(target, "", chosen.PhaseId, chosen.OptionIds, [], [], [], diagnostics.ToArray());
        var chordFeet = profile.ChordToleranceInches * profile.ScaleDenominator / 12;
        var planarFeet = 0.00001 * profile.ScaleDenominator / 12;
        using var options = new Options { DetailLevel = ViewDetailLevel.Fine, ComputeReferences = true, IncludeNonVisibleObjects = false };
        using var collector = new FilteredElementCollector(doc).WhereElementIsNotElementType();
        var elements = collector.Where(e => e.Category?.CategoryType == CategoryType.Model)
            .OrderBy(e => e.Id.Value).ToArray();
        foreach (var element in elements)
        {
            var category = Category(element); var id = Id(element);
            var reason = Exclusion(element, category, chosen);
            if (reason is not null)
            { sources.Add(Source(element, category, TimberDisposition.Excluded, reason, [], [])); continue; }
            if (element is Wall or RoofBase)
            {
                var faceStart = faces.Count; var errorStart = diagnostics.Count;
                try
                {
                    if (element is Wall w && (w.WallType.Kind != WallKind.Basic ||
                        w.CrossSection != WallCrossSection.Vertical || w.Location is not LocationCurve { Curve: Line }))
                        throw new InvalidOperationException("Only vertical, straight, basic native walls are supported.");
                    ExtractHost((HostObject)element, category, options, chordFeet, planarFeet, faces, diagnostics);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                { diagnostics.Add(new("native_host_geometry", TimberSeverity.Error, ex.Message, id)); }
                var blocked = diagnostics.Skip(errorStart).Any(d => d.Severity == TimberSeverity.Error);
                sources.Add(Source(element, category, blocked ? TimberDisposition.Unsupported : TimberDisposition.Included,
                    blocked ? "Native shell geometry could not be reconciled completely." : "Native exterior-shell face geometry.",
                    faces.Skip(faceStart).Select(f => f.Id).ToArray(), []));
            }
            else if (category is TimberCategory.Window or TimberCategory.Door || element is Opening)
            {
                // Resolve only after all native hosts have been accounted for.
            }
            else
            {
                var unsupported = category is TimberCategory.Link or TimberCategory.Import;
                sources.Add(Source(element, category, unsupported ? TimberDisposition.Unsupported : TimberDisposition.Excluded,
                    unsupported ? "Linked/imported model geometry is outside the supported native shell." : "Outside the exterior-shell kit scope.", [], []));
                if (unsupported) diagnostics.Add(new("unsupported_architecture", TimberSeverity.Error,
                    "Linked/imported model geometry requires explicit scope exclusion; it cannot be silently omitted.", id));
            }
        }
        foreach (var element in elements.Where(e => Category(e) is TimberCategory.Window or TimberCategory.Door || e is Opening))
        {
            if (sources.Any(s => s.Id == Id(element))) continue;
            NativeTimberOpenings.Extract(doc, element, chosen.Includes.Contains(Id(element)), options,
                chordFeet, faces, sources, openings, diagnostics);
        }
        // Host source records retain the IDs of every resolved opening as well as their faces.
        for (var i = 0; i < sources.Count; i++)
        {
            var source = sources[i];
            var ownFaces = faces.Where(f => f.SourceId == source.Id).ToArray();
            var ownOpenings = openings.Where(o => o.SourceId == source.Id || o.HostSourceId == source.Id).ToArray();
            sources[i] = source with
            {
                OpeningIds = ownOpenings.Select(o => o.Id).Order(StringComparer.Ordinal).ToArray(),
                GeometryFingerprint = NativeTimberGeometry.Hash(new { source.GeometryFingerprint, Faces = ownFaces, Openings = ownOpenings })
            };
        }
        var orderedSources = sources.OrderBy(s => s.Id, StringComparer.Ordinal).ToArray();
        var orderedFaces = faces.OrderBy(f => f.Id, StringComparer.Ordinal).ToArray();
        var orderedOpenings = openings.OrderBy(o => o.Id, StringComparer.Ordinal).ToArray();
        var fingerprint = NativeTimberGeometry.Hash(new
        { Project = doc.ProjectInformation.UniqueId, chosen.PhaseId, chosen.OptionIds, Sources = orderedSources, Faces = orderedFaces, Openings = orderedOpenings });
        return new(target, fingerprint, chosen.PhaseId, chosen.OptionIds, orderedSources, orderedFaces,
            orderedOpenings, diagnostics.ToArray());
    }

    private static void ExtractHost(HostObject host, TimberCategory category, Options options,
        double chordFeet, double planarFeet, List<TimberFace> faces, List<TimberDiagnostic> diagnostics)
    {
        var id = Id(host); var isRoof = category == TimberCategory.Roof;
        IList<Reference> references;
        try { references = isRoof ? HostObjectUtils.GetTopFaces(host) : HostObjectUtils.GetSideFaces(host, ShellLayerType.Exterior); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        { throw new InvalidOperationException("Native shell reference query failed: " + ex.Message, ex); }
        var resolved = new List<TimberFace>(); var unresolved = 0;
        foreach (var reference in references)
        {
            Face? native;
            try { native = host.GetGeometryObjectFromReference(reference) as Face; }
            catch (Autodesk.Revit.Exceptions.InvalidOperationException) { unresolved++; continue; }
            catch (Autodesk.Revit.Exceptions.ArgumentException) { unresolved++; continue; }
            if (native is null) { unresolved++; continue; }
            if (native is not PlanarFace plane)
                throw new InvalidOperationException("Native shell contains a nonplanar face.");
            if (plane.Area <= 1e-10) throw new InvalidOperationException("Native shell contains an empty face.");
            resolved.Add(NativeTimberGeometry.Face(plane, id, isRoof ? TimberFaceKind.Roof : TimberFaceKind.Wall, chordFeet, planarFeet));
        }
        if (isRoof)
        {
            using var geometry = host.get_Geometry(options) ?? throw new InvalidOperationException("Roof has no native solid geometry.");
            var solidTop = new List<TimberFace>();
            foreach (var solid in NativeTimberGeometry.Solids(geometry))
            foreach (Face native in solid.Faces)
            {
                var uv = native.GetBoundingBox();
                if (native.ComputeNormal((uv.Min + uv.Max) / 2).Z <= 1e-8) continue;
                if (native is not PlanarFace plane) throw new InvalidOperationException("Roof contains a nonplanar upward solid face.");
                if (plane.Area <= 1e-10) throw new InvalidOperationException("Roof contains an empty upward solid face.");
                solidTop.Add(NativeTimberGeometry.Face(plane, id, TimberFaceKind.Roof, chordFeet, planarFeet));
            }
            var solidKeys = solidTop.Select(NativeTimberGeometry.FaceKey).Order(StringComparer.Ordinal).ToArray();
            var refKeys = resolved.Select(NativeTimberGeometry.FaceKey).Order(StringComparer.Ordinal).ToArray();
            // Revit 2027 can return an additional unresolved top reference (the
            // original native gable has three references but two actual faces).
            // The full solid inventory is authoritative; every resolvable face
            // must agree with it, and a fully resolved provider must match it.
            if (refKeys.Except(solidKeys).Any() ||
                unresolved == 0 && !solidKeys.SequenceEqual(refKeys))
                throw new InvalidOperationException($"Roof top references and independently enumerated native solid faces disagree ({references.Count} references, {solidTop.Count} solid faces, {unresolved} unresolved).");
            if (unresolved > 0) diagnostics.Add(new("roof_reference_reconciled", TimberSeverity.Warning,
                $"Reconciled {references.Count} top references ({unresolved} unresolved) against all {solidTop.Count} independently enumerated upward solid faces.", id));
            resolved = solidTop;
        }
        else if (unresolved > 0) throw new InvalidOperationException($"Wall has {unresolved} unresolved exterior references.");
        if (resolved.Count == 0) throw new InvalidOperationException("No supported native shell faces were found.");
        var sorted = resolved.OrderBy(NativeTimberGeometry.FaceKey, StringComparer.Ordinal).ToArray();
        if (sorted.Select(NativeTimberGeometry.FaceKey).Distinct().Count() != sorted.Length)
            throw new InvalidOperationException("Native shell contains duplicate faces.");
        for (var i = 0; i < sorted.Length; i++) faces.Add(sorted[i] with { Id = $"{(isRoof ? "R" : "W")}-{id}-{i + 1}" });
    }

    internal static string Id(Element element) => element.Id.Value.ToString(CultureInfo.InvariantCulture);
    internal static TimberSource Source(Element element, TimberCategory category, TimberDisposition disposition,
        string reason, string[] faceIds, string[] openingIds)
    {
        var box = element.get_BoundingBox(null);
        return new(Id(element), element.UniqueId, category, disposition, reason, faceIds, openingIds,
            NativeTimberGeometry.Hash(new { Type = element.GetTypeId().Value, Created = element.CreatedPhaseId.Value,
                Demolished = element.DemolishedPhaseId.Value, Option = element.DesignOption?.Id.Value,
                Min = box is null ? null : NativeTimberGeometry.Point(box.Min), Max = box is null ? null : NativeTimberGeometry.Point(box.Max) }));
    }

    internal static TimberCategory Category(Element e)
    {
        if (e is Wall) return TimberCategory.Wall;
        if (e is RoofBase) return TimberCategory.Roof;
        if (e is Opening) return TimberCategory.Opening;
        if (e is RevitLinkInstance) return TimberCategory.Link;
        if (e is ImportInstance) return TimberCategory.Import;
        return (BuiltInCategory)(e.Category?.Id.Value ?? 0) switch
        {
            BuiltInCategory.OST_Doors => TimberCategory.Door, BuiltInCategory.OST_Windows => TimberCategory.Window,
            BuiltInCategory.OST_Floors => TimberCategory.Floor, BuiltInCategory.OST_Furniture => TimberCategory.Furniture,
            BuiltInCategory.OST_Topography or BuiltInCategory.OST_Toposolid or BuiltInCategory.OST_Site => TimberCategory.Site,
            BuiltInCategory.OST_DuctCurves or BuiltInCategory.OST_PipeCurves or BuiltInCategory.OST_MechanicalEquipment or
                BuiltInCategory.OST_ElectricalEquipment or BuiltInCategory.OST_ElectricalFixtures or BuiltInCategory.OST_LightingFixtures => TimberCategory.Mep,
            _ => TimberCategory.Other
        };
    }

    private sealed record ResolvedScope(string PhaseId, ElementId Phase, string[] OptionIds,
        HashSet<string> Includes, HashSet<string> Excludes);

    private static ResolvedScope ResolveScope(Document doc, TimberScope? scope, List<TimberDiagnostic> diagnostics)
    {
        var phases = doc.Phases.Cast<Phase>().ToArray();
        var phase = scope is null ? doc.GetElement(doc.ActiveView?.get_Parameter(BuiltInParameter.VIEW_PHASE)?.AsElementId()
            ?? ElementId.InvalidElementId) as Phase : Resolve(doc, scope.PhaseId) as Phase;
        if (phase is null && scope is null && phases.Length > 0)
        {
            phase = phases[^1];
            diagnostics.Add(new("phase_fallback", TimberSeverity.Warning, "Active view has no phase; using the last native project phase explicitly recorded in this snapshot."));
        }
        if (phase is null) diagnostics.Add(new("invalid_phase", TimberSeverity.Error, "Select a phase belonging to this project."));
        using var optionCollector = new FilteredElementCollector(doc).OfClass(typeof(DesignOption));
        var options = optionCollector.Cast<DesignOption>().ToArray();
        var selected = scope is null ? options.Where(o => o.IsPrimary).ToArray() :
            scope.DesignOptionIds.Select(id => Resolve(doc, id)).OfType<DesignOption>().ToArray();
        if (scope is not null && (selected.Length != scope.DesignOptionIds.Length || selected.Select(o => o.Id.Value).Distinct().Count() != selected.Length))
            diagnostics.Add(new("invalid_design_options", TimberSeverity.Error, "Every selected design option must exist once in this project."));
        if (selected.GroupBy(o => o.get_Parameter(BuiltInParameter.OPTION_SET_ID)?.AsElementId().Value).Any(g => g.Count() > 1))
            diagnostics.Add(new("incompatible_design_options", TimberSeverity.Error, "Select at most one design option per option set."));
        var includes = ResolveIds(doc, scope?.IncludeIds, "include", diagnostics);
        var excludes = ResolveIds(doc, scope?.ExcludeIds, "exclude", diagnostics);
        if (includes.Overlaps(excludes)) diagnostics.Add(new("scope_conflict", TimberSeverity.Error, "An element cannot be both explicitly included and excluded."));
        diagnostics.Add(new("scope_recorded", TimberSeverity.Info, scope is null ?
            "Resolved active-view phase (or recorded fallback), main model and primary design options." : "Resolved explicit phase, main model and selected design options."));
        return new(phase is null ? "" : Id(phase), phase?.Id ?? ElementId.InvalidElementId,
            selected.Select(Id).Order(StringComparer.Ordinal).ToArray(), includes, excludes);
    }

    private static HashSet<string> ResolveIds(Document doc, string[]? ids, string role, List<TimberDiagnostic> diagnostics)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in ids ?? [])
        {
            var element = Resolve(doc, id);
            if (element is null || element is ElementType || element.Category?.CategoryType != CategoryType.Model)
                diagnostics.Add(new("invalid_scope_id", TimberSeverity.Error, $"The {role} element '{id}' is not a native model instance.", id));
            else result.Add(Id(element));
        }
        return result;
    }
    private static Element? Resolve(Document doc, string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        try
        {
            return long.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
                ? doc.GetElement(new ElementId(number)) : doc.GetElement(id);
        }
        catch (Autodesk.Revit.Exceptions.ArgumentException) { return null; }
    }

    private static string? Exclusion(Element e, TimberCategory category, ResolvedScope scope)
    {
        if (scope.Excludes.Contains(Id(e))) return "Explicit scope exclusion.";
        if (e.DesignOption is { } option && !scope.OptionIds.Contains(Id(option))) return "Outside selected design options.";
        if (e.HasPhases() && e.GetPhaseStatus(scope.Phase) is not (ElementOnPhaseStatus.New or ElementOnPhaseStatus.Existing))
            return "Not present as existing/new geometry in the selected phase.";
        if (category == TimberCategory.Wall && e is Wall wall && wall.WallType.Function != WallFunction.Exterior && !scope.Includes.Contains(Id(e)))
            return "Wall type function is outside the default exterior-shell scope.";
        return null;
    }
}
