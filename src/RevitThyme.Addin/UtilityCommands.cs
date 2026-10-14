using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitThyme.Addin;

[Transaction(TransactionMode.Manual)]
public sealed class ViewRangeCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
    {
        if (FoundationApplication.Instance is not { } host) { message = "RevitThyme is not running."; return Result.Failed; }
        host.ShowViewRange(data.Application); return Result.Succeeded;
    }
}
[Transaction(TransactionMode.Manual)]
public sealed class RoomDimensionsCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
    {
        if (FoundationApplication.Instance is not { } host) { message = "RevitThyme is not running."; return Result.Failed; }
        host.ShowRoomDimensions(data.Application); return Result.Succeeded;
    }
}
