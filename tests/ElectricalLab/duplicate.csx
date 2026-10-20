var expectedA = __A__; var expectedB = __B__;
if (System.Environment.ProcessId != __PID__ || System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture) != __START__
    || !string.Equals(doc.PathName, expectedA, StringComparison.OrdinalIgnoreCase) || !doc.Equals(uiapp.ActiveUIDocument?.Document)
    || doc.IsWorkshared || doc.IsLinked || doc.IsFamilyDocument
    || uiapp.Application.Documents.Cast<Document>().Any(d => d.IsWorkshared || d.IsLinked || d.IsFamilyDocument
        || !(string.Equals(d.PathName, expectedA, StringComparison.OrdinalIgnoreCase) || string.Equals(d.PathName, expectedB, StringComparison.OrdinalIgnoreCase))))
    throw new InvalidOperationException("Electrical qualification identity/context guard failed.");
cancellationToken.ThrowIfCancellationRequested();
var source = doc.GetElement(new ElementId(__SOURCE_VIEW_ID__)) as ViewPlan ?? throw new InvalidOperationException("Expected power plan missing.");
if (source.IsTemplate || source.ViewTemplateId != ElementId.InvalidElementId || !source.CanViewBeDuplicated(ViewDuplicateOption.Duplicate))
    throw new InvalidOperationException("Source view is not independently duplicable without detailing.");
string name = __QA_NAME__;
if (new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Any(v => v.Name == name))
    throw new InvalidOperationException("Study name already exists; recover the original edit instead of creating another.");
var beforeIds = new FilteredElementCollector(doc).WherePasses(new LogicalOrFilter(new ElementIsElementTypeFilter(), new ElementIsElementTypeFilter(true))).ToElementIds().ToHashSet();
var id = source.Duplicate(ViewDuplicateOption.Duplicate); var study = (ViewPlan)doc.GetElement(id); study.Name = name;
doc.Regenerate();
var owned = new FilteredElementCollector(doc).OwnedByView(id).WhereElementIsNotElementType()
    .Where(e => e.ViewSpecific && (e is Dimension || e is TextNote || e is DetailCurve || e is IndependentTag || e is FamilyInstance || e.Category?.Id.Value == (long)BuiltInCategory.OST_Wire)).ToArray();
if (owned.Length != 0 || study.GenLevel.Id != source.GenLevel.Id || study.ViewTemplateId != ElementId.InvalidElementId)
    throw new InvalidOperationException("New study must be empty, independent and on the same level.");
var dependent = study.GetDependentElements(null).ToHashSet();
var structuralCategories = new HashSet<long>();
foreach (string category in new[] { "OST_Views", "OST_Viewers", "OST_Cameras", "OST_SectionBox", "OST_CropBoundary", "OST_AnnotationCrop" })
    if (Enum.TryParse<BuiltInCategory>(category, out var value)) structuralCategories.Add((long)value);
var companions = new FilteredElementCollector(doc).WherePasses(new LogicalOrFilter(new ElementIsElementTypeFilter(), new ElementIsElementTypeFilter(true)))
    .Where(e => !beforeIds.Contains(e.Id) && e.Id != study.Id).OrderBy(e => e.Id.Value).ToArray();
foreach (var companion in companions)
    if (companion is ElementType || !(companion.OwnerViewId == study.Id || dependent.Contains(companion.Id) || companion is SketchPlane && study.SketchPlane?.Id == companion.Id)
        || !(companion is SketchPlane || companion.GetType() == typeof(Element) && (companion.Category?.CategoryType == CategoryType.Internal || structuralCategories.Contains(companion.Category?.Id.Value ?? 0))))
        throw new InvalidOperationException("Unclassified native duplicate companion; roll back and review this element: " + companion.Id.Value + " / " + companion.GetType().Name + " / " + companion.Category?.Name);
return new { id = id.Value, uid = study.UniqueId, study.Name, annotations = owned.Length, sourceUid = source.UniqueId,
    companions = companions.Select(e => new { id = e.Id.Value, uid = e.UniqueId, kind = e.GetType().FullName, category = e.Category?.Name, owner = e.OwnerViewId.Value, dependent = dependent.Contains(e.Id) }).ToArray(),
    allowedModified = new[] { new { id = source.Id.Value, reason = "source view duplicate bookkeeping" }, new { id = source.GetTypeId().Value, reason = "source view family type duplicate bookkeeping" } } };

