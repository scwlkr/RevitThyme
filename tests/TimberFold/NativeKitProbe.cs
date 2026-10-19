using System.Security.Cryptography;
using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitThyme.TimberFold.Fixtures;

// Test-only capability probe. All native writes belong to NEW unsaved documents.
public static class NativeKitProbe
{
    private sealed record Segment(double[] A, double[] B);
    private sealed record Geometry(Segment[] Segments, string[] Layers, int ClosedPolylines);
    private const double Tolerance = .00001;

    public static object Check(UIApplication uiapp, string cutDirectory, string destination, CancellationToken ct)
    {
        var source = uiapp.ActiveUIDocument?.Document ?? throw new InvalidOperationException("Pinned source required.");
        var pinned = Environment.GetEnvironmentVariable("REVITTHYME_FOUNDATION_FIXTURE");
        if (Environment.GetEnvironmentVariable("REVITTHYME_TRUSTED_CODE") != "1" || pinned is null ||
            !string.Equals(Path.GetFullPath(source.PathName), Path.GetFullPath(pinned), StringComparison.OrdinalIgnoreCase) ||
            source.IsWorkshared || uiapp.Application.Documents.Size != 1)
            throw new InvalidOperationException("Exactly one pinned disposable source document is required.");
        if (!Path.IsPathFullyQualified(destination) || Directory.Exists(destination) || File.Exists(destination) ||
            !Path.IsPathFullyQualified(cutDirectory)) throw new InvalidDataException("Use absolute inputs and a fresh destination.");
        string[] files = Directory.GetFiles(cutDirectory, "*.dxf").Order(StringComparer.Ordinal).ToArray();
        if (files.Length != 2 || !files.Any(f => Path.GetFileName(f) == "CALIBRATION.dxf"))
            throw new InvalidDataException("Probe requires exactly the qualified gable stock and calibration DXFs.");
        var sourceIds = Inventory(source); bool modified = source.IsModified; long viewId = source.ActiveView.Id.Value;
        string sourceHash = Hash(source.PathName);
        Directory.CreateDirectory(destination);
        var reports = new List<object>(); var closeResults = new List<bool>(); Document? project = null, readback = null;
        Exception? failure = null; string stage = "preflight";
        string template = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Autodesk", "RVT 2027", "Templates", "Default_I_ENU.rte");
        double shortCurve = uiapp.Application.ShortCurveTolerance, vertex = uiapp.Application.VertexTolerance;
        bool dwgAvailable = OptionalFunctionalityUtils.IsDWGExportAvailable();
        try
        {
            ct.ThrowIfCancellationRequested();
            if (!dwgAvailable) throw new InvalidOperationException("Native DWG export module is unavailable.");
            project = uiapp.Application.NewProjectDocument(template);
            var before = Inventory(project);
            using (var group = new TransactionGroup(project, "Temporary native kit capability probe"))
            {
                if (group.Start() != TransactionStatus.Started) throw new InvalidOperationException("Group did not start.");
                var views = new List<(string File, ViewDrafting View, Geometry Geometry)>();
                using (var transaction = Start(project, "Create annotation-only kit probe"))
                {
                    foreach (string file in files)
                    {
                        ct.ThrowIfCancellationRequested();
                        var view = Drafting(project); view.Name = "TF Probe " + Path.GetFileNameWithoutExtension(file);
                        var imported = Import(project, view, file);
                        project.Regenerate(); var geometry = Read(project, imported, ct);
                        if (geometry.Segments.Length == 0) throw new InvalidDataException("Imported DXF is empty.");
                        views.Add((file, view, geometry));
                        reports.Add(new { stage = "dxf-import", file, viewId = view.Id.Value, view.UniqueId,
                            importId = imported.Id.Value, importUniqueId = imported.UniqueId, imported.ViewSpecific,
                            ownerViewId = imported.OwnerViewId.Value, geometry });
                        if (!imported.ViewSpecific || imported.OwnerViewId != view.Id)
                            throw new InvalidDataException("CAD import is not owned by its drafting view.");
                    }
                    stage = "reference-view";
                    var reference = Drafting(project); reference.Name = "TF Probe Assembly reference";
                    stage = "native-text";
                    var text = TextNote.Create(project, reference.Id, new XYZ(0, 1, 0), "TF-P001 — assembly reference only",
                        project.GetDefaultElementTypeId(ElementTypeGroup.TextNoteType)) ?? throw new InvalidOperationException("TextNote.Create returned null.");
                    stage = "native-sheet";
                    var sheet = ViewSheet.Create(project, ElementId.InvalidElementId) ?? throw new InvalidOperationException("ViewSheet.Create returned null.");
                    sheet.Name = "TF Probe Kit"; sheet.SheetNumber = "TF-PROBE"; project.Regenerate();
                    stage = "native-viewport";
                    if (!Viewport.CanAddViewToSheet(project, sheet.Id, reference.Id)) throw new InvalidOperationException("Reference cannot be placed on sheet.");
                    var viewport = Viewport.Create(project, sheet.Id, reference.Id, new XYZ(1, 1, 0))
                        ?? throw new InvalidOperationException("Viewport.Create returned null.");
                    stage = "annotation-readback";
                    reports.Add(new { stage = "native-annotation", referenceId = reference.Id.Value, textId = text.Id.Value,
                        text.Text, sheetId = sheet.Id.Value, viewportId = viewport.Id.Value, sheet.SheetNumber,
                        titleBlockCount = new FilteredElementCollector(project, sheet.Id).OfCategory(BuiltInCategory.OST_TitleBlocks).GetElementCount() });
                    stage = "annotation-commit"; Commit(transaction);
                }
                stage = "resource-inventory";
                reports.Add(new { stage = "introduced-elements", elements = Inventory(project).Except(before).Select(id =>
                { var e = project.GetElement(new ElementId(id)); return new { id, e.UniqueId, e.Name, type = e.GetType().Name, category = e.Category?.Name }; }).ToArray() });
                foreach (var item in views)
                {
                    ct.ThrowIfCancellationRequested(); stage = "dwg-export";
                    if (!item.View.CanBePrinted || project.IsModifiable) throw new InvalidOperationException("Export requires printable view and closed inner transaction.");
                    using var options = new DWGExportOptions { TargetUnit = ExportUnit.Inch, SharedCoords = false, MergedViews = false };
                    string name = Path.GetFileNameWithoutExtension(item.File);
                    bool exported = project.Export(destination, name, new List<ElementId> { item.View.Id }, options);
                    string path = Path.Combine(destination, name + ".dwg");
                    reports.Add(new { stage = "dwg-export", exported, groupStatus = group.GetStatus().ToString(),
                        project.IsModifiable, item.View.CanBePrinted, path, targetUnit = options.TargetUnit.ToString() });
                    if (!exported || !File.Exists(path)) throw new InvalidDataException("DWG export did not produce expected file.");
                    stage = "dwg-readback-document"; readback = uiapp.Application.NewProjectDocument(template);
                    using (var transaction = Start(readback, "Temporary DWG readback"))
                    {
                        stage = "dwg-readback-import"; var view = Drafting(readback); var imported = Import(readback, view, path);
                        readback.Regenerate(); var geometry = Read(readback, imported, ct);
                        bool equivalent = Covered(item.Geometry.Segments, geometry.Segments, ct) && Covered(geometry.Segments, item.Geometry.Segments, ct);
                        double beforeLength = item.Geometry.Segments.Sum(Length), afterLength = geometry.Segments.Sum(Length);
                        bool lengthPassed = Math.Abs(beforeLength - afterLength) <= Tolerance * (item.Geometry.Segments.Length + geometry.Segments.Length);
                        reports.Add(new { stage = "dwg-readback", path, sha256 = Hash(path), importUnit = "Default: saved file units",
                            equivalent, lengthPassed, beforeLengthInches = beforeLength, afterLengthInches = afterLength,
                            beforeGeometry = item.Geometry, afterGeometry = geometry });
                        if (!equivalent || !lengthPassed) throw new InvalidDataException("DWG native consumer scale/coverage differs from qualified DXF.");
                        if (transaction.RollBack() != TransactionStatus.RolledBack) throw new InvalidOperationException("Readback rollback failed.");
                    }
                    Close(ref readback, closeResults);
                }
                if (group.RollBack() != TransactionStatus.RolledBack || !before.SequenceEqual(Inventory(project)))
                    throw new InvalidOperationException("Probe group did not restore new document inventory.");
                reports.Add(new { stage = "group-rollback", passed = true, undoQualified = false });
            }
        }
        catch (Exception error) { failure = new InvalidOperationException("Probe stage: " + stage, error); }
        finally
        {
            try { Close(ref readback, closeResults); } catch (Exception error) { failure ??= error; }
            try { Close(ref project, closeResults); } catch (Exception error) { failure ??= error; }
        }
        var afterIds = Inventory(source);
        var preservation = new { sameDocument = source.Equals(uiapp.ActiveUIDocument?.Document), beforeModified = modified,
            afterModified = source.IsModified, originalViewId = viewId, afterViewId = source.ActiveView.Id.Value,
            documentCount = uiapp.Application.Documents.Size, beforeFileHash = sourceHash, afterFileHash = Hash(source.PathName),
            addedIds = afterIds.Except(sourceIds).ToArray(), removedIds = sourceIds.Except(afterIds).ToArray() };
        bool sourcePreserved = preservation.sameDocument && modified == source.IsModified && viewId == source.ActiveView.Id.Value &&
            preservation.documentCount == 1 && preservation.beforeFileHash == preservation.afterFileHash && sourceIds.SequenceEqual(afterIds);
        var result = new { passed = failure is null && sourcePreserved && closeResults.All(v => v), error = failure?.ToString(),
            shortCurveToleranceFeet = shortCurve, shortCurveToleranceInches = shortCurve * 12, vertexToleranceFeet = vertex,
            vertexToleranceInches = vertex * 12, dwgAvailable, sourcePreserved, preservation, closeResults, reports,
            files = Directory.GetFiles(destination).Select(p => new { path = p, sha256 = Hash(p) }).ToArray(),
            nativeKitGenerationQualified = false, oneUndoQualified = false, physicalChecksOutstanding = true };
        string reportPath = Path.Combine(destination, "native-kit-probe.json");
        using (var stream = new FileStream(reportPath, FileMode.CreateNew))
        { JsonSerializer.Serialize(stream, result, new JsonSerializerOptions { WriteIndented = true }); stream.Flush(true); }
        return new { result.passed, result.error, result.sourcePreserved, reportPath, reportSha256 = Hash(reportPath),
            shortCurveToleranceInches = shortCurve * 12, vertexToleranceInches = vertex * 12, dwgAvailable,
            closedTemporaryDocuments = closeResults.Count, nativeKitGenerationQualified = false, oneUndoQualified = false };
    }

    private static Transaction Start(Document doc, string name)
    {
        var transaction = new Transaction(doc, name);
        if (transaction.Start() != TransactionStatus.Started) { transaction.Dispose(); throw new InvalidOperationException("Transaction did not start."); }
        return transaction;
    }
    private static void Commit(Transaction transaction)
    { if (transaction.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Transaction did not commit."); }
    private static ViewDrafting Drafting(Document doc)
    {
        var type = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>().First(t => t.ViewFamily == ViewFamily.Drafting);
        var view = ViewDrafting.Create(doc, type.Id); view.Scale = 1; return view;
    }
    private static ImportInstance Import(Document doc, View view, string path)
    {
        using var options = new DWGImportOptions { Unit = ImportUnit.Default, Placement = ImportPlacement.Origin,
            ThisViewOnly = true, OrientToView = false, AutoCorrectAlmostVHLines = false };
        if (!doc.Import(path, options, view, out var id) || doc.GetElement(id) is not ImportInstance imported)
            throw new InvalidDataException("Native import failed: " + path);
        return imported;
    }
    private static Geometry Read(Document doc, ImportInstance imported, CancellationToken ct)
    {
        var segments = new List<Segment>(); var layers = new HashSet<string>(StringComparer.Ordinal); int closed = 0;
        Walk(imported.get_Geometry(new Options())); return new(segments.ToArray(), layers.Order(StringComparer.Ordinal).ToArray(), closed);
        void Walk(GeometryElement geometry)
        {
            foreach (var item in geometry)
            {
                ct.ThrowIfCancellationRequested();
                if (item is GeometryInstance instance) { Walk(instance.GetInstanceGeometry()); continue; }
                layers.Add((doc.GetElement(item.GraphicsStyleId) as GraphicsStyle)?.GraphicsStyleCategory.Name ?? "unresolved");
                if (item is PolyLine polyline)
                {
                    var points = polyline.GetCoordinates(); if (points[0].DistanceTo(points[^1]) * 12 <= Tolerance) closed++;
                    for (int i = 1; i < points.Count; i++) segments.Add(new(Convert(points[i - 1]), Convert(points[i])));
                }
                else if (item is Line line) segments.Add(new(Convert(line.GetEndPoint(0)), Convert(line.GetEndPoint(1))));
                else throw new InvalidDataException("Unexpected native CAD geometry: " + item.GetType().Name);
            }
        }
        static double[] Convert(XYZ p) => [p.X * 12, p.Y * 12, p.Z * 12];
    }
    private static bool Covered(Segment[] source, Segment[] target, CancellationToken ct)
    {
        if (source.Length == 0 || target.Length == 0) return false;
        foreach (var s in source)
        {
            ct.ThrowIfCancellationRequested(); double dx = s.B[0] - s.A[0], dy = s.B[1] - s.A[1], length = Length(s);
            if (length <= 1e-12 || Math.Abs(s.A[2]) > Tolerance / 2 || Math.Abs(s.B[2]) > Tolerance / 2) return false;
            var intervals = new List<(double Low, double High)>();
            foreach (var t in target)
            {
                double a = Project(t.A), b = Project(t.B);
                if (Distance(t.A, a) <= Tolerance / 2 && Distance(t.B, b) <= Tolerance / 2)
                    intervals.Add((Math.Max(0, Math.Min(a, b)), Math.Min(length, Math.Max(a, b))));
            }
            double covered = 0;
            foreach (var interval in intervals.OrderBy(i => i.Low))
            { if (interval.High < 0 || interval.Low > length) continue; if (interval.Low > covered + Tolerance / 2) return false; covered = Math.Max(covered, interval.High); }
            if (covered + Tolerance / 2 < length) return false;
            double Project(double[] p) => ((p[0] - s.A[0]) * dx + (p[1] - s.A[1]) * dy) / length;
            double Distance(double[] p, double d) => Math.Sqrt(Math.Pow(p[0] - s.A[0] - dx * d / length, 2) + Math.Pow(p[1] - s.A[1] - dy * d / length, 2));
        }
        return true;
    }
    private static double Length(Segment s) => Math.Sqrt(Math.Pow(s.B[0] - s.A[0], 2) + Math.Pow(s.B[1] - s.A[1], 2));
    private static long[] Inventory(Document doc) => new FilteredElementCollector(doc).WhereElementIsNotElementType().ToElementIds()
        .Concat(new FilteredElementCollector(doc).WhereElementIsElementType().ToElementIds()).Select(id => id.Value).Order().ToArray();
    private static void Close(ref Document? doc, List<bool> results)
    {
        if (doc is not { IsValidObject: true }) { doc = null; return; }
        bool closed = doc.Close(false); results.Add(closed);
        if (!closed) throw new InvalidOperationException("Temporary document did not close unsaved.");
        doc = null;
    }
    private static string Hash(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}
