using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitThyme.Addin;

[Transaction(TransactionMode.Manual)]
public sealed class AgentCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
    {
        data.Application.GetDockablePane(AgentPane.Id).Show(); return Result.Succeeded;
    }
}

internal sealed class AgentPane(RevitThyme.UI.AgentPanel panel) : IDockablePaneProvider
{
    public static readonly DockablePaneId Id = new(new Guid("D988D5C7-B1BB-40CE-A636-48C344DA7E68"));
    public void SetupDockablePane(DockablePaneProviderData data)
    {
        data.FrameworkElement = panel;
        data.InitialState = new DockablePaneState { DockPosition = DockPosition.Right };
        data.VisibleByDefault = false;
    }
}
