var expectedA = __A__; var expectedB = __B__;
if (System.Environment.ProcessId != __PID__ || System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture) != __START__
    || !string.Equals(doc.PathName, expectedA, StringComparison.OrdinalIgnoreCase) || !doc.Equals(uiapp.ActiveUIDocument?.Document)
    || doc.IsWorkshared || doc.IsLinked || doc.IsFamilyDocument
    || uiapp.Application.Documents.Cast<Document>().Any(d => d.IsWorkshared || d.IsLinked || d.IsFamilyDocument
        || !(string.Equals(d.PathName, expectedA, StringComparison.OrdinalIgnoreCase) || string.Equals(d.PathName, expectedB, StringComparison.OrdinalIgnoreCase))))
    throw new InvalidOperationException("Electrical diagnostic identity/context guard failed.");
cancellationToken.ThrowIfCancellationRequested();
var source = doc.GetElement(new ElementId(__SOURCE_VIEW_ID__)) as ViewPlan ?? throw new InvalidOperationException("Expected power plan missing.");
if (doc.ActiveView.Id != source.Id || source.IsTemplate || source.ViewTemplateId != ElementId.InvalidElementId || !source.CanViewBeDuplicated(ViewDuplicateOption.Duplicate))
    throw new InvalidOperationException("Diagnostic requires the exact active independently duplicable source plan.");
var beforeIds = new FilteredElementCollector(doc).WherePasses(new LogicalOrFilter(new ElementIsElementTypeFilter(), new ElementIsElementTypeFilter(true))).ToElementIds().ToHashSet();
var id = source.Duplicate(ViewDuplicateOption.Duplicate); var study = (ViewPlan)doc.GetElement(id); study.Name = __QA_NAME__;
doc.Regenerate();
var dependent = study.GetDependentElements(null).ToHashSet();
Func<Element, object> describe = e => {
    string? name = null; string? nameError = null;
    try { name = e.Name; } catch (Exception error) { nameError = error.GetType().FullName; }
    return new { id = e.Id.Value, uid = e.UniqueId, kind = e.GetType().FullName, category = e.Category?.Name,
        categoryId = e.Category?.Id.Value, categoryType = e.Category?.CategoryType.ToString(), ownerViewId = e.OwnerViewId.Value,
        e.ViewSpecific, studyDependent = dependent.Contains(e.Id), dependentIds = e.GetDependentElements(null).Select(d => d.Value).OrderBy(d => d).ToArray(),
        typeId = e.GetTypeId().Value, name, nameError, e.IsTransient, isElementType = e is ElementType,
        ownedByStudy = e.OwnerViewId == study.Id, sketchPlaneOfStudy = e is SketchPlane && study.SketchPlane?.Id == e.Id };
};
var companions = new FilteredElementCollector(doc).WherePasses(new LogicalOrFilter(new ElementIsElementTypeFilter(), new ElementIsElementTypeFilter(true)))
    .Where(e => !beforeIds.Contains(e.Id) && e.Id != study.Id).OrderBy(e => e.Id.Value).Select(describe).ToArray();
return new { temporaryQueryDuplicate = true, sourceId = source.Id.Value, sourceUid = source.UniqueId, root = describe(study), companions,
    boundary = "Evidence only. Host query transaction rolls back the temporary duplicate; no companion is accepted by this script." };
