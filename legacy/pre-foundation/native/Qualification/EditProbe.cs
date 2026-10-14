using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitThyme.Native;
using RevitThyme.RevitAdapter;

namespace RevitThyme.Qualification;
// A native regression driver, excluded from the installed product. No sketch edits.
public static class EditProbe
{
    public static object Run(UIApplication app, Host host, string approvedPath, string approvedView, long sketchId)
    {
        var doc = app.ActiveUIDocument.Document;
        if (!string.Equals(doc.PathName, approvedPath, StringComparison.OrdinalIgnoreCase)
            || doc.ActiveView.UniqueId != approvedView || doc.IsWorkshared || doc.IsReadOnly || doc.IsModifiable)
            throw new InvalidOperationException("Approved disposable target required.");
        var model = new Model(app, host);
        var before = model.CurrentTarget();
        var modified = doc.IsModified;
        string code = "accepted";
        bool active, permitted;
        using (var edit = new SketchEditScope(doc, "RevitThyme no-write qualification"))
        {
            edit.Start(new ElementId(sketchId));
            try
            {
                active = edit.IsActive;
                using var readiness = new SketchEditScope(doc, "RevitThyme readiness qualification");
                permitted = readiness.IsPermitted;
                try { _ = model.CurrentTarget(); }
                catch (Rejection error) { code = error.Code; }
            }
            finally { if (edit.IsActive) edit.Cancel(); }
        }
        var after = model.CurrentTarget();
        return new { Active = active, AnotherEditPermitted = permitted, Code = code,
            ModifiableAfter = doc.IsModifiable, ModifiedPreserved = doc.IsModified == modified,
            SameDocumentAndView = before.DocumentId == after.DocumentId && before.ViewId == after.ViewId };
    }
}
