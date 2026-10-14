using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitThyme.Addin;

[Transaction(TransactionMode.Manual)]
public sealed class InspectCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        if (FoundationApplication.Instance is not { } host)
        {
            message = "RevitThyme Foundation is not running."; return Result.Failed;
        }
        host.Show(commandData.Application); return Result.Succeeded;
    }
}
