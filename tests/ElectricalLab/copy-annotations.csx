var expectedA = __A__; var expectedB = __B__;
if (System.Environment.ProcessId != __PID__ || System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture) != __START__
    || !string.Equals(doc.PathName, expectedA, StringComparison.OrdinalIgnoreCase) || !doc.Equals(uiapp.ActiveUIDocument?.Document)
    || doc.IsWorkshared || doc.IsLinked || doc.IsFamilyDocument
    || uiapp.Application.Documents.Cast<Document>().Any(d => d.IsWorkshared || d.IsLinked || d.IsFamilyDocument
        || !(string.Equals(d.PathName, expectedA, StringComparison.OrdinalIgnoreCase) || string.Equals(d.PathName, expectedB, StringComparison.OrdinalIgnoreCase))))
    throw new InvalidOperationException("Electrical qualification identity/context guard failed.");
cancellationToken.ThrowIfCancellationRequested();
var source = doc.GetElement(new ElementId(__SOURCE_VIEW_ID__)) as ViewPlan ?? throw new InvalidOperationException("Expected source power view missing.");
var study = doc.GetElement(__STUDY_UID__) as ViewPlan ?? throw new InvalidOperationException("Expected owned study missing.");
if (study.Name != __QA_NAME__ || study.IsTemplate || study.GenLevel.Id != source.GenLevel.Id || study.ViewTemplateId != ElementId.InvalidElementId)
    throw new InvalidOperationException("Study identity or native context changed.");
Func<View, Element[]> annotations = view => new FilteredElementCollector(doc).OwnedByView(view.Id).WhereElementIsNotElementType()
    .Where(e => e.ViewSpecific && (e is Dimension || e is TextNote || e is DetailCurve || e is IndependentTag || e is FamilyInstance || e.Category?.Id.Value == (long)BuiltInCategory.OST_Wire)).ToArray();
string[] expectedUids = [__ANNOTATION_UIDS__];
var originals = expectedUids.Select(uid => doc.GetElement(uid) ?? throw new InvalidOperationException("Source annotation missing.")).ToArray();
if (originals.Length != 2 || annotations(source).Length != 2 || annotations(study).Length != 0
    || originals.Any(e => e.OwnerViewId != source.Id || !e.ViewSpecific || e is not FamilyInstance || e.Location is not LocationPoint
        || e.Category?.Id.Value != (long)BuiltInCategory.OST_GenericAnnotation || e.GroupId != ElementId.InvalidElementId || e.GetDependentElements(null).Any(id => id != e.Id)))
    throw new InvalidOperationException("Exact independent annotation copy precondition failed.");
var copied = ElementTransformUtils.CopyElements(source, originals.Select(e => e.Id).ToList(), study, Transform.Identity, new CopyPasteOptions());
doc.Regenerate(); var after = annotations(study);
if (copied.Count != 2 || after.Length != 2 || after.Any(e => e.OwnerViewId != study.Id || e.Location is not LocationPoint))
    throw new InvalidOperationException("Expected exactly two copied study annotations.");
foreach (var original in originals) {
    var point = (LocationPoint)original.Location;
    if (after.Count(e => e.GetTypeId() == original.GetTypeId() && ((LocationPoint)e.Location).Point.DistanceTo(point.Point) < 1e-9
        && Math.Abs(((LocationPoint)e.Location).Rotation - point.Rotation) < 1e-9) != 1)
        throw new InvalidOperationException("Copied annotation type/position/rotation differs from source.");
}
return new { studyUid = study.UniqueId, copied = after.OrderBy(e => e.Id.Value).Select(e => new { id = e.Id.Value, uid = e.UniqueId,
    type = doc.GetElement(e.GetTypeId()).UniqueId, owner = e.OwnerViewId.Value,
    position = new[] { ((LocationPoint)e.Location).Point.X, ((LocationPoint)e.Location).Point.Y, ((LocationPoint)e.Location).Point.Z },
    rotation = ((LocationPoint)e.Location).Rotation }).ToArray(),
    allowedModified = new[] { new { id = study.Id.Value, reason = "destination view annotation membership" } }
        .Concat(originals.Select(e => new { id = e.GetTypeId().Value, reason = "exact source annotation type copy bookkeeping" })).Distinct().ToArray() };

