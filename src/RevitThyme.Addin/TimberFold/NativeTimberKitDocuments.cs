using System.IO;
using Autodesk.Revit.DB;
using RevitThyme.Core;

namespace RevitThyme.Addin;

internal sealed class NativeTimberKitDocuments(Document document, TimberNativeKitPlan plan, string directory)
{
    internal sealed record CutView(string InputFile, ViewDrafting View, ImportInstance Import);
    internal sealed record ReferenceMapping(string PhysicalGuideFile, string NativeRelativePath, string PhysicalGuideSha256,
        string NativeInputSha256, int CoordinateMultiplier, long ViewId, string ViewUniqueId, long ImportId, string ImportUniqueId);
    internal List<CutView> Cuts { get; } = [];
    internal List<TimberNativeElement> Elements { get; } = [];
    internal List<NativeTimberKitReference.CheckResult> References { get; } = [];
    internal List<ReferenceMapping> ReferenceMappings { get; } = [];
    internal List<TimberNativeFile> ReferenceInputs { get; } = [];
    private readonly Dictionary<double, ElementId> textTypes = [];
    private readonly List<(TextNote Note, TimberGuideLabel Label, TimberPoint3 Position)> labels = [];
    private readonly List<(ViewSheet Sheet, Viewport Viewport, ViewDrafting View)> sheets = [];
    private readonly string prefix = "TF-" + plan.RunId.ToString("N")[..12];

    internal void Create(CancellationToken ct)
    {
        using var transaction = Transaction("TimberFold review documentation");
        foreach (var file in plan.CutFiles.Where(f => f.Name.EndsWith(".dxf", StringComparison.Ordinal)).OrderBy(f => f.Name, StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            var view = Drafting("CUT " + Path.GetFileNameWithoutExtension(file.Name));
            Add(view, "CutView", file.Sha256);
            var import = Import(view, Path.Combine(directory, "cutting", file.Name)); Add(import, "CutImport", file.Sha256);
            Cuts.Add(new(file.Name, view, import));
        }
        int index = 0;
        foreach (var drawing in plan.Guides.Drawings)
        {
            ct.ThrowIfCancellationRequested();
            var file = plan.Guides.Files.Single(f => f.Name == drawing.File);
            var native = NativeTimberKitReferenceTransform.Prepare(System.IO.File.ReadAllBytes(Path.Combine(directory, "guides", drawing.File)),
                plan.Guides.Labels.Where(l => l.GuideFile == drawing.File).ToArray(), ct);
            string nativePath = Path.Combine(directory, "native-reference-inputs", drawing.File);
            using (var stream = new FileStream(nativePath, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            { stream.Write(native.Dxf.AsSpan()); stream.Flush(true); }
            var nativeFile = NativeTimberKitFiles.File(directory, nativePath, "NativeReferenceInput"); ReferenceInputs.Add(nativeFile);
            var view = Drafting("REFERENCE " + (++index).ToString("000", System.Globalization.CultureInfo.InvariantCulture));
            view.Scale = native.ViewScale;
            Add(view, "ReferenceView", nativeFile.Sha256);
            var import = Import(view, nativePath); Add(import, "ReferenceImport", nativeFile.Sha256);
            ReferenceMappings.Add(new(drawing.File, nativeFile.RelativePath, file.Sha256, nativeFile.Sha256, native.CoordinateMultiplier,
                view.Id.Value, view.UniqueId, import.Id.Value, import.UniqueId));
            document.Regenerate();
            References.Add(NativeTimberKitReference.Check(document, import, nativePath, ct));
            foreach (var preparedLabel in native.Labels)
            {
                ct.ThrowIfCancellationRequested();
                var label = preparedLabel.Source;
                var note = TextNote.Create(document, view.Id,
                    new XYZ(preparedLabel.PositionFeet.X, preparedLabel.PositionFeet.Y, preparedLabel.PositionFeet.Z),
                    label.Text, TextType(label.TextHeightInches)) ?? throw new InvalidOperationException("Native part label creation returned null.");
                Add(note, label.PanelId is null ? "GuideLabel" : "PartLabel", NativeTimberGeometry.Hash(label), label.PanelId);
                labels.Add((note, label, preparedLabel.PositionFeet));
            }
            var sheet = ViewSheet.Create(document, ElementId.InvalidElementId) ?? throw new InvalidOperationException("Native sheet creation returned null.");
            sheet.Name = drawing.Title; sheet.SheetNumber = prefix + "-" + index.ToString("000", System.Globalization.CultureInfo.InvariantCulture);
            Add(sheet, "NativeSheet", file.Sha256);
            document.Regenerate();
            if (!Viewport.CanAddViewToSheet(document, sheet.Id, view.Id)) throw new InvalidOperationException("Native reference view cannot be placed on its sheet.");
            var center = new XYZ((drawing.MaximumInches.X - drawing.MinimumInches.X) / 24 + 0.5,
                (drawing.MaximumInches.Y - drawing.MinimumInches.Y) / 24 + 0.5, 0);
            var viewport = Viewport.Create(document, sheet.Id, view.Id, center) ?? throw new InvalidOperationException("Native viewport creation returned null.");
            Add(viewport, "Viewport", file.Sha256); sheets.Add((sheet, viewport, view));
        }
        document.Regenerate();
        if (transaction.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Native documentation transaction did not commit.");
        Verify(ct);
    }
    internal void AuditNew(long[] before, CancellationToken ct)
    {
        var beforeSet = before.ToHashSet();
        var ids = NativeTimberKitPreservation.Ids(document);
        if (before.Any(id => !ids.Contains(id))) throw new InvalidDataException("Native documentation removed an existing element.");
        var recorded = Elements.Select(e => e.Id).ToHashSet();
        foreach (long id in ids.Where(id => !beforeSet.Contains(id)))
        {
            ct.ThrowIfCancellationRequested();
            if (recorded.Contains(id)) continue;
            var e = document.GetElement(new ElementId(id));
            if (e is View or ImportInstance or TextNote or DirectShape or CurveElement or FamilyInstance or HostObject)
                throw new InvalidDataException("Unaccounted native geometry/documentation was created: " + id);
            Elements.Add(new(id, e.UniqueId, e is Material or GraphicsStyle or CADLinkType ? "CadResource" : "DocumentationResource"));
        }
        if (Elements.Select(e => e.Id).Distinct().Count() != Elements.Count || Elements.Any(e => beforeSet.Contains(e.Id)))
            throw new InvalidDataException("Native created-element ownership is ambiguous.");
    }
    internal void Verify(CancellationToken ct)
    {
        if (!Directory.GetFileSystemEntries(Path.Combine(directory, "native-reference-inputs")).Select(Path.GetFileName).Order(StringComparer.Ordinal)
            .SequenceEqual(ReferenceMappings.Select(m => m.PhysicalGuideFile).Order(StringComparer.Ordinal)))
            throw new InvalidDataException("Derived native reference input inventory differs from its exact guide mapping.");
        foreach (var item in Cuts)
        {
            ct.ThrowIfCancellationRequested();
            if (!item.View.CanBePrinted || item.View.Scale != 1 || !item.Import.ViewSpecific || item.Import.OwnerViewId != item.View.Id)
                throw new InvalidDataException("Cut-only native view/import ownership or physical scale is invalid.");
            if (new FilteredElementCollector(document, item.View.Id).OfClass(typeof(TextNote)).Any())
                throw new InvalidDataException("Cut-only view contains labels.");
        }
        foreach (var mapping in ReferenceMappings)
        {
            ct.ThrowIfCancellationRequested();
            if (document.GetElement(new ElementId(mapping.ViewId)) is not ViewDrafting { Scale: NativeTimberKitReferenceTransform.Multiplier } view ||
                view.UniqueId != mapping.ViewUniqueId || document.GetElement(new ElementId(mapping.ImportId)) is not ImportInstance { ViewSpecific: true } import ||
                import.UniqueId != mapping.ImportUniqueId || import.OwnerViewId != view.Id ||
                NativeTimberKitFiles.Hash(Path.Combine(directory, mapping.NativeRelativePath)) != mapping.NativeInputSha256 ||
                NativeTimberKitFiles.Hash(Path.Combine(directory, "guides", mapping.PhysicalGuideFile)) != mapping.PhysicalGuideSha256)
                throw new InvalidDataException("Native reference scale, input mapping or source guide hashes differ from the prepared plan.");
        }
        foreach (var (note, label, position) in labels)
        {
            ct.ThrowIfCancellationRequested();
            var owner = ReferenceMappings.Single(m => m.PhysicalGuideFile == label.GuideFile);
            if (Normalize(note.Text) != Normalize(label.Text) || !note.ViewSpecific || note.OwnerViewId.Value != owner.ViewId ||
                note.Coord.DistanceTo(new XYZ(position.X, position.Y, position.Z)) * 12 / NativeTimberKitReferenceTransform.Multiplier > .00001 ||
                Math.Abs(((TextNoteType)document.GetElement(note.GetTypeId())).get_Parameter(BuiltInParameter.TEXT_SIZE).AsDouble() * 12 - label.TextHeightInches) > .0000001)
                throw new InvalidDataException("Native guide/part label differs from the verified label plan.");
        }
        foreach (var (sheet, viewport, view) in sheets)
        {
            ct.ThrowIfCancellationRequested();
            if (viewport.SheetId != sheet.Id || viewport.ViewId != view.Id || !sheet.GetAllViewports().Contains(viewport.Id) ||
                new FilteredElementCollector(document, sheet.Id).OfCategory(BuiltInCategory.OST_TitleBlocks).Any())
                throw new InvalidDataException("Native sheet/viewport mapping differs from the documentation plan.");
        }
        if (labels.Count != plan.Guides.Labels.Length || sheets.Count != plan.Guides.Drawings.Length || ReferenceMappings.Count != plan.Guides.Drawings.Length || Cuts.Count != plan.Stock.SheetCount + 1)
            throw new InvalidDataException("Native documentation lost a cutting view, reference view or label.");
        static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').TrimEnd('\n');
    }
    private ElementId TextType(double height)
    {
        if (textTypes.TryGetValue(height, out var id)) return id;
        var original = (TextNoteType)document.GetElement(document.GetDefaultElementTypeId(ElementTypeGroup.TextNoteType));
        var type = (TextNoteType)original.Duplicate(prefix + " Text " + textTypes.Count);
        if (!type.get_Parameter(BuiltInParameter.TEXT_SIZE).Set(height / 12)) throw new InvalidOperationException("Native guide text size could not be set.");
        Add(type, "TextType", NativeTimberGeometry.Hash(new { height })); textTypes.Add(height, type.Id); return type.Id;
    }
    private ViewDrafting Drafting(string name)
    {
        var type = new FilteredElementCollector(document).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>().First(t => t.ViewFamily == ViewFamily.Drafting);
        var view = ViewDrafting.Create(document, type.Id); view.Name = prefix + " " + name; view.Scale = 1; return view;
    }
    private ImportInstance Import(View view, string path)
    {
        using var options = new DWGImportOptions { Unit = ImportUnit.Default, Placement = ImportPlacement.Origin,
            ThisViewOnly = true, OrientToView = false, AutoCorrectAlmostVHLines = false };
        if (!document.Import(path, options, view, out var id) || document.GetElement(id) is not ImportInstance imported ||
            !imported.ViewSpecific || imported.OwnerViewId != view.Id) throw new InvalidDataException("Native view-only CAD import failed.");
        return imported;
    }
    private void Add(Element element, string role, string hash, string? panel = null)
    {
        NativeTimberKitOwnership.TagElement(element, plan, role, hash);
        Elements.Add(new(element.Id.Value, element.UniqueId, role, panel));
    }
    internal Transaction Transaction(string name)
        => StartTransaction(document, name);
    internal static Transaction StartTransaction(Document document, string name)
    {
        var transaction = new Transaction(document, name);
        if (transaction.Start() != TransactionStatus.Started) { transaction.Dispose(); throw new InvalidOperationException("Native transaction did not start."); }
        transaction.SetFailureHandlingOptions(transaction.GetFailureHandlingOptions().SetClearAfterRollback(true)
            .SetForcedModalHandling(true).SetFailuresPreprocessor(new FailClosed()));
        return transaction;
    }
    private sealed class FailClosed : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor failures) => failures.GetFailureMessages().Count > 0
            ? FailureProcessingResult.ProceedWithRollBack : FailureProcessingResult.Continue;
    }
}
