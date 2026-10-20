using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitThyme.Core;

namespace RevitThyme.Addin;

public sealed partial class RevitModel(UIApplication app, FoundationApplication host) : IGenericModel, IUtilityModel, IDynamicModel, ITimberFoldModel, ITimberNativeKitModel, ITimberNativeReconciliationModel, IViewImageModel, IDocumentLifecycleModel
{
    public bool TrustedCodeEnabled => host.TrustedCodeEnabled;
    public string TimberFoldEvidenceDirectory => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RevitThyme", "TimberFold", "inspections");
    public TimberSnapshot ExtractTimberFold(TimberFoldProfile profile, TimberScope? scope)
        => ExtractTimberFold(profile, scope, CancellationToken.None);
    public TimberSnapshot ExtractTimberFold(TimberFoldProfile profile, TimberScope? scope, CancellationToken cancellationToken)
    {
        var captured = Capture();
        return NativeTimberExtraction.ExtractCancellable(Document, captured.Target, profile, scope, cancellationToken);
    }
    public OperationResult ExecuteCode(OperationRequest request) => NativeDynamicExecution.Run(app, this, request);
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
    public ElementPage ListViews(ElementQuery query)
    {
        host.AssertDocument(Document);
        using var collector = new FilteredElementCollector(Document).OfClass(typeof(View));
        return ElementInspection.Page(collector, query);
    }
    public ElementPage QueryElements(ElementQuery query)
    {
        host.AssertDocument(Document);
        using var collector = new FilteredElementCollector(Document);
        if (query.CategoryId is not null) collector.OfCategoryId(new ElementId(long.Parse(query.CategoryId, System.Globalization.CultureInfo.InvariantCulture)));
        if (!query.IncludeTypes) collector.WhereElementIsNotElementType();
        else collector.WherePasses(new LogicalOrFilter(new ElementIsElementTypeFilter(), new ElementIsElementTypeFilter(true)));
        return ElementInspection.Page(collector, query);
    }
    public ElementDetail[] InspectElements(string[] ids, int parameterOffset)
    { host.AssertDocument(Document); return ids.Select(id => ElementInspection.Detail(Document, id, parameterOffset)).ToArray(); }
    public string[] GetSelection()
    {
        host.AssertDocument(Document); var ids = app.ActiveUIDocument.Selection.GetElementIds();
        if (ids.Count > 100) throw new OperationException("selection_too_large", "Selection exceeds 100 elements; reduce it in Revit.");
        return ids.Select(id => Document.GetElement(id).UniqueId).Order().ToArray();
    }
    public void SetSelection(string[] ids)
    { host.AssertDocument(Document); app.ActiveUIDocument.Selection.SetElementIds(ids.Select(id => ElementInspection.Resolve(Document, id).Id).ToArray()); }
    public ParameterInfo ReadParameter(string elementId, string parameterId)
    { host.AssertDocument(Document); return ElementInspection.Read(ElementInspection.ResolveParameter(Document, elementId, parameterId)); }
    public IParameterChange BeginParameterChange()
    {
        if (!Capture().CanModify) throw new OperationException("write_blocked", "Document cannot be edited.");
        return new RevitChange(Document, Document.ActiveView, "RevitThyme Set Parameters");
    }
    public ViewRangeInfo ReadViewRange() { host.AssertDocument(Document); return NativeViewRange.Read(Document); }
    public RoomDimensionPreview PlanRoomDimensions(string roomId, RoomDimensionOptions options)
    { host.AssertDocument(Document); return NativeRoomDimensions.Plan(Document, roomId, options); }
    public IUtilityChange BeginUtilityChange(string label)
    {
        if (!Capture().CanModify) throw new OperationException("write_blocked", "Document cannot be edited.");
        return new RevitChange(Document, Document.ActiveView, label);
    }
    public bool VerifyDimensions(long[] ids, RoomDimensionPreview preview) => NativeRoomDimensions.Verify(Document, ids, preview);
    public bool ElementsAbsent(long[] ids) => ids.All(id => Document.GetElement(new ElementId(id)) is null);
}
