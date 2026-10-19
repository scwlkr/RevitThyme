using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitThyme.TimberFold.Fixtures;

// Post-generation observer only. Root drives typed generation and the actual UI Undo.
// This assembly has no Core/Addin reference and never loads replacement product code.
public static class NativeKitQualification
{
    private sealed record CheckResult(string Name, bool Passed);
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter() } };

    public static object Check(UIApplication uiapp, string runDirectory, string evidenceDirectory, bool afterUndo, CancellationToken ct)
    {
        var source = uiapp.ActiveUIDocument?.Document ?? throw new InvalidOperationException("Pinned source is required.");
        var pinned = Environment.GetEnvironmentVariable("REVITTHYME_FOUNDATION_FIXTURE");
        if (Environment.GetEnvironmentVariable("REVITTHYME_TRUSTED_CODE") != "1" || pinned is null ||
            !string.Equals(Path.GetFullPath(source.PathName), Path.GetFullPath(pinned), StringComparison.OrdinalIgnoreCase) ||
            source.IsWorkshared || uiapp.Application.Documents.Size != 1) throw new InvalidOperationException("Use one exact pinned disposable source.");
        if (!Path.IsPathFullyQualified(evidenceDirectory) || Directory.Exists(evidenceDirectory) || File.Exists(evidenceDirectory))
            throw new InvalidDataException("Qualification evidence needs a fresh absolute directory.");
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RevitThyme", "TimberFold", "runs");
        if (!Guid.TryParse(Path.GetFileName(runDirectory), out var runId) ||
            !string.Equals(Path.GetFullPath(runDirectory), Path.Combine(root, runId.ToString("D")), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Qualify only an exact host-owned generation directory.");
        using var receipt = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(runDirectory, "native-receipt.json")));
        string preparedPath = Path.Combine(runDirectory, "native-prepared.json");
        using var prepared = JsonDocument.Parse(File.ReadAllBytes(preparedPath));
        var r = receipt.RootElement; var p = prepared.RootElement;
        if (Text(r, "state") != "Committed" || Property(r, "runId").GetGuid() != runId || Property(p, "runId").GetGuid() != runId ||
            Hash(preparedPath) != Text(r, "preparedReceiptSha256") || !Property(r, "sourcePreserved").GetBoolean() ||
            !Property(r, "nativeReadbackPassed").GetBoolean() || !string.Equals(Text(p, "sourcePath"), source.PathName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Generation does not have a matching committed native receipt.");
        var before = Property(p, "beforeCatalog").EnumerateArray().ToDictionary(e => Property(e, "id").GetInt64(), e => Text(e, "uniqueId"));
        var created = Property(r, "createdElements").EnumerateArray().ToArray();
        var referenceInputs = Property(p, "referenceInputs").EnumerateArray().ToArray();
        if (created.Length == 0 || created.Select(e => Property(e, "id").GetInt64()).Distinct().Count() != created.Length)
            throw new InvalidDataException("Native receipt has empty/duplicate created identities.");
        var actual = Ids(source); var checks = new List<CheckResult>();
        Add("native reference display multiplier is explicit48", Property(p, "referenceMultiplier").GetInt32() == 48);
        Add("all original native IDs and UniqueIds remain", before.All(e => source.GetElement(new ElementId(e.Key))?.UniqueId == e.Value));
        Add("every original native parameter/name/type content hash is unchanged", NativeKitSourceAudit.ContentUnchanged(source, Property(p, "beforeCatalog"), ct));
        Add("per-run documentation bookkeeping calibration has checked rollback and exact eligible original identities",
            NativeKitSourceAudit.Calibration(source, Property(p, "bookkeepingCalibration"), Property(p, "beforeCatalog")));
        Add("saved source file has never changed", Hash(source.PathName) == Text(p, "savedSourceSha256"));
        Add("original source view remains active", source.ActiveView.Id.Value == Property(p, "originalViewId").GetInt64());
        var newIds = created.Select(e => Property(e, "id").GetInt64()).ToHashSet();
        if (afterUndo)
        {
            Add("one Undo removes every created native element/resource", newIds.All(id => source.GetElement(new ElementId(id)) is null));
            Add("one Undo restores exact original native ID inventory", actual.SequenceEqual(before.Keys.Order()));
            Add("one Undo restores original modified state", source.IsModified == Property(p, "sourceWasModified").GetBoolean());
        }
        else
        {
            Add("every introduced native ID is recorded exactly once", actual.Except(before.Keys).Order().SequenceEqual(newIds.Order()) && !before.Keys.Except(actual).Any());
            Add("documentation remains intentionally unsaved", source.IsModified);
            foreach (var item in created)
            {
                ct.ThrowIfCancellationRequested();
                long id = Property(item, "id").GetInt64(); string role = Text(item, "role");
                var e = source.GetElement(new ElementId(id)) ?? throw new InvalidDataException("Created native ID is missing: " + id);
                Add("created identity " + id, e.UniqueId == Text(item, "uniqueId"));
                if (role is "CutImport" or "ReferenceImport")
                {
                    bool owned = e is ImportInstance { ViewSpecific: true } import && source.GetElement(import.OwnerViewId) is ViewDrafting &&
                        created.Any(v => Property(v, "id").GetInt64() == import.OwnerViewId.Value && Text(v, "role") == (role == "CutImport" ? "CutView" : "ReferenceView"));
                    Add("view-specific annotation CAD " + id, owned);
                }
                if (role == "CutView") Add("cut view is printable/scale1/label-free " + id, e is ViewDrafting { CanBePrinted: true, Scale: 1 } &&
                    !new FilteredElementCollector(source, e.Id).OfClass(typeof(TextNote)).Any());
                if (role == "ReferenceView") Add("native reference uses explicit review scale48 " + id, e is ViewDrafting { Scale: 48 } &&
                    referenceInputs.Any(v => Property(v, "viewId").GetInt64() == id && Text(v, "viewUniqueId") == e.UniqueId && Property(v, "coordinateMultiplier").GetInt32() == 48));
                if (role == "NativeSheet") Add("native sheet has reference viewport and no titleblock " + id, e is ViewSheet sheet &&
                    sheet.GetAllViewports().Count == 1 && new FilteredElementCollector(source, sheet.Id).OfCategory(BuiltInCategory.OST_TitleBlocks).GetElementCount() == 0);
                if (e is DirectShape or ModelCurve or HostObject) Add("no fabricated model geometry " + id, false);
            }
            var notes = created.Where(e => Text(e, "role") is "PartLabel" or "GuideLabel")
                .Select(e => (TextNote)source.GetElement(new ElementId(Property(e, "id").GetInt64()))).ToList();
            foreach (var label in Property(p, "labels").EnumerateArray())
            {
                ct.ThrowIfCancellationRequested();
                var mapping = referenceInputs.Single(v => Text(v, "physicalGuideFile") == Text(label, "guideFile"));
                long owner = Property(mapping, "viewId").GetInt64();
                var position = Property(label, "physicalPositionInches");
                double x = Property(position, "x").GetDouble(), y = Property(position, "y").GetDouble();
                int index = notes.FindIndex(n => Normalize(n.Text) == Normalize(Text(label, "text")) && n.OwnerViewId.Value == owner &&
                    Math.Abs(n.Coord.X * 12 / 48 - x) <= .00001 && Math.Abs(n.Coord.Y * 12 / 48 - y) <= .00001 && Math.Abs(n.Coord.Z * 12 / 48) <= .00001 &&
                    Math.Abs(((TextNoteType)source.GetElement(n.GetTypeId())).get_Parameter(BuiltInParameter.TEXT_SIZE).AsDouble() * 12 - Property(label, "textHeightInches").GetDouble()) <= .0000001);
                Add("native guide label " + Text(label, "guideFile"), index >= 0);
                if (index >= 0) notes.RemoveAt(index);
            }
            Add("native labels contain no extra unmapped notes", notes.Count == 0);
        }
        foreach (var mapping in referenceInputs)
        {
            ct.ThrowIfCancellationRequested();
            string physical = Path.Combine(runDirectory, "guides", Text(mapping, "physicalGuideFile"));
            string native = Path.Combine(runDirectory, Text(mapping, "nativeRelativePath"));
            Add("native reference input hashes and inch-coordinate relationship " + Path.GetFileName(native),
                Property(mapping, "coordinateMultiplier").GetInt32() == 48 && Hash(physical) == Text(mapping, "physicalGuideSha256") &&
                Hash(native) == Text(mapping, "nativeInputSha256") && ReferenceScale(physical, native));
        }
        foreach (var file in Property(r, "files").EnumerateArray())
        {
            ct.ThrowIfCancellationRequested();
            string relative = Text(file, "relativePath"), path = Path.GetFullPath(Path.Combine(runDirectory, relative));
            if (!path.StartsWith(runDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Receipt file escaped its generation directory.");
            Add("retained output " + relative, new FileInfo(path).Length == Property(file, "bytes").GetInt64() && Hash(path) == Text(file, "sha256"));
        }
        string fingerprint = ExtractFingerprint(source, p);
        Add("public native reinspection preserves original source fingerprint", fingerprint == Text(p, "sourceFingerprint"));
        bool passed = checks.All(c => c.Passed);
        var result = new { passed, runId, afterUndo, sourceFingerprint = fingerprint, source.IsModified, activeViewId = source.ActiveView.Id.Value,
            originalElementCount = before.Count, createdElementCount = created.Length, actualElementCount = actual.Length, checks,
            physicalChecksOutstanding = true, nativeDwgGeometryProof = "See independently checked native-readback.json; this observer verifies native identities/source/Undo." };
        Directory.CreateDirectory(evidenceDirectory);
        string report = Path.Combine(evidenceDirectory, afterUndo ? "native-kit-undo.json" : "native-kit-documentation.json");
        using (var stream = new FileStream(report, FileMode.CreateNew))
        { JsonSerializer.Serialize(stream, result, new JsonSerializerOptions { WriteIndented = true }); stream.Flush(true); }
        return new { passed, afterUndo, report, sha256 = Hash(report), checkCount = checks.Count, sourceFingerprint = fingerprint };

        void Add(string name, bool value) => checks.Add(new(name, value));
    }

    private static string ExtractFingerprint(Document doc, JsonElement prepared)
    {
        var core = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "RevitThyme.Core");
        var addin = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "RevitThyme.Addin");
        object Decode(string name, string type) => JsonSerializer.Deserialize(Property(prepared, name).GetRawText(), core.GetType(type, true)!, Json)
            ?? throw new InvalidDataException("Prepared source settings are missing.");
        try
        {
            var snapshot = addin.GetType("RevitThyme.Addin.NativeTimberExtraction", true)!.GetMethod("Extract", BindingFlags.Public | BindingFlags.Static)!
                .Invoke(null, [doc, Decode("sourceTarget", "RevitThyme.Core.Target"), Decode("profile", "RevitThyme.Core.TimberFoldProfile"), Decode("scope", "RevitThyme.Core.TimberScope")])!;
            return (string)snapshot.GetType().GetProperty("DocumentFingerprint")!.GetValue(snapshot)!;
        }
        catch (TargetInvocationException error) { throw error.InnerException ?? error; }
    }
    private static long[] Ids(Document doc) => new FilteredElementCollector(doc).WhereElementIsNotElementType().ToElementIds()
        .Concat(new FilteredElementCollector(doc).WhereElementIsElementType().ToElementIds()).Select(id => id.Value).Order().ToArray();
    private static JsonElement Property(JsonElement e, string name) => e.EnumerateObject().Single(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)).Value;
    private static string Text(JsonElement e, string name) => Property(e, name).GetString() ?? throw new InvalidDataException("Missing " + name);
    private static string Normalize(string value) => value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').TrimEnd('\n');
    private static bool ReferenceScale(string physical, string native)
    {
        var a = File.ReadAllLines(physical); var b = File.ReadAllLines(native);
        if (a.Length != b.Length || a.Length % 2 != 0) return false;
        bool entities = false, inches = false;
        for (int i = 0; i < a.Length; i += 2)
        {
            if (int.Parse(a[i], System.Globalization.CultureInfo.InvariantCulture) != int.Parse(b[i], System.Globalization.CultureInfo.InvariantCulture)) return false;
            int code = int.Parse(a[i], System.Globalization.CultureInfo.InvariantCulture);
            if (code == 9 && a[i + 1] == "$INSUNITS" && i + 3 < a.Length) inches = a[i + 3].Trim() == "1" && b[i + 3].Trim() == "1";
            if (code == 2 && a[i + 1] == "ENTITIES") entities = true;
            if (code == 0 && a[i + 1] == "ENDSEC") entities = false;
            if (entities && code is 10 or 20)
            {
                double source = double.Parse(a[i + 1], System.Globalization.CultureInfo.InvariantCulture), scaled = double.Parse(b[i + 1], System.Globalization.CultureInfo.InvariantCulture);
                if (!double.IsFinite(scaled) || Math.Abs(scaled / 48 - source) > .00001) return false;
            }
            else if (a[i + 1] != b[i + 1]) return false;
        }
        return inches;
    }
    private static string Hash(string path)
    { using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); return Convert.ToHexStringLower(SHA256.HashData(stream)); }
}
