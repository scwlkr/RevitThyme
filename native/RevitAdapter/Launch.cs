using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
namespace RevitThyme.RevitAdapter;
[Transaction(TransactionMode.Manual)]
public sealed class Launch : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        try
        {
            var host = Host.Instance ?? throw new InvalidDataException("Native adapter is unavailable.");
            _ = new Model(commandData.Application, host).CurrentTarget(); // Useful exclusions before launching desktop.
            host.Open(); return Result.Succeeded;
        }
        catch (Exception ex) { message = ex.Message; return Result.Failed; }
    }
}
