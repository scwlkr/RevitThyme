using Autodesk.Revit.DB;
using RevitThyme.Core;

namespace RevitThyme.Addin;

internal static class NativeViewRange
{
    private static readonly PlanViewPlane[] Planes = [PlanViewPlane.TopClipPlane, PlanViewPlane.CutPlane, PlanViewPlane.BottomClipPlane, PlanViewPlane.ViewDepthPlane];
    private static ViewPlan View(Document doc) => doc.ActiveView is ViewPlan p && !p.IsTemplate
        && p.ViewType is ViewType.FloorPlan or ViewType.CeilingPlan or ViewType.EngineeringPlan ? p
        : throw new OperationException("unsupported_view", "Activate a floor plan, ceiling plan or structural plan.");
    public static ViewRangeInfo Read(Document doc)
    {
        var view = View(doc);
        using var range = view.GetViewRange();
        var values = Planes.Select(p => new RangePlane(Key(doc, range.GetLevelId(p)), range.GetOffset(p))).ToArray();
        var levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.Elevation).ThenBy(l => l.Id.Value)
            .Select(l => new RangeLevel(l.UniqueId, l.Name.Length <= 64 ? l.Name : l.Name[..63] + "…", l.Elevation)).ToArray();
        if (levels.Length > 100) throw new OperationException("too_many_levels", "This utility currently supports projects with at most 100 levels.");
        string? block = null;
        if (doc.GetElement(view.ViewTemplateId) is View template)
        {
            var rangeId = new ElementId(BuiltInParameter.PLAN_VIEW_RANGE);
            if (template.GetTemplateParameterIds().Contains(rangeId) && !template.GetNonControlledTemplateParameterIds().Contains(rangeId))
                block = "View range is controlled by the view template. Edit the template in Revit or remove its range control.";
        }
        return new(new(values[0], values[1], values[2], values[3]), levels, view.GenLevel.Elevation, block is null, block);
    }
    public static void Set(Document doc, ViewRangeSettings settings)
    {
        var view = View(doc);
        if (!Read(doc).CanEdit) throw new OperationException("write_blocked", "View template controls the range.");
        using var range = view.GetViewRange();
        var values = new[] { settings.Top, settings.Cut, settings.Bottom, settings.Depth };
        for (int i = 0; i < Planes.Length; i++) { range.SetLevelId(Planes[i], Resolve(doc, values[i].Level)); range.SetOffset(Planes[i], values[i].OffsetFeet); }
        var errors = view.CheckPlanViewRangeValidity(range);
        if (errors.Count != 0) throw new OperationException("invalid_view_range", "Revit rejected plane order/levels: " + string.Join(", ", errors));
        view.SetViewRange(range);
    }
    private static string Key(Document doc, ElementId id) => id == PlanViewRange.Current ? "Current" : id == PlanViewRange.LevelAbove ? "Above"
        : id == PlanViewRange.LevelBelow ? "Below" : id == PlanViewRange.Unlimited ? "Unlimited" : doc.GetElement(id) is Level level ? level.UniqueId
        : throw new OperationException("unsupported_level", "This range uses an unsupported level reference.");
    private static ElementId Resolve(Document doc, string key) => key switch
    {
        "Current" => PlanViewRange.Current, "Above" => PlanViewRange.LevelAbove, "Below" => PlanViewRange.LevelBelow,
        "Unlimited" => PlanViewRange.Unlimited,
        _ => doc.GetElement(key) is Level l ? l.Id : throw new OperationException("invalid_level", "The selected level no longer exists.")
    };
}
