#nullable enable
var expectedA = __A__; var expectedB = __B__;
if (System.Environment.ProcessId != __PID__ || System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture) != __START__
    || !string.Equals(doc.PathName, expectedA, StringComparison.OrdinalIgnoreCase) || !doc.Equals(uiapp.ActiveUIDocument?.Document)
    || doc.IsWorkshared || doc.IsLinked || doc.IsFamilyDocument
    || uiapp.Application.Documents.Cast<Document>().Any(d => d.IsWorkshared || d.IsLinked || d.IsFamilyDocument
        || !(string.Equals(d.PathName, expectedA, StringComparison.OrdinalIgnoreCase) || string.Equals(d.PathName, expectedB, StringComparison.OrdinalIgnoreCase))))
    throw new InvalidOperationException("Applied inspection identity/context guard failed.");
cancellationToken.ThrowIfCancellationRequested();
var source = doc.GetElement(new ElementId(__SOURCE_VIEW_ID__)) as ViewPlan ?? throw new InvalidOperationException("Expected source plan missing.");
var study = doc.GetElement(__STUDY_UID__) as ViewPlan ?? throw new InvalidOperationException("Exact Applied study missing.");
if ((doc.ActiveView.Id != source.Id && doc.ActiveView.Id != study.Id) || study.Id.Value != __INSPECT_ROOT_ID__ || study.Name != __QA_NAME__ || study.ViewTemplateId != ElementId.InvalidElementId || study.GenLevel.Id != source.GenLevel.Id)
    throw new InvalidOperationException("Applied study/source identity changed.");
var nativeAdded = new long[] { __INSPECT_ADDED_IDS__ }; var known = new HashSet<long>(new long[] { __INSPECT_KNOWN_IDS__ });
var all = new FilteredElementCollector(doc).WherePasses(new LogicalOrFilter(new ElementIsElementTypeFilter(), new ElementIsElementTypeFilter(true))).ToElementIds().Select(i => i.Value).ToHashSet();
var dependent = study.GetDependentElements(null).ToHashSet();
Func<long, object> describe = id => {
    try {
    var e = doc.GetElement(new ElementId(id));
    if (e == null) return new { id, exists = false, collectorMembership = all.Contains(id), studyDependent = dependent.Contains(new ElementId(id)) };
    string? name = null; string? nameError = null;
    try { name = e.Name; } catch (Exception error) { nameError = error.GetType().FullName; }
    return new { id, exists = true, uid = e.UniqueId, kind = e.GetType().FullName, category = e.Category?.Name,
        categoryId = e.Category?.Id.Value, categoryType = e.Category?.CategoryType.ToString(), ownerViewId = e.OwnerViewId.Value,
        e.ViewSpecific, studyDependent = dependent.Contains(e.Id), dependentIds = e.GetDependentElements(null).Select(d => d.Value).OrderBy(d => d).ToArray(),
        typeId = e.GetTypeId().Value, typeIsElementType = doc.GetElement(e.GetTypeId()) is ElementType, name, nameError, e.IsTransient, isElementType = e is ElementType,
        viewportViewId = e is Viewport viewport ? (long?)viewport.ViewId.Value : null,
        viewportSheetId = e is Viewport sheetViewport ? (long?)sheetViewport.SheetId.Value : null,
        ownedByStudy = e.OwnerViewId == study.Id, sketchPlaneOfStudy = e is SketchPlane && study.SketchPlane?.Id == e.Id, collectorMembership = all.Contains(id) };
    } catch (Exception error) { return new { id, inspectionError = error.GetType().FullName, error.Message, collectorMembership = all.Contains(id), studyDependent = dependent.Contains(new ElementId(id)) }; }
};
var originalIds = all.Where(id => !known.Contains(id)).OrderBy(id => id).ToArray();
return new { root = describe(study.Id.Value), sourceTypeId = source.GetTypeId().Value, sunSettingsId = study.SunAndShadowSettings.Id.Value, sketchPlaneId = study.SketchPlane?.Id.Value,
    rootDependencies = dependent.Select(i => i.Value).OrderBy(i => i).ToArray(), added = nativeAdded.Select(describe).ToArray(),
    classifiedCollectorIds = known.Where(all.Contains).OrderBy(id => id).ToArray(), originalCollectorCount = originalIds.Length,
    originalCollectorFingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(originalIds)))).ToLowerInvariant(),
    boundary = "Direct GetElement evidence only. Native Added identities outside previously classified collector membership are not accepted or used to exclude original IDs." };
