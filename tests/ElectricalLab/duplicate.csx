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
var roles = new Dictionary<long, string>();
var sun = study.SunAndShadowSettings;
foreach (var companion in companions)
{
    bool generic = companion.GetType() == typeof(Element), ownedByStudy = companion.OwnerViewId == study.Id, isDependent = dependent.Contains(companion.Id);
    bool invalidType = companion.GetTypeId() == ElementId.InvalidElementId; string? role = null;
    if (companion is not ElementType && !companion.IsTransient)
    {
        if (companion is SketchPlane && study.SketchPlane?.Id == companion.Id)
            role = "exact study sketch plane";
        else if (companion is SketchPlane && ownedByStudy && isDependent)
            role = "owned dependent sketch plane";
        else if (generic && companion.Category == null && isDependent && companion.OwnerViewId == ElementId.InvalidElementId && !companion.ViewSpecific
            && companion.Name == study.Name && (invalidType || companion.GetTypeId() == source.GetTypeId()))
            role = "named dependent view structure";
        else if (generic && companion.Category == null && ownedByStudy && isDependent && companion.ViewSpecific && invalidType && companion.Name == "ExtentElem")
            role = "owned dependent view extent";
        else if (companion is SunAndShadowSettings && companion.Id == sun.Id && ownedByStudy && isDependent && companion.ViewSpecific && invalidType)
            role = "exact study sun and shadow settings";
        else if (generic && sun.Category != null && companion.Category?.Id == sun.Category.Id && companion.Category?.CategoryType == CategoryType.Model
            && ownedByStudy && isDependent && companion.ViewSpecific && invalidType)
            role = "owned dependent sun path structure";
        else if (generic && companion.Category?.Id.Value == (long)BuiltInCategory.OST_IOSSketchGrid && companion.Category?.CategoryType == CategoryType.Internal
            && isDependent && companion.OwnerViewId == ElementId.InvalidElementId && !companion.ViewSpecific && invalidType)
            role = "dependent internal work plane grid";
        else if (generic && structuralCategories.Contains(companion.Category?.Id.Value ?? 0) && (ownedByStudy || isDependent))
            role = "owned or dependent explicit view structural category";
    }
    if (role == null)
        throw new InvalidOperationException("Unclassified native duplicate companion; roll back and review this element: " + companion.Id.Value + " / " + companion.GetType().Name + " / " + companion.Category?.Name);
    roles.Add(companion.Id.Value, role);
}
return new { id = id.Value, uid = study.UniqueId, study.Name, annotations = owned.Length, sourceUid = source.UniqueId,
    companions = companions.Select(e => new { id = e.Id.Value, uid = e.UniqueId, role = roles[e.Id.Value], kind = e.GetType().FullName,
        category = e.Category?.Name, categoryId = e.Category?.Id.Value, categoryType = e.Category?.CategoryType.ToString(),
        owner = e.OwnerViewId.Value, dependent = dependent.Contains(e.Id), e.ViewSpecific, typeId = e.GetTypeId().Value, e.Name, e.IsTransient }).ToArray(),
    allowedModified = new[] { new { id = source.Id.Value, reason = "source view duplicate bookkeeping" }, new { id = source.GetTypeId().Value, reason = "source view family type duplicate bookkeeping" } } };

