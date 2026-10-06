using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Runtime.CompilerServices;
using RevitThyme.Native;
using Range = RevitThyme.Native.Range;
using Plane = RevitThyme.Native.Plane;

namespace RevitThyme.RevitAdapter;
public sealed class Model(UIApplication app, Host host) : IModel
{
    private static readonly ConditionalWeakTable<Document, Identity> documents = new();
    private sealed class Identity { public string Id = Guid.NewGuid().ToString(); }
    internal static readonly PlanViewPlane[] Planes = [PlanViewPlane.TopClipPlane, PlanViewPlane.CutPlane, PlanViewPlane.BottomClipPlane, PlanViewPlane.ViewDepthPlane];
    private (Document doc, ViewPlan view) Active()
    {
        var doc = app.ActiveUIDocument?.Document ?? throw new Rejection("no_document", "Open an approved disposable project plan.");
        if (doc.IsFamilyDocument) throw new Rejection("family_document", "View Range requires a project document.");
        if (doc.IsReadOnly) throw new Rejection("read_only", "Document is read-only.");
        if (doc.IsModifiable) throw new Rejection("modifiable", "Finish the active transaction or edit mode before capture/Apply.");
        var view = doc.ActiveView as ViewPlan ?? throw new Rejection("unsupported_view", "Use a floor, engineering or ceiling plan.");
        if (view.IsTemplate || view.ViewType is not (ViewType.FloorPlan or ViewType.EngineeringPlan or ViewType.CeilingPlan))
            throw new Rejection("unsupported_view", "Use a non-template floor, engineering or ceiling plan.");
        if (view.GetPrimaryViewId() != ElementId.InvalidElementId || view.GetDependentViewIds().Count > 0)
            throw new Rejection("dependent_view", "Dependent views and primary views with dependents are excluded until propagation is qualified.");
        if (doc.GetElement(view.ViewTemplateId) is View template)
        {
            var range = new ElementId(BuiltInParameter.PLAN_VIEW_RANGE);
            if (template.GetTemplateParameterIds().Contains(range) && !template.GetNonControlledTemplateParameterIds().Contains(range))
                throw new Rejection("template_controlled", "The view template controls View Range. This tool does not change templates.");
        }
        return (doc, view);
    }
    public Target CurrentTarget()
    {
        var (doc, view) = Active();
        return new(host.ProcessId, host.StartTicks, host.SessionId, documents.GetValue(doc, _ => new()).Id, view.UniqueId, host.Session.Revision);
    }
    public Facts Capture()
    {
        var (doc, view) = Active(); var target = CurrentTarget();
        using var native = view.GetViewRange();
        var raw = ReadRange(native);
        var values = Planes.Select(p => Describe(doc, view, native.GetLevelId(p), native.GetOffset(p), p)).ToArray();
        var geometry = Geometry.Capture(doc);
        var capture = new Capture(Guid.NewGuid().ToString(), target, doc.Title, view.Name,
            view.ViewType == ViewType.CeilingPlan ? "ceiling" : view.ViewType == ViewType.EngineeringPlan ? "engineering" : "floor",
            new(values[0], values[1], values[2], values[3]), geometry.Bounds, (uint)geometry.Triangles.Length,
            geometry.Partial, geometry.Diagnostics, 600);
        return new(capture, raw, geometry.Triangles, DateTime.UtcNow);
    }
    private (Document doc, ViewPlan view) Resolve(Facts facts)
    {
        var state = Active();
        if (CurrentTarget() != facts.Capture.Target) throw new Rejection("stale_target", "Document, view or model revision changed. Refresh.");
        return state;
    }
    public RawRange Read(Facts facts)
    {
        // During our transaction the document is deliberately modifiable and revision may change at Commit.
        // Readback still resolves the exact document instance and view; never another active document.
        var doc = app.ActiveUIDocument?.Document ?? throw new InvalidDataException("Document closed.");
        if (documents.GetValue(doc, _ => new()).Id != facts.Capture.Target.DocumentId || doc.ActiveView.UniqueId != facts.Capture.Target.ViewId)
            throw new InvalidDataException("Readback target changed.");
        using var range = ((ViewPlan)doc.ActiveView).GetViewRange(); return ReadRange(range);
    }
    public RawRange Validate(Facts facts, Range proposed)
    {
        var (doc, view) = Resolve(facts); using var range = view.GetViewRange();
        for (int i = 0; i < Planes.Length; i++)
        {
            var p = proposed.Planes[i]; var original = facts.Capture.Original.Planes[i];
            if (p.LevelId != original.LevelId || p.BaseFeet != original.BaseFeet || p.LevelName != original.LevelName
                || p.AllowUnlimited != original.AllowUnlimited || (p.Unlimited && !original.AllowUnlimited))
                throw new InvalidDataException("Captured level references required.");
            var id = p.Unlimited ? PlanViewRange.Unlimited : new ElementId(long.Parse(p.LevelId));
            // Revit stores only Unlimited, so returning to finite uses the current view level explicitly.
            if (!p.Unlimited && id == PlanViewRange.Unlimited) id = PlanViewRange.Current;
            _ = Describe(doc, view, id, p.OffsetFeet, Planes[i]);
            range.SetLevelId(Planes[i], id); range.SetOffset(Planes[i], p.OffsetFeet);
        }
        var errors = view.CheckPlanViewRangeValidity(range);
        if (errors.Count > 0) throw new Rejection("invalid_native_range", "Revit rejects this plan range: " + string.Join(", ", errors));
        return ReadRange(range);
    }
    public IChange Begin(Facts facts)
    {
        var (doc, view) = Resolve(facts); return new Change(doc, view);
    }
    internal static RawRange ReadRange(PlanViewRange range)
    {
        var values = Planes.Select(p => new RawPlane(range.GetLevelId(p).Value.ToString(), range.GetOffset(p))).ToArray();
        return new(values[0], values[1], values[2], values[3]);
    }
    private static Plane Describe(Document doc, ViewPlan view, ElementId id, double offset, PlanViewPlane plane)
    {
        var current = view.GenLevel ?? throw new InvalidDataException("View level missing.");
        bool unlimited = id == PlanViewRange.Unlimited;
        Level level;
        if (id == PlanViewRange.Current || unlimited) level = current;
        else if (id == PlanViewRange.LevelAbove || id == PlanViewRange.LevelBelow)
        {
            var levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>();
            level = id == PlanViewRange.LevelAbove
                ? levels.Where(l => l.ProjectElevation > current.ProjectElevation).OrderBy(l => l.ProjectElevation).FirstOrDefault()!
                : levels.Where(l => l.ProjectElevation < current.ProjectElevation).OrderByDescending(l => l.ProjectElevation).FirstOrDefault()!;
            if (level is null) throw new InvalidDataException("Relative level cannot be resolved.");
        }
        else level = doc.GetElement(id) as Level ?? throw new InvalidDataException("Reference is not a level.");
        if (!double.IsFinite(offset) || !double.IsFinite(level.ProjectElevation)) throw new InvalidDataException("Nonfinite native values.");
        bool allow = plane != PlanViewPlane.CutPlane && (plane != PlanViewPlane.BottomClipPlane || view.ViewType != ViewType.CeilingPlan);
        string label = unlimited ? level.Name + " (Unlimited; finite uses Current)" : id.Value < 0 ? level.Name + " (relative reference)" : level.Name;
        return new(id.Value.ToString(), label, level.ProjectElevation, offset, unlimited, allow);
    }
}
