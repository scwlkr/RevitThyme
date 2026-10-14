using Autodesk.Revit.DB;
using RevitThyme.Native;
using PlanDirection = RevitThyme.Native.PlanDirection;

namespace RevitThyme.RevitAdapter;
internal static class Orientation
{
    // ViewDirection.Z does not distinguish a reflected ceiling plan from a floor plan.
    public static PlanDirection Direction(Document doc, ViewPlan view) => view.ViewType switch
    {
        ViewType.FloorPlan => PlanDirection.Down,
        ViewType.CeilingPlan => PlanDirection.Up,
        ViewType.EngineeringPlan => (doc.GetElement(view.GetTypeId()) as ViewFamilyType)?.PlanViewDirection switch
        {
            PlanViewDirection.Down => PlanDirection.Down,
            PlanViewDirection.Up => PlanDirection.Up,
            _ => throw new Rejection("unsupported_direction", "The structural plan type has no supported viewing direction.")
        },
        _ => throw new Rejection("unsupported_view", "Use a floor, structural or ceiling plan.")
    };
    public static Underlay Underlay(Document doc, ViewPlan view)
    {
        var baseId = view.GetUnderlayBaseLevel(); var topId = view.GetUnderlayTopLevel();
        var bottom = doc.GetElement(baseId) as Level; var top = doc.GetElement(topId) as Level;
        if (baseId != ElementId.InvalidElementId && bottom is null || topId != ElementId.InvalidElementId && top is null)
            throw new Rejection("invalid_underlay", "Underlay level reference cannot be resolved.");
        return new(bottom is not null, view.GetUnderlayOrientation() switch
        {
            UnderlayOrientation.LookingDown => PlanDirection.Down,
            UnderlayOrientation.LookingUp => PlanDirection.Up,
            _ => throw new Rejection("unsupported_underlay", "Unsupported underlay orientation.")
        },
        baseId.Value.ToString(), bottom?.Name ?? "None", bottom?.ProjectElevation ?? 0,
        topId.Value.ToString(), top?.Name ?? "Unbounded", top?.ProjectElevation ?? 0, top is null);
    }
}
