using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitThyme.Core;

namespace RevitThyme.Addin;

public sealed partial class RevitModel
{
    public void ActivateView(string viewId)
    {
        var doc = Document; host.AssertDocument(doc);
        if (doc.GetElement(viewId) is not View view || view.IsTemplate || !view.CanBePrinted)
            throw new OperationException("unsupported_view", "Use an explicit printable non-template view in the captured document.");
        if (doc.ActiveView.Id == view.Id) return;
        try { app.ActiveUIDocument.ActiveView = view; }
        catch (Exception) { throw new OperationException("view_unconfirmed", "Native view activation or verification may be incomplete. Recover and inspect."); }
    }
}
