using System.Collections.Immutable;
using System.IO;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitThyme.Core;

namespace RevitThyme.Addin;

internal static class NativeTimberKitExport
{
    internal static ImmutableArray<TimberNativeFile> Run(UIApplication app, Document source, NativeTimberKitDocuments documents,
        TimberNativeKitPlan plan, string directory, string output, string support, CancellationToken ct)
    {
        var expected = NativeTimberKitContours.Expected(plan, directory, ct);
        var reports = new List<object>(); var files = ImmutableArray.CreateBuilder<TimberNativeFile>();
        files.AddRange(documents.ReferenceInputs);
        string template = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Autodesk", "RVT 2027", "Templates", "Default_I_ENU.rte");
        if (!System.IO.File.Exists(template)) throw new FileNotFoundException("Native independent readback needs the installed Imperial project template.", template);
        var originalDocuments = app.Application.Documents.Cast<Document>().ToArray();
        foreach (var item in documents.Cuts)
        {
            ct.ThrowIfCancellationRequested();
            if (!expected.TryGetValue(item.InputFile, out var rings)) throw new InvalidDataException("Cut view is missing its canonical contour inventory.");
            var input = NativeTimberKitReadback.Check(source, item.Import, item.InputFile, rings, "CUT", ct);
            if (source.IsModifiable || !item.View.CanBePrinted) throw new InvalidOperationException("Close inner transactions before native DWG export.");
            string name = Path.GetFileNameWithoutExtension(item.InputFile);
            using var options = new DWGExportOptions { TargetUnit = ExportUnit.Inch, SharedCoords = false, MergedViews = false, PreserveCoincidentLines = true };
            if (!source.Export(output, name, new List<ElementId> { item.View.Id }, options)) throw new InvalidDataException("Native DWG export returned false.");
            string dwg = Path.Combine(output, name + ".dwg"), pcp = Path.Combine(output, name + ".pcp");
            if (!System.IO.File.Exists(dwg)) throw new InvalidDataException("Native DWG exporter did not produce its expected file.");
            if (System.IO.File.Exists(pcp))
            {
                string retained = Path.Combine(support, name + ".pcp");
                System.IO.File.Move(pcp, retained, false); files.Add(NativeTimberKitFiles.File(directory, retained, "ExportSupport"));
            }
            files.Add(NativeTimberKitFiles.File(directory, dwg, name == "CALIBRATION" ? "CalibrationDwg" : "CutDwg"));
            Document? readback = null; NativeTimberKitReadback.FileCheck? consumer = null;
            try
            {
                ct.ThrowIfCancellationRequested(); readback = app.Application.NewProjectDocument(template);
                using var transaction = NativeTimberKitDocuments.StartTransaction(readback, "TimberFold temporary DWG verification");
                var type = new FilteredElementCollector(readback).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>().First(t => t.ViewFamily == ViewFamily.Drafting);
                var view = ViewDrafting.Create(readback, type.Id); view.Scale = 1;
                using var importOptions = new DWGImportOptions { Unit = ImportUnit.Default, Placement = ImportPlacement.Origin,
                    ThisViewOnly = true, OrientToView = false, AutoCorrectAlmostVHLines = false };
                if (!readback.Import(dwg, importOptions, view, out var id) || readback.GetElement(id) is not ImportInstance import)
                    throw new InvalidDataException("Native DWG independent readback import failed.");
                readback.Regenerate(); consumer = NativeTimberKitReadback.Check(readback, import, Path.GetFileName(dwg), rings, "CUT", ct);
                if (transaction.RollBack() != TransactionStatus.RolledBack) throw new InvalidOperationException("Temporary native readback did not roll back.");
            }
            finally
            {
                if (readback is { IsValidObject: true } && !readback.Close(false))
                    throw new InvalidOperationException("Temporary native DWG document did not close unsaved.");
            }
            if (!source.Equals(app.ActiveUIDocument?.Document) || originalDocuments.Length != app.Application.Documents.Size ||
                originalDocuments.Any(d => !app.Application.Documents.Cast<Document>().Any(d.Equals)))
                throw new InvalidDataException("Temporary readback changed the original active/open documents.");
            reports.Add(new { canonicalInput = input, nativeDwg = consumer, units = "Inch; readback consumes saved units", temporaryDocumentClosedUnsaved = true });
        }
        var expectedDwgs = documents.Cuts.Select(v => Path.GetFileNameWithoutExtension(v.InputFile) + ".dwg").Order(StringComparer.Ordinal);
        if (!Directory.GetFileSystemEntries(output).Select(Path.GetFileName).Order(StringComparer.Ordinal).SequenceEqual(expectedDwgs) ||
            !Directory.GetFileSystemEntries(support).Select(Path.GetFileName).Order(StringComparer.Ordinal)
                .SequenceEqual(files.Where(f => f.Role == "ExportSupport").Select(f => Path.GetFileName(f.RelativePath)).Order(StringComparer.Ordinal)))
            throw new InvalidDataException("Native exporter produced unexpected files or references.");
        string report = Path.Combine(directory, "native-readback.json");
        NativeTimberKitFiles.WriteNew(report, new { schema = 1, plan.RunId, passed = true, reports, referenceDiagrams = documents.References,
            referenceMultiplier = NativeTimberKitReferenceTransform.Multiplier, referenceInputs = documents.ReferenceMappings,
            referencePhysicalToleranceInches = .00001 / NativeTimberKitReferenceTransform.Multiplier, physicalChecksOutstanding = true });
        files.Add(NativeTimberKitFiles.File(directory, report, "NativeVerification"));
        return files.ToImmutable();
    }
}
