var expectedA = __A__; var expectedB = __B__;
if (System.Environment.ProcessId != __PID__ || System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture) != __START__
    || !string.Equals(doc.PathName, expectedA, StringComparison.OrdinalIgnoreCase) || !doc.Equals(uiapp.ActiveUIDocument?.Document)
    || doc.IsWorkshared || doc.IsLinked || doc.IsFamilyDocument
    || uiapp.Application.Documents.Cast<Document>().Any(d => d.IsWorkshared || d.IsLinked || d.IsFamilyDocument
        || !(string.Equals(d.PathName, expectedA, StringComparison.OrdinalIgnoreCase) || string.Equals(d.PathName, expectedB, StringComparison.OrdinalIgnoreCase))))
    throw new InvalidOperationException("Electrical qualification identity/context guard failed.");
cancellationToken.ThrowIfCancellationRequested();
Func<object, string> digest = value => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(value)))).ToLowerInvariant();
Func<View, Element[]> annotations = view => new FilteredElementCollector(doc).OwnedByView(view.Id).WhereElementIsNotElementType()
    .Where(e => e.ViewSpecific && (e is Dimension || e is TextNote || e is DetailCurve || e is IndependentTag || e is FamilyInstance || e.Category?.Id.Value == (long)BuiltInCategory.OST_Wire))
    .OrderBy(e => e.Id.Value).ToArray();
Func<Element, object> annotation = e => {
    var point = e.Location as LocationPoint;
    return new { id = e.Id.Value, uid = e.UniqueId, owner = e.OwnerViewId.Value, category = e.Category?.Name, categoryId = e.Category?.Id.Value,
        type = doc.GetElement(e.GetTypeId())?.UniqueId, group = e.GroupId.Value,
        dependencies = e.GetDependentElements(null).Where(id => id != e.Id).Select(id => id.Value).OrderBy(id => id).ToArray(),
        position = point == null ? null : new[] { point.Point.X, point.Point.Y, point.Point.Z }, rotation = point?.Rotation };
};
Func<View, string> settings = view => {
    var plan = view as ViewPlan;
    using var range = plan?.GetViewRange();
    var planes = new[] { PlanViewPlane.TopClipPlane, PlanViewPlane.CutPlane, PlanViewPlane.BottomClipPlane, PlanViewPlane.ViewDepthPlane };
    var categories = new[] { BuiltInCategory.OST_LightingFixtures, BuiltInCategory.OST_LightingDevices, BuiltInCategory.OST_ElectricalFixtures,
        BuiltInCategory.OST_ElectricalEquipment, BuiltInCategory.OST_Wire, BuiltInCategory.OST_GenericAnnotation, BuiltInCategory.OST_Walls };
    return digest(new { view.ViewTemplateId.Value, detail = view.DetailLevel.ToString(), view.Scale,
        cropActive = view.IsTemplate ? (bool?)null : view.CropBoxActive, cropVisible = view.IsTemplate ? (bool?)null : view.CropBoxVisible,
        level = plan?.GenLevel?.UniqueId, baseLevel = plan?.GetUnderlayBaseLevel().Value, topLevel = plan?.GetUnderlayTopLevel().Value,
        phase = view.get_Parameter(BuiltInParameter.VIEW_PHASE)?.AsElementId().Value, phaseFilter = view.get_Parameter(BuiltInParameter.VIEW_PHASE_FILTER)?.AsElementId().Value,
        ranges = range == null ? null : planes.Select(p => new { plane = p.ToString(), level = range.GetLevelId(p).Value, offset = range.GetOffset(p) }).ToArray(),
        categories = categories.Select(c => new { category = c.ToString(), hidden = view.GetCategoryHidden(new ElementId(c)),
            halftone = view.GetCategoryOverrides(new ElementId(c)).Halftone }).ToArray(),
        filters = view.GetFilters().Select(id => new { id = id.Value, visible = view.GetFilterVisibility(id), enabled = view.GetIsFilterEnabled(id) }).ToArray() });
};
long[] selected = [__VIEW_IDS__], templates = [__TEMPLATE_IDS__], schedules = [__SCHEDULE_IDS__], excluded = [__EXCLUDED_IDS__];
var views = selected.Select(id => {
    var v = doc.GetElement(new ElementId(id)) as View ?? throw new InvalidOperationException("Expected canonical native view missing.");
    var owned = annotations(v);
    return new { id, uid = v.UniqueId, v.Name, isTemplate = v.IsTemplate, templateId = v.ViewTemplateId.Value, detail = v.DetailLevel.ToString(), scale = v.Scale,
        settings = settings(v), annotations = owned.GroupBy(e => e.Category?.Name ?? "").ToDictionary(g => g.Key, g => g.Count()), annotationFingerprint = digest(owned.Select(annotation).ToArray()) };
}).ToArray();
var source = (View)doc.GetElement(new ElementId(__SOURCE_VIEW_ID__));
var sourceAnnotations = annotations(source);
if (sourceAnnotations.Length != 2 || sourceAnnotations.Any(e => e is not FamilyInstance || e.Category?.Id.Value != (long)BuiltInCategory.OST_GenericAnnotation
    || e.GroupId != ElementId.InvalidElementId || e.Location is not LocationPoint || e.GetDependentElements(null).Any(id => id != e.Id)))
    throw new InvalidOperationException("Expected exactly two independent point-based source annotation instances; dependencies require separate review.");
var placements = new FilteredElementCollector(doc).OfClass(typeof(ScheduleSheetInstance)).Cast<ScheduleSheetInstance>().Where(p => schedules.Contains(p.ScheduleId.Value)).OrderBy(p => p.Id.Value).ToArray();
var sheetIds = placements.Select(p => p.OwnerViewId.Value).Distinct().ToArray();
if (placements.Length != schedules.Length || sheetIds.Length != 1) throw new InvalidOperationException("Expected schedule placement inventory on one native sheet.");
var sheet = (ViewSheet)doc.GetElement(new ElementId(sheetIds[0]));
Func<ViewSchedule, string[]> footer = schedule => {
    var body = schedule.GetTableData().GetSectionData(SectionType.Body);
    return Enumerable.Range(body.FirstColumnNumber, body.NumberOfColumns).Select(col => schedule.GetCellText(SectionType.Body, body.LastRowNumber, col)).ToArray();
};
var studyUid = __STUDY_UID__;
var study = string.IsNullOrEmpty(studyUid) ? null : doc.GetElement(studyUid) as View;
if (!string.IsNullOrEmpty(studyUid) && study == null) throw new InvalidOperationException("Persisted study view missing.");
var studyAnnotations = study == null ? null : annotations(study).Select(annotation).ToArray();
var all = new FilteredElementCollector(doc).WherePasses(new LogicalOrFilter(new ElementIsElementTypeFilter(), new ElementIsElementTypeFilter(true))).ToElementIds().Select(id => id.Value).OrderBy(id => id).ToArray();
return new {
    elements = all.Length, nonTypeElements = new FilteredElementCollector(doc).WhereElementIsNotElementType().GetElementCount(), idFingerprint = digest(all.Where(id => !excluded.Contains(id)).ToArray()), views,
    templates = templates.Select(id => { var v = doc.GetElement(new ElementId(id)) as View ?? throw new InvalidOperationException("Expected native template missing.");
        return new { id, v.Name, isTemplate = v.IsTemplate, settings = settings(v), exclusions = digest(v.GetNonControlledTemplateParameterIds().Select(p => p.Value).OrderBy(p => p).ToArray()) }; }).ToArray(),
    counts = new[] { __CATEGORY_NAMES__ }.Select(name => { var category = Enum.Parse<BuiltInCategory>(name); return new { category = name, count = new FilteredElementCollector(doc).OfCategory(category).WhereElementIsNotElementType().GetElementCount() }; }).ToArray(),
    wireCount = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Wire).WhereElementIsNotElementType().GetElementCount(),
    sourceL1LightingAnnotationCount = annotations((View)doc.GetElement(new ElementId(__LIGHTING_SOURCE_ID__))).Length,
    schedules = schedules.Select(id => { var s = (ViewSchedule)doc.GetElement(new ElementId(id)); return new { id, s.Name, footer = footer(s) }; }).ToArray(),
    sheet = new { id = sheet.Id.Value, uid = sheet.UniqueId, sheet.SheetNumber, sheet.Name,
        placementFingerprint = digest(new FilteredElementCollector(doc).OwnedByView(sheet.Id).WhereElementIsNotElementType().OrderBy(e => e.Id.Value)
            .Select(e => new { id = e.Id.Value, e.UniqueId, type = e.GetTypeId().Value, location = e.Location is LocationPoint p ? new[] { p.Point.X, p.Point.Y, p.Point.Z } : null }).ToArray()) },
    sourceUid = source.UniqueId, sourceAnnotations = sourceAnnotations.Select(annotation).ToArray(),
    study = study == null ? null : new { id = study.Id.Value, uid = study.UniqueId, study.Name, templateId = study.ViewTemplateId.Value,
        detail = study.DetailLevel.ToString(), scale = study.Scale, settings = settings(study), annotations = studyAnnotations } };

