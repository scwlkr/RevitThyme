using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitThyme.Core;

namespace RevitThyme.Addin;

public sealed class RevitModel(UIApplication app, FoundationApplication host) : IModel
{
    private Document Document => app.ActiveUIDocument?.Document
        ?? throw new OperationException("no_document", "Open a Revit project and refresh.");
    public Snapshot Capture()
    {
        var doc = Document; host.AssertDocument(doc);
        var view = doc.ActiveView ?? throw new OperationException("no_view", "Activate a Revit view.");
        string? block = doc.IsReadOnly ? "Document is read-only." :
            doc.IsModifiable ? "Finish the current Revit transaction." :
            !CanStartEdit(doc) ? "Finish the current Revit edit mode." : null;
        return new(new(host.Binding.ProcessId, host.Binding.ProcessStart, host.Binding.SessionId,
            host.DocumentId(doc), view.UniqueId, host.Revision), app.Application.VersionNumber,
            doc.Title, doc.PathName, doc.IsWorkshared, doc.IsModified, view.Name,
            view.ViewType.ToString(), view.Id.Value, block is null, block);
    }
    private static bool CanStartEdit(Document doc)
    {
        if (doc.IsFamilyDocument) return true;
        using var scope = new SketchEditScope(doc, "RevitThyme readiness");
        return scope.IsPermitted;
    }
    public string ReadViewName() { host.AssertDocument(Document); return Document.ActiveView.Name; }
    public IChange BeginChange()
    {
        var snapshot = Capture();
        if (!snapshot.CanModify) throw new OperationException("write_blocked", snapshot.WriteBlock!);
        return new RevitChange(Document, Document.ActiveView);
    }
}
