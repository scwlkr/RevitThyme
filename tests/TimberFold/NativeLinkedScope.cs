using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitThyme.TimberFold.Fixtures;

// Installed public extraction only. The caller owns the rollback query and must
// independently verify native catalog/open documents/modified state after return.
public static class NativeLinkedScope
{
    public static object Check(UIApplication app, string retainedGeneration, string linkedRvt,
        string evidenceDirectory, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var doc = app.ActiveUIDocument?.Document ?? throw new InvalidOperationException("Pinned project required.");
        string? pinned = Environment.GetEnvironmentVariable("REVITTHYME_FOUNDATION_FIXTURE");
        if (Environment.GetEnvironmentVariable("REVITTHYME_TRUSTED_CODE") != "1" || pinned is null ||
            !Path.IsPathFullyQualified(pinned) || doc.IsWorkshared || doc.IsFamilyDocument || !doc.IsModifiable || app.Application.Documents.Size != 1 ||
            !string.Equals(Path.GetFullPath(pinned), Path.GetFullPath(doc.PathName), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Use dev-query in one exact trusted pinned disposable project.");
        string runs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RevitThyme", "TimberFold", "runs");
        if (!Path.IsPathFullyQualified(retainedGeneration) || !Guid.TryParseExact(Path.GetFileName(retainedGeneration), "D", out var runId) ||
            !string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(retainedGeneration)), Path.Combine(runs, runId.ToString("D")), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Use an exact retained host-owned generation directory.");
        if (!Path.IsPathFullyQualified(linkedRvt) || !File.Exists(linkedRvt) || !linkedRvt.EndsWith(".rvt", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Path.GetFullPath(linkedRvt), Path.GetFullPath(doc.PathName), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Link a different original authored RVT, never the active model itself.");
        linkedRvt = Path.GetFullPath(linkedRvt); Plain(linkedRvt); Plain(retainedGeneration);
        if (!Path.IsPathFullyQualified(evidenceDirectory)) throw new InvalidDataException("Use fresh absolute evidence outside generation storage.");
        evidenceDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(evidenceDirectory));
        if (Directory.Exists(evidenceDirectory) || File.Exists(evidenceDirectory) || Inside(runs, evidenceDirectory))
            throw new InvalidDataException("Preserve previous evidence and all generation storage.");
        string parent = Path.GetDirectoryName(evidenceDirectory)!;
        if (!Directory.Exists(parent)) throw new DirectoryNotFoundException("Evidence parent must already exist.");
        Plain(parent);
        string sourceHash = Hash(doc.PathName, ct), linkedHash = Hash(linkedRvt, ct);
        long viewId = doc.ActiveView.Id.Value; bool modified = doc.IsModified;
        var originalIds = Ids(doc); var openBefore = Documents(app);
        var addin = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "RevitThyme.Addin");
        var core = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "RevitThyme.Core");
        var options = (JsonSerializerOptions)core.GetType("RevitThyme.Core.Protocol", true)!.GetField("Json")!.GetValue(null)!;
        var planType = core.GetType("RevitThyme.Core.TimberNativeKitPlan", true)!;
        var scopeType = core.GetType("RevitThyme.Core.TimberScope", true)!;
        string planPath = Path.Combine(retainedGeneration, "native-plan.json");
        if (new FileInfo(planPath).Length is <= 0 or > 134217728) throw new InvalidDataException("Retained native plan exceeds its 128 MiB bound.");
        Plain(planPath);
        var plan = JsonSerializer.Deserialize(File.ReadAllBytes(planPath), planType, options) ?? throw new InvalidDataException("Missing retained native plan.");
        object Property(object value, string name) => value.GetType().GetProperty(name)!.GetValue(value)!;
        if ((Guid)Property(plan, "RunId") != runId) throw new InvalidDataException("Retained plan run identity differs.");
        object target = Property(Property(plan, "SourceContext"), "Target"), scope = Property(plan, "Scope"), profile = Property(plan, "Profile");
        var hostType = addin.GetType("RevitThyme.Addin.FoundationApplication", true)!;
        object host = hostType.GetProperty("Instance")!.GetValue(null) ?? throw new InvalidOperationException("Installed host required.");
        var model = Activator.CreateInstance(addin.GetType("RevitThyme.Addin.RevitModel", true)!, app, host)!;
        object current = Property(model.GetType().GetMethod("Capture")!.Invoke(model, null)!, "Target");
        foreach (string field in new[] { "ProcessId", "ProcessStart", "SessionId", "DocumentId", "ViewId" })
            if (!object.Equals(Property(target, field), Property(current, field))) throw new InvalidDataException("Retained generation targets a different native " + field);
        if ((int)Property(current, "ProcessId") != Environment.ProcessId) throw new InvalidDataException("Native process differs from installed host.");
        var scopeJson = JsonSerializer.SerializeToElement(scope, scopeType, options);
        var exclusions = Json(scopeJson, "excludeIds");
        if (exclusions.ValueKind is not JsonValueKind.Null && exclusions.GetArrayLength() != 0)
            throw new InvalidDataException("This regression requires an original scope with no explicit exclusions.");
        var extract = addin.GetType("RevitThyme.Addin.NativeTimberExtraction", true)!.GetMethod("Extract", BindingFlags.Public | BindingFlags.Static)!;
        JsonElement Capture(object selectedScope)
        {
            ct.ThrowIfCancellationRequested();
            var value = extract.Invoke(null, [doc, target, profile, selectedScope])!;
            return JsonSerializer.SerializeToElement(value, value.GetType(), options);
        }
        Directory.CreateDirectory(evidenceDirectory);
        var baseline = Capture(scope);
        Write("baseline.json", baseline);
        if (Errors(baseline).Length != 0 || Json(baseline, "documentFingerprint").GetString() != (string)Property(plan, "SourceFingerprint"))
            throw new InvalidDataException("Clean original extraction differs from the retained generation baseline.");
        ct.ThrowIfCancellationRequested();
        using var linkOptions = new RevitLinkOptions(false);
        var load = RevitLinkType.Create(doc, ModelPathUtils.ConvertUserVisiblePathToModelPath(linkedRvt), linkOptions);
        if (load.LoadResult != LinkLoadResultType.LinkLoaded) throw new InvalidDataException("Real original Revit link did not load: " + load.LoadResult);
        ct.ThrowIfCancellationRequested();
        var instance = RevitLinkInstance.Create(doc, load.ElementId);
        doc.Regenerate();
        var linked = instance.GetLinkDocument() ?? throw new InvalidDataException("Loaded link has no native linked document.");
        if (!linked.IsLinked || linked.Equals(doc) || !string.Equals(Path.GetFullPath(linked.PathName), linkedRvt, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Native link resolved a different document.");
        string linkId = instance.Id.Value.ToString(CultureInfo.InvariantCulture);
        var defaultCapture = Capture(scope); Write("linked-default.json", defaultCapture);
        var explicitScope = scopeJson.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.OrdinalIgnoreCase);
        explicitScope["excludeIds"] = JsonSerializer.SerializeToElement(new[] { linkId }, options);
        var excludedScope = JsonSerializer.Deserialize(JsonSerializer.SerializeToUtf8Bytes(explicitScope, options), scopeType, options)!;
        var excluded = Capture(excludedScope); Write("linked-excluded.json", excluded);
        bool defaultBlocks = Sources(defaultCapture).Any(s => Json(s, "id").GetString() == linkId && Json(s, "category").GetString() == "Link" &&
            Json(s, "disposition").GetString() == "Unsupported") && Errors(defaultCapture).Any(d =>
                Json(d, "sourceId").GetString() == linkId && Json(d, "code").GetString() == "unsupported_architecture");
        bool explicitExcludes = Sources(excluded).Any(s => Json(s, "id").GetString() == linkId && Json(s, "category").GetString() == "Link" &&
            Json(s, "disposition").GetString() == "Excluded" && Json(s, "reason").GetString() == "Explicit scope exclusion.") && Errors(excluded).Length == 0;
        bool hostGeometryUnchanged = new[] { "faces", "openings" }.All(name =>
            Json(baseline, name).GetRawText() == Json(defaultCapture, name).GetRawText() && Json(baseline, name).GetRawText() == Json(excluded, name).GetRawText());
        bool filesUnchanged = Hash(doc.PathName, ct) == sourceHash && Hash(linkedRvt, ct) == linkedHash;
        bool passed = defaultBlocks && explicitExcludes && hostGeometryUnchanged && filesUnchanged && doc.ActiveView.Id.Value == viewId && doc.Equals(app.ActiveUIDocument?.Document);
        var report = new { schema = 1, passed, runId, linkedRvt, linkedSourceSha256 = linkedHash, sourcePath = doc.PathName, savedSourceSha256 = sourceHash,
            sourceWasModified = modified, viewId, originalIds, addedNativeIds = Ids(doc).Except(originalIds).ToArray(), openBefore, openWithLink = Documents(app),
            linkId, linkUniqueId = instance.UniqueId, linkTypeId = load.ElementId.Value, loadResult = load.LoadResult.ToString(), linkedDocumentName = linked.ProjectInformation.Name,
            defaultBlocks, explicitExcludes, hostGeometryUnchanged, filesUnchanged, baselineSourceIds = SourceIds(baseline), defaultSourceIds = SourceIds(defaultCapture),
            excludedSourceIds = SourceIds(excluded), originalScope = scopeJson, excludedScope = JsonSerializer.SerializeToElement(excludedScope, scopeType, options),
            captureTarget = JsonSerializer.SerializeToElement(target, target.GetType(), options), currentTarget = JsonSerializer.SerializeToElement(current, current.GetType(), options),
            nativePlanSha256 = Hash(planPath, ct), installedAddin = addin.Location, installedAddinSha256 = Hash(addin.Location, ct), installedCore = core.Location,
            installedCoreSha256 = Hash(core.Location, ct), observerMvid = typeof(NativeLinkedScope).Module.ModuleVersionId,
            callerMustVerifyQueryRollback = true, callerMustVerifyOpenDocuments = true, physicalChecksOutstanding = true };
        Write("qualification.json", report);
        return new { passed, linkId, linkUniqueId = instance.UniqueId, linkTypeId = load.ElementId.Value, loadResult = load.LoadResult.ToString(),
            defaultBlocks, explicitExcludes, hostGeometryUnchanged, linkedSourceSha256 = linkedHash, report = Path.Combine(evidenceDirectory, "qualification.json"),
            reportSha256 = Hash(Path.Combine(evidenceDirectory, "qualification.json"), ct), callerMustVerifyQueryRollback = true, callerMustVerifyOpenDocuments = true };

        void Write(string name, object value)
        {
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(value, options);
            if (bytes.Length > 67108864) throw new InvalidDataException("Native link evidence exceeds 64 MiB.");
            using var stream = new FileStream(Path.Combine(evidenceDirectory, name), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            stream.Write(bytes); stream.Flush(true);
        }
    }
    private static JsonElement Json(JsonElement element, string name) => element.EnumerateObject().Single(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)).Value;
    private static JsonElement[] Sources(JsonElement value) => Json(value, "sources").EnumerateArray().ToArray();
    private static string[] SourceIds(JsonElement value) => Sources(value).Select(s => Json(s, "id").GetString()!).Order(StringComparer.Ordinal).ToArray();
    private static JsonElement[] Errors(JsonElement value) => Json(value, "diagnostics").EnumerateArray().Where(d => Json(d, "severity").GetString() == "Error").ToArray();
    private static long[] Ids(Document doc) => new FilteredElementCollector(doc).WhereElementIsNotElementType().ToElementIds()
        .Concat(new FilteredElementCollector(doc).WhereElementIsElementType().ToElementIds()).Select(id => id.Value).Order().ToArray();
    private static object[] Documents(UIApplication app) => app.Application.Documents.Cast<Document>()
        .Select(d => (object)new { d.Title, d.PathName, d.IsLinked, d.IsModified }).ToArray();
    private static bool Inside(string root, string path) => string.Equals(root, path, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    private static void Plain(string path)
    {
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Native link qualification paths cannot be redirected.");
    }
    private static string Hash(string path, CancellationToken ct)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length > 268435456) throw new InvalidDataException("Native link input exceeds 256 MiB.");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); byte[] buffer = new byte[65536]; int read; long consumed = 0;
        while ((read = stream.Read(buffer)) > 0)
        {
            ct.ThrowIfCancellationRequested(); consumed += read;
            if (consumed > 268435456) throw new InvalidDataException("A native link input grew beyond 256 MiB.");
            hash.AppendData(buffer, 0, read);
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}
