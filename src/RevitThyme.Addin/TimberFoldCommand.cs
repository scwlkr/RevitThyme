using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitThyme.Addin;

[Transaction(TransactionMode.Manual)]
public sealed class TimberFoldCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
    {
        if (FoundationApplication.Instance is not { } host) { message = "RevitThyme is not running."; return Result.Failed; }
        host.ShowTimberFold(data.Application); return Result.Succeeded;
    }
}

public sealed class TimberFoldAvailability : IExternalCommandAvailability
{
    public bool IsCommandAvailable(UIApplication applicationData, CategorySet selectedCategories) =>
        applicationData.ActiveUIDocument?.Document is { IsFamilyDocument: false };
}
