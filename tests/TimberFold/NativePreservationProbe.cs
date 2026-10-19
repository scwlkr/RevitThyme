using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using RevitThyme.Addin;

namespace RevitThyme.TimberFold.Fixtures;

// Test-only installed-module replay on an owned, non-active byte-identical fixture copy.
public static class NativePreservationProbe
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    private sealed record ParameterRow(long Id, string Name, string Storage, bool HasValue, string? Value);
    private sealed record Row(long Id, string UniqueId, string Type, string Name, long TypeId, string? Category, long OwnerViewId,
        bool ViewSpecific, Guid[] EntitySchemas, ParameterRow[] Parameters);
    private sealed record Change(string Operation, string[] Transactions, long[] Modified, long[] Deleted, long[] Added);

    public static object Check(UIApplication uiapp, string retainedRun, string destination, string mode, CancellationToken ct)
    {
        var source = uiapp.ActiveUIDocument?.Document ?? throw new InvalidOperationException("Pinned source required.");
        string? pinned = Environment.GetEnvironmentVariable("REVITTHYME_FOUNDATION_FIXTURE");
        if (Environment.GetEnvironmentVariable("REVITTHYME_TRUSTED_CODE") != "1" || pinned is null || source.IsWorkshared ||
            uiapp.Application.Documents.Size != 1 || !Path.GetFullPath(pinned).Equals(Path.GetFullPath(source.PathName), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("One exact disposable pinned source required.");
        if (!Path.IsPathFullyQualified(destination) || Directory.Exists(destination) || File.Exists(destination))
            throw new InvalidDataException("Fresh absolute evidence directory required.");
        string runs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RevitThyme", "TimberFold", "runs") + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(retainedRun).StartsWith(runs, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Owned retained run required.");
        var original = Rows(source, ct); bool wasModified = source.IsModified; long view = source.ActiveView.Id.Value; string savedHash = Hash(source.PathName);
        Directory.CreateDirectory(destination);
        foreach (string folder in new[] { "cutting", "guides" })
        {
            string output = Path.Combine(destination, folder); Directory.CreateDirectory(output);
            foreach (string file in Directory.GetFiles(Path.Combine(retainedRun, folder)))
            {
                string target = Path.Combine(output, Path.GetFileName(file)); File.Copy(file, target, false);
                if (Hash(file) != Hash(target)) throw new InvalidDataException("Copied input hash mismatch.");
            }
        }
        Directory.CreateDirectory(Path.Combine(destination, "native-reference-inputs"));
        string copy = Path.Combine(destination, "Probe.rvt");
        using (var input = new FileStream(source.PathName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var output = new FileStream(copy, FileMode.CreateNew, FileAccess.Write, FileShare.Read)) { input.CopyTo(output); output.Flush(true); }
        if (Hash(copy) != savedHash) throw new InvalidDataException("Fixture copy differs from pinned bytes.");
        var addin = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "RevitThyme.Addin");
        var core = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "RevitThyme.Core");
        var options = (JsonSerializerOptions)core.GetType("RevitThyme.Core.Protocol", true)!.GetField("Json")!.GetValue(null)!;
        var json = JsonNode.Parse(File.ReadAllText(Path.Combine(retainedRun, "native-plan.json")))!;
        json["runId"] = Guid.NewGuid().ToString("D"); json["guides"]!["directory"] = Path.Combine(destination, "guides");
        var plan = JsonSerializer.Deserialize(json.ToJsonString(), core.GetType("RevitThyme.Core.TimberNativeKitPlan", true)!, options)!;
        Document? temporary = null; TransactionGroup? group = null;
        Row[] before = [], after = [], restored = []; var changes = new List<Change>(); object? beforeCatalog = null, afterCatalog = null;
        string? error = null, guardError = null, rollbackGuardError = null; bool closed = false, rolledBack = false;
        NativeTimberPreservationStamp[] calibration = []; int generationEventStart = 0; bool calibrationRestored = false;
        void Changed(object? sender, DocumentChangedEventArgs e)
        {
            if (temporary is null || !temporary.Equals(e.GetDocument())) return;
            changes.Add(new(e.Operation.ToString(), e.GetTransactionNames().ToArray(),
                e.GetModifiedElementIds().Select(i => i.Value).Order().ToArray(), e.GetDeletedElementIds().Select(i => i.Value).Order().ToArray(),
                e.GetAddedElementIds().Select(i => i.Value).Order().ToArray()));
        }
        uiapp.Application.DocumentChanged += Changed;
        try
        {
            ct.ThrowIfCancellationRequested(); temporary = uiapp.Application.OpenDocumentFile(copy);
            before = Rows(temporary, ct);
            var preservation = addin.GetType("RevitThyme.Addin.NativeTimberKitPreservation", true)!;
            beforeCatalog = Invoke(preservation.GetMethod("Catalog", Hidden)!, null, [temporary, ct]);
            group = new TransactionGroup(temporary, "Test-only installed native preservation replay");
            if (group.Start() != TransactionStatus.Started) throw new InvalidOperationException("Probe group did not start.");
            if (mode == "calibrated")
            {
                bool copyModified = temporary.IsModified;
                using var measurement = new TransactionGroup(temporary, "Measured documentation calibration");
                if (measurement.Start() != TransactionStatus.Started) throw new InvalidOperationException("Calibration group did not start.");
                try
                {
                    using var tx = new Transaction(temporary, "Measured ViewDrafting.Create+Scale1");
                    if (tx.Start() != TransactionStatus.Started) throw new InvalidOperationException("Calibration transaction did not start.");
                    var type = new FilteredElementCollector(temporary).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>().First(t => t.ViewFamily == ViewFamily.Drafting);
                    var viewCreated = ViewDrafting.Create(temporary, type.Id); viewCreated.Scale = 1; temporary.Regenerate();
                    if (tx.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Calibration transaction did not commit.");
                    var states = Stamps(before, beforeCatalog!);
                    var modifiedIds = changes.SelectMany(e => e.Modified).ToHashSet();
                    calibration = states.Where(e => modifiedIds.Contains(e.Id)).ToArray();
                    var currentCatalog = Invoke(preservation.GetMethod("Catalog", Hidden)!, null, [temporary, ct])!;
                    if (!NativeTimberKitPreservationCheck.Check(states, Stamps(Rows(temporary, ct), currentCatalog),
                        modifiedIds, changes.SelectMany(e => e.Deleted), calibration)) throw new InvalidDataException("Measured calibration altered source content or an ineligible original.");
                }
                finally
                {
                    if (measurement.RollBack() != TransactionStatus.RolledBack) throw new InvalidOperationException("Calibration rollback failed.");
                    calibrationRestored = temporary.IsModified == copyModified && JsonSerializer.Serialize(before) == JsonSerializer.Serialize(Rows(temporary, ct));
                    if (!calibrationRestored) throw new InvalidDataException("Calibration did not restore complete copied source.");
                }
                generationEventStart = changes.Count;
            }
            if (mode == "actions") Actions(temporary, addin, plan, destination, ct);
            else if (mode is "full" or "calibrated")
            {
                var type = addin.GetType("RevitThyme.Addin.NativeTimberKitDocuments", true)!;
                var documents = Activator.CreateInstance(type, Hidden, null, [temporary, plan, destination], null)!;
                Invoke(type.GetMethod("Create", Hidden)!, documents, [ct]);
            }
            else throw new InvalidDataException("Unknown probe mode.");
            after = Rows(temporary, ct);
            afterCatalog = Invoke(preservation.GetMethod("Catalog", Hidden)!, null, [temporary, ct]);
            var workEvents = changes.Skip(generationEventStart);
            if (!NativeTimberKitPreservationCheck.Check(Stamps(before, beforeCatalog!), Stamps(after, afterCatalog!),
                workEvents.SelectMany(c => c.Modified), workEvents.SelectMany(c => c.Deleted), calibration))
                guardError = "Installed catalog or original-ID DocumentChanged preservation condition rejected.";
            if (group.RollBack() != TransactionStatus.RolledBack) throw new InvalidOperationException("Probe group rollback failed.");
            rolledBack = true; restored = Rows(temporary, ct);
            if (JsonSerializer.Serialize(before) != JsonSerializer.Serialize(restored)) rollbackGuardError = "Copy rollback raw catalog differs.";
        }
        catch (Exception failure) { error = failure.ToString(); }
        finally
        {
            if (group?.GetStatus() == TransactionStatus.Started) rolledBack = group.RollBack() == TransactionStatus.RolledBack;
            group?.Dispose(); uiapp.Application.DocumentChanged -= Changed;
            if (temporary is { IsValidObject: true }) closed = temporary.Close(false);
        }
        var current = Rows(source, ct);
        bool sourcePreserved = source.Equals(uiapp.ActiveUIDocument?.Document) && source.ActiveView.Id.Value == view && source.IsModified == wasModified &&
            Hash(source.PathName) == savedHash && JsonSerializer.Serialize(original) == JsonSerializer.Serialize(current) && uiapp.Application.Documents.Size == 1;
        var old = before.ToDictionary(r => r.Id); var now = after.ToDictionary(r => r.Id);
        var originalsModified = changes.SelectMany(c => c.Modified.Concat(c.Deleted)).Where(old.ContainsKey).Distinct().Order().ToArray();
        var contentChanged = after.Length == 0 ? [] : before.Where(r => !now.TryGetValue(r.Id, out var next) || JsonSerializer.Serialize(r) != JsonSerializer.Serialize(next)).Select(r => r.Id).ToArray();
        string report = Path.Combine(destination, "preservation-probe.json");
        File.WriteAllBytes(report, JsonSerializer.SerializeToUtf8Bytes(new { mode, sourcePath = source.PathName, savedHash, copy, before, after, restored,
            beforeCatalog, afterCatalog, changes, calibration, calibrationRestored, generationEventStart,
            originalsModified, contentChanged, guardError, rollbackGuardError, error, rolledBack, closed, sourcePreserved,
            installedAddin = addin.Location, installedAddinSha256 = Hash(addin.Location) }, new JsonSerializerOptions { WriteIndented = true }));
        return new { probeSucceeded = sourcePreserved && closed && rolledBack && error is null && rollbackGuardError is null,
            sourcePreserved, closed, rolledBack, calibrationRestored, guardRejected = guardError is not null,
            modifiedOriginalCount = originalsModified.Length, originalsModified = originalsModified.Take(30),
            contentChangedCount = contentChanged.Length, contentChanged = contentChanged.Take(30), error,
            report, sha256 = Hash(report) };
    }
    private static NativeTimberPreservationStamp[] Stamps(Row[] rows, object catalog)
    {
        var hashes = ((System.Collections.IEnumerable)catalog).Cast<object>().ToDictionary(e => (long)e.GetType().GetProperty("Id")!.GetValue(e)!,
            e => (string)e.GetType().GetProperty("ContentHash")!.GetValue(e)!);
        return rows.Select(e => new NativeTimberPreservationStamp(e.Id, e.UniqueId, hashes[e.Id], e.Type, e.Category is not null,
            e.TypeId, e.OwnerViewId, e.ViewSpecific, e.EntitySchemas.Length)).ToArray();
    }
    private static object? Invoke(MethodInfo method, object? instance, object?[] args)
    {
        try { return method.Invoke(instance, args); }
        catch (TargetInvocationException e) { throw e.InnerException ?? e; }
    }
    private static void Actions(Document doc, Assembly addin, object plan, string directory, CancellationToken ct)
    {
        ViewDrafting? view = null; ViewSheet? sheet = null; TextNoteType? text = null;
        void Step(string name, Action action)
        {
            ct.ThrowIfCancellationRequested(); using var tx = new Transaction(doc, "Preservation probe: " + name);
            if (tx.Start() != TransactionStatus.Started) throw new InvalidOperationException("Step did not start.");
            action(); doc.Regenerate();
            if (tx.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Step did not commit: " + name);
        }
        Step("DraftingView", () =>
        {
            var type = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>().First(t => t.ViewFamily == ViewFamily.Drafting);
            view = ViewDrafting.Create(doc, type.Id); view.Scale = 1;
        });
        Step("CutImport", () =>
        {
            using var options = new DWGImportOptions { Unit = ImportUnit.Default, Placement = ImportPlacement.Origin, ThisViewOnly = true, AutoCorrectAlmostVHLines = false };
            if (!doc.Import(Path.Combine(directory, "cutting", "CUT-001.dxf"), options, view!, out _)) throw new InvalidDataException("Probe import failed.");
        });
        Step("DuplicateTextType", () =>
        {
            var original = (TextNoteType)doc.GetElement(doc.GetDefaultElementTypeId(ElementTypeGroup.TextNoteType));
            text = (TextNoteType)original.Duplicate("Preservation probe text"); text.get_Parameter(BuiltInParameter.TEXT_SIZE).Set(.125 / 12);
        });
        Step("TextNote", () => TextNote.Create(doc, view!.Id, XYZ.Zero, "Probe", text!.Id));
        Step("Sheet", () => sheet = ViewSheet.Create(doc, ElementId.InvalidElementId));
        Step("Viewport", () => Viewport.Create(doc, sheet!.Id, view!.Id, new XYZ(1, 1, 0)));
        Step("OwnershipTag", () => Invoke(addin.GetType("RevitThyme.Addin.NativeTimberKitOwnership", true)!.GetMethod("TagElement", Hidden)!,
            null, [view, plan, "CutView", "probe"]));
    }
    private static Row[] Rows(Document doc, CancellationToken ct) => new FilteredElementCollector(doc).WhereElementIsNotElementType().ToElementIds()
        .Concat(new FilteredElementCollector(doc).WhereElementIsElementType().ToElementIds()).OrderBy(id => id.Value).Select(id =>
        {
            ct.ThrowIfCancellationRequested(); var e = doc.GetElement(id);
            return new Row(id.Value, e.UniqueId, e.GetType().FullName!, e.Name, e.GetTypeId().Value, e.Category?.Name, e.OwnerViewId.Value,
                e.ViewSpecific, e.GetEntitySchemaGuids().Order().ToArray(), e.Parameters.Cast<Parameter>().OrderBy(p => p.Id.Value)
                .Select(p => new ParameterRow(p.Id.Value, p.Definition.Name, p.StorageType.ToString(), p.HasValue, p.StorageType switch
                {
                    StorageType.Double => p.AsDouble().ToString("R", CultureInfo.InvariantCulture), StorageType.Integer => p.AsInteger().ToString(CultureInfo.InvariantCulture),
                    StorageType.ElementId => p.AsElementId().Value.ToString(CultureInfo.InvariantCulture), StorageType.String => p.AsString(), _ => null
                })).ToArray());
        }).ToArray();
    private static string Hash(string file)
    {
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}
