using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using Autodesk.Revit.UI;

namespace RevitThyme.TimberFold.Fixtures;

internal static class NativeFailureEvidence
{
    internal sealed record Pin(int ProcessId, string ProcessStart, string SessionId, string DocumentId, string ViewId, long Revision);
    internal sealed record Stamp(long Id, string UniqueId, string ContentHash);
    private sealed record Ownership(int Schema, Guid RunId, Guid PreviewRunId, string Role, string UniqueId, string ContentHash);
    internal static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter() } };
    internal static string RunsRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RevitThyme", "TimberFold", "runs");

    internal static (Document Source, object Host) Guard(UIApplication app, Pin expected, bool exactRevision)
    {
        var source = app.ActiveUIDocument?.Document ?? throw new InvalidOperationException("Pinned source required.");
        string? fixture = Environment.GetEnvironmentVariable("REVITTHYME_FOUNDATION_FIXTURE");
        if (Environment.GetEnvironmentVariable("REVITTHYME_TRUSTED_CODE") != "1" || fixture is null ||
            !Path.IsPathFullyQualified(fixture) || source.IsFamilyDocument || source.IsWorkshared || app.Application.Documents.Size != 1 ||
            !string.Equals(Path.GetFullPath(source.PathName), Path.GetFullPath(fixture), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Use one trusted, exact pinned disposable source.");
        var addin = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "RevitThyme.Addin");
        object host = addin.GetType("RevitThyme.Addin.FoundationApplication", true)!.GetProperty("Instance")!.GetValue(null)
            ?? throw new InvalidOperationException("Installed host is unavailable.");
        var model = Activator.CreateInstance(addin.GetType("RevitThyme.Addin.RevitModel", true)!, app, host)!;
        object snapshot = model.GetType().GetMethod("Capture")!.Invoke(model, null)!;
        object target = snapshot.GetType().GetProperty("Target")!.GetValue(snapshot)!;
        var actual = JsonSerializer.Deserialize<Pin>(JsonSerializer.Serialize(target, target.GetType(), Json), Json)!;
        if (actual.ProcessId != Environment.ProcessId || (exactRevision ? actual : actual with { Revision = expected.Revision }) != expected)
            throw new InvalidOperationException("Process/start/session/document/view identity differs from the explicit pin.");
        return (source, host);
    }

    internal static bool OwnedDraftingView(ViewDrafting view, Guid generationId)
    {
        var schema = Schema.Lookup(new Guid("4ca4f8c4-0121-4d3b-807a-e0200485fb55"));
        if (schema is null) return false;
        var entity = view.GetEntity(schema);
        if (!entity.IsValid()) return false;
        string json = entity.Get<string>("Ownership");
        if (json.Length > 16384) return false;
        try
        {
            var tag = JsonSerializer.Deserialize<Ownership>(json, Json);
            return tag is { Schema: 1, ContentHash.Length: 64 } && tag.RunId == generationId &&
                tag.PreviewRunId != Guid.Empty && tag.UniqueId == view.UniqueId && tag.Role is "CutView" or "ReferenceView";
        }
        catch (JsonException) { return false; }
    }

    internal static Stamp[] Catalog(Document document, CancellationToken ct)
    {
        var ids = new FilteredElementCollector(document).WhereElementIsNotElementType().ToElementIds()
            .Concat(new FilteredElementCollector(document).WhereElementIsElementType().ToElementIds()).OrderBy(id => id.Value).ToArray();
        if (ids.Length > 500000) throw new InvalidDataException("Independent source catalog exceeds 500000 elements.");
        var schema = Schema.Lookup(new Guid("4ca4f8c4-0121-4d3b-807a-e0200485fb55"));
        return ids.Select(id =>
        {
            ct.ThrowIfCancellationRequested();
            var element = document.GetElement(id) ?? throw new InvalidDataException("Original element disappeared.");
            var parameters = element.Parameters.Cast<Parameter>().OrderBy(p => p.Id.Value).Select(p => new
            {
                Id = p.Id.Value, Storage = p.StorageType.ToString(), p.HasValue, Value = p.StorageType switch
                {
                    StorageType.Double => p.AsDouble().ToString("R", CultureInfo.InvariantCulture),
                    StorageType.Integer => p.AsInteger().ToString(CultureInfo.InvariantCulture),
                    StorageType.ElementId => p.AsElementId().Value.ToString(CultureInfo.InvariantCulture),
                    StorageType.String => p.AsString(), _ => null
                }
            }).ToArray();
            string? ownership = null;
            if (schema is not null) { var entity = element.GetEntity(schema); if (entity.IsValid()) ownership = entity.Get<string>("Ownership"); }
            if (ownership is { Length: > 16384 } || parameters.Length > 10000) throw new InvalidDataException("Independent source content exceeds its bound.");
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new { element.Name, TypeId = element.GetTypeId().Value,
                RuntimeType = element.GetType().FullName, OwnerViewId = element.OwnerViewId.Value, element.ViewSpecific, Parameters = parameters, Ownership = ownership }, Json);
            return new Stamp(id.Value, element.UniqueId, Convert.ToHexStringLower(SHA256.HashData(bytes)));
        }).ToArray();
    }

    internal static JsonElement Cancel(object host, Guid generation)
    {
        var core = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "RevitThyme.Core");
        var requestType = core.GetType("RevitThyme.Core.OperationRequest", true)!;
        var kind = Enum.Parse(core.GetType("RevitThyme.Core.OperationKind", true)!, "CancelTimberFoldRun");
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        { ["Id"] = Guid.NewGuid(), ["Operation"] = kind, ["OperationId"] = generation };
        var constructor = requestType.GetConstructors().Single();
        object?[] arguments = constructor.GetParameters().Select(p => values.TryGetValue(p.Name!, out var value) ? value :
            p.HasDefaultValue ? p.DefaultValue : throw new InvalidOperationException("Unexpected required operation parameter: " + p.Name)).ToArray();
        object request = constructor.Invoke(arguments);
        object operations = host.GetType().GetProperty("Operations")!.GetValue(host)!;
        var task = (Task)operations.GetType().GetMethod("ExecuteAsync")!.Invoke(operations, [request, CancellationToken.None])!;
        if (!task.Wait(TimeSpan.FromSeconds(2))) throw new TimeoutException("Public cancellation control did not acknowledge within two seconds; inspect the original generation.");
        object result = task.GetType().GetProperty("Result")!.GetValue(task)!;
        var reply = JsonSerializer.SerializeToElement(result, result.GetType(), Json);
        var run = Property(reply, "timberRun");
        if (Property(run, "runId").GetGuid() != generation || Property(run, "state").GetString() != "Finalizing" || Property(run, "nativeCommitted").GetBoolean())
            throw new InvalidDataException("Cancellation did not acknowledge the exact in-flight native generation.");
        return reply;
    }

    internal static void Plain(string path)
    {
        for (var current = new DirectoryInfo(path); current is not null; current = current.Parent)
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Failure qualification paths cannot be redirected.");
    }
    internal static bool Inside(string root, string path) => string.Equals(root, path, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    internal static string Hash(string path, CancellationToken ct)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length > 268435456) throw new InvalidDataException("Source file exceeds the 256 MiB observation limit.");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); byte[] buffer = new byte[65536]; int read;
        while ((read = stream.Read(buffer)) > 0) { ct.ThrowIfCancellationRequested(); hash.AppendData(buffer, 0, read); }
        ct.ThrowIfCancellationRequested(); return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
    internal static void Write(string directory, string name, object value)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(value, Json);
        if (bytes.Length > 67108864) throw new InvalidDataException("Failure observation exceeds 64 MiB.");
        using var stream = new FileStream(Path.Combine(directory, name), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        stream.Write(bytes); stream.Flush(true);
    }
    internal static JsonElement Read(string path)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length > 33554432) throw new InvalidDataException("Failure outcome exceeds 32 MiB.");
        using var json = JsonDocument.Parse(stream); return json.RootElement.Clone();
    }
    internal static JsonElement Property(JsonElement element, string name) => element.EnumerateObject()
        .Single(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)).Value;
}
