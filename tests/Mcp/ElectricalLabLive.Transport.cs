using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using RevitThyme.Core;

namespace RevitThyme.McpTests;

// Real installed SDK transport. C# bodies cannot perform lifecycle or file operations.
internal sealed partial class ElectricalLabLive
{
    private readonly string folder;
    private readonly JsonElement input, reference;
    private readonly SessionBinding binding;
    private readonly List<object> calls = [];
    private readonly object logGate = new();
    private readonly HashSet<long> added = [];
    private readonly Dictionary<string, string> scripts;
    private string transcriptName = "transcript.json";
    private object? resumeSource;
    private McpClient client = null!, observer = null!;
    private string studyUid = "", qaName = "RevitThyme Electrical QA " + Guid.NewGuid().ToString("N")[..8], savedA;
    private string[] annotationUids = [];
    private string Text(string name) => input.GetProperty(name).GetString()!;
    private ElectricalLabLive(string folder)
    {
        if (!Path.IsPathFullyQualified(folder)) throw new ArgumentException("Use an absolute owned run folder.");
        this.folder = folder; input = Read(Path.Combine(folder, "input.json")); reference = Read(Text("referencePath"));
        binding = SessionBinding.Load(Text("session")); savedA = Text("sourceSha256");
        scripts = new[] { "inventory.csx", "duplicate.csx", "copy-annotations.csx", "inspect-duplicate.csx" }.ToDictionary(name => name, name => File.ReadAllText(Path.Combine(Text("scriptsDirectory"), name)));
    }
    public static async Task Run(string folder)
    {
        var driver = new ElectricalLabLive(folder);
        try { await driver.Execute(); }
        finally { if (driver.client is not null) await driver.client.DisposeAsync(); if (driver.observer is not null) await driver.observer.DisposeAsync(); }
    }
    private static JsonElement Read(string path) { using var json = JsonDocument.Parse(File.ReadAllText(path)); return json.RootElement.Clone(); }
    private void Save(string name, object value) => File.WriteAllText(Path.Combine(folder, name), JsonSerializer.Serialize(value, Protocol.Json));
    private void Log(object value) { lock (logGate) { calls.Add(value); Save(transcriptName, calls); } }
    private static string Hash(string path) { using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); return Convert.ToHexStringLower(SHA256.HashData(stream)); }
    private static string Digest(string text) => Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));
    private async Task<McpClient> Client(string role) => await McpClient.CreateAsync(new StdioClientTransport(new()
    { Command = Path.Combine(Text("bundle"), "mcp", "revitthyme-mcp.exe"), Arguments = ["--session", Text("session")], Name = "Electrical lab " + role }));
    private async Task<JsonElement> Call(string name, Dictionary<string, object?>? args = null, string? expected = null)
    {
        Log(new { phase = "submitted", name, args, utc = DateTime.UtcNow }); var watch = Stopwatch.StartNew();
        if (name != "get_operation" && args?.TryGetValue("requestId", out var id) == true) Save("intent-" + id + ".json", new { name, args });
        try
        {
            var reply = await Program.Call(client, name, args).WaitAsync(TimeSpan.FromSeconds(45));
            Log(new { phase = "returned", name, args, milliseconds = watch.Elapsed.TotalMilliseconds, reply.Data, reply.Result.IsError });
            if (expected is null or "inspected" or "applied" or "document_opened" or "document_activated" or "document_saved" or "document_closed" or "view_activated" or "image_exported")
                if (reply.Data.TryGetProperty("code", out var code) && code.GetString() is "transport_lost" or "pending" && args?.TryGetValue("requestId", out var originalId) == true)
                    await RecordBlocked(name, originalId!.ToString()!, reply.Data);
            if (expected != "any") Program.Check("electrical " + name + (expected is null ? " succeeds" : ": " + expected), expected is null ? reply.Result.IsError != true
                : reply.Data.GetProperty("code").GetString() == expected && reply.Result.IsError == (expected is not ("inspected" or "applied" or "document_opened" or "document_activated" or "document_saved" or "document_closed" or "view_activated" or "image_exported")));
            if (name == "export_view_image" && reply.Result.IsError != true)
            {
                var image = reply.Data.GetProperty("imageExport").Deserialize<ViewImageResult>(Protocol.Json)!; byte[] bytes = File.ReadAllBytes(image.Path);
                Program.Check("electrical native PNG dimensions, bytes, hash and inline image agree", Hash(image.Path) == image.Sha256 && bytes.LongLength == image.Bytes
                    && reply.Result.Content.OfType<ImageContentBlock>().Single().DecodedData.Span.SequenceEqual(bytes)
                    && System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4)) == image.Width
                    && System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4)) == image.Height);
                string copy = Path.Combine(folder, "image-" + reply.Data.GetProperty("id").GetString() + ".png"); File.Copy(image.Path, copy, false);
                Save("image-" + reply.Data.GetProperty("id").GetString() + ".json", new { copy, image });
            }
            return reply.Data;
        }
        catch (Exception error) { Log(new { phase = "threw", name, args, milliseconds = watch.Elapsed.TotalMilliseconds, error = error.GetType().Name, error.Message }); throw; }
    }
    private async Task<(string Id, Snapshot Snapshot)> Context(bool requireA = true)
    {
        var data = await Call("get_context"); var snapshot = data.GetProperty("result").GetProperty("snapshot").Deserialize<Snapshot>(Protocol.Json)!;
        Program.Check("electrical context pins exact native process/session", snapshot.Target.ProcessId == binding.ProcessId && snapshot.Target.ProcessStart == binding.ProcessStart && snapshot.Target.SessionId == binding.SessionId);
        if (requireA) Program.Check("electrical script capture is exact owned editable A", snapshot.DocumentPath == Text("a") && !snapshot.IsWorkshared && snapshot.CanModify);
        return (data.GetProperty("captureId").GetString()!, snapshot);
    }
    private Dictionary<string, object?> Lifecycle(string capture, string path, string? document = null, string? hash = null, string? id = null)
    {
        var args = new Dictionary<string, object?> { ["captureId"] = capture, ["requestId"] = id ?? Guid.NewGuid().ToString(), ["path"] = path };
        if (document is not null) args["documentId"] = document; if (hash is not null) args["expectedSha256"] = hash; return args;
    }
    private async Task<JsonElement> Observe(string id)
    {
        var watch = Stopwatch.StartNew(); var result = await Program.Call(observer, "get_operation", new() { ["requestId"] = id }).WaitAsync(TimeSpan.FromSeconds(5));
        Log(new { actor = "independent observer", name = "get_operation", requestId = id, milliseconds = watch.Elapsed.TotalMilliseconds, result.Data, result.Result.IsError }); return result.Data;
    }
    private async Task<JsonElement> Open(string path, string hash)
    {
        var context = await Context(false); string id = Guid.NewGuid().ToString(); var args = Lifecycle(context.Id, path, hash: hash, id: id);
        var watch = Stopwatch.StartNew(); var pending = Call("open_document", args, "any");
        while (!pending.IsCompleted)
        {
            if (await Task.WhenAny(pending, Task.Delay(5000)) == pending) break;
            await Observe(id);
        }
        JsonElement data;
        try { data = await pending; }
        catch (Exception error)
        {
            var retained = await Observe(id); Save("blocked-open.json", new { id, args, elapsedMilliseconds = watch.Elapsed.TotalMilliseconds, error = error.GetType().Name, error.Message, retained }); throw;
        }
        if (data.GetProperty("code").GetString() != "document_opened")
        {
            var retained = await Observe(id); Save("blocked-open.json", new { id, args, elapsedMilliseconds = watch.Elapsed.TotalMilliseconds, response = data, retained,
                boundary = "Dependent work stopped; original request retained. No dialog dismissal, reference sanitization, replacement request or alternate fixture." });
            throw new InvalidDataException("Electrical native open did not complete: recover original GUID " + id + "; " + data.GetRawText());
        }
        Program.Check("electrical typed open verifies exact original GUID and path", data.GetProperty("id").GetString() == id
            && data.GetProperty("outcome").GetString() == "Completed" && data.GetProperty("documentLifecycle").GetProperty("path").GetString() == path); return data;
    }
    private string Body(string name)
    {
        string Quote(string text) => JsonSerializer.Serialize(text);
        string Ids(IEnumerable<long> values) => string.Join(",", values.Select(id => id.ToString(CultureInfo.InvariantCulture) + "L"));
        var views = reference.GetProperty("views").EnumerateArray().ToArray();
        var replacements = new Dictionary<string, string>
        {
            ["__A__"] = Quote(Text("a")), ["__B__"] = Quote(Text("b")), ["__PID__"] = binding.ProcessId.ToString(CultureInfo.InvariantCulture), ["__START__"] = Quote(binding.ProcessStart),
            ["__VIEW_IDS__"] = Ids(views.Select(v => v.GetProperty("viewId").GetInt64())), ["__TEMPLATE_IDS__"] = Ids(views.Select(v => v.GetProperty("templateId").GetInt64())),
            ["__SCHEDULE_IDS__"] = Ids(reference.GetProperty("schedules").EnumerateArray().Select(s => s.GetProperty("id").GetInt64())),
            ["__CATEGORY_NAMES__"] = string.Join(",", reference.GetProperty("counts").EnumerateArray().Select(c => Quote(c.GetProperty("category").GetString()!))),
            ["__EXCLUDED_IDS__"] = Ids(added.Order()), ["__SOURCE_VIEW_ID__"] = views[0].GetProperty("viewId").GetInt64().ToString(CultureInfo.InvariantCulture) + "L",
            ["__LIGHTING_SOURCE_ID__"] = views[2].GetProperty("sourceId").GetInt64().ToString(CultureInfo.InvariantCulture) + "L",
            ["__STUDY_UID__"] = Quote(studyUid), ["__QA_NAME__"] = Quote(qaName), ["__ANNOTATION_UIDS__"] = string.Join(",", annotationUids.Select(Quote))
        };
        string body = scripts[name]; foreach (var pair in replacements) body = body.Replace(pair.Key, pair.Value, StringComparison.Ordinal);
        if (body.Contains("__", StringComparison.Ordinal)) throw new InvalidDataException("Unexpanded script placeholder."); return body;
    }
    private async Task<JsonElement> Code(string file, bool modify = false)
    {
        var context = await Context(); string id = Guid.NewGuid().ToString(), body = Body(file), sourceSha256 = Digest(body);
        File.WriteAllText(Path.Combine(folder, "body-" + id + ".csx"), body); Log(new { name = "bounded native code intent", file, id, sourceSha256, context.Snapshot.Target });
        var result = await Call(modify ? "run_modify" : "run_query", new() { ["captureId"] = context.Id, ["requestId"] = id, ["source"] = body, ["trusted"] = true, ["budgetMilliseconds"] = 10000 }, modify ? "applied" : "inspected");
        Program.Check("electrical Roslyn result matches exact submitted source hash", result.GetProperty("execution").GetProperty("sourceSha256").GetString() == sourceSha256);
        if (modify)
        {
            var changes = result.GetProperty("execution").GetProperty("changes").Deserialize<CodeChanges>(Protocol.Json)!;
            Program.Check("bounded electrical native edit has no deletions and stays within 512 changes", changes.Deleted.Length == 0 && changes.Added.Length > 0 && changes.Added.Length + changes.Modified.Length <= 512);
            var details = Value(result);
            long[] expectedAdded = file == "copy-annotations.csx" ? details.GetProperty("copied").EnumerateArray().Select(e => e.GetProperty("id").GetInt64()).ToArray()
                : file == "duplicate.csx" ? [details.GetProperty("id").GetInt64(), .. details.GetProperty("companions").EnumerateArray().Select(e => e.GetProperty("id").GetInt64())]
                : throw new InvalidDataException("Unclassified native modification script.");
            Program.Check("native additions are exactly the study/structural companions or two returned annotation IDs", changes.Added.Order().SequenceEqual(expectedAdded.Order())
                && (file != "copy-annotations.csx" || expectedAdded.Length == 2));
            var allowedModified = details.GetProperty("allowedModified").EnumerateArray().Select(e => e.GetProperty("id").GetInt64()).ToHashSet();
            Program.Check("every native modified identity has an explicit bounded bookkeeping role", changes.Modified.All(allowedModified.Contains));
            foreach (long value in changes.Added) Program.Check("electrical edit adds fresh native identity", added.Add(value));
        }
        Save("result-" + id + ".json", result); return result;
    }
    private void Preserve()
    {
        var hashes = new { source = Hash(Text("source")), bSource = Hash(Text("bSource")), a = Hash(Text("a")), b = Hash(Text("b")), manifest = Hash(Path.Combine(Text("bundle"), "manifest.json")) }; Log(new { name = "preservation readback", hashes });
        Program.Check("electrical source, handoff source and saved copies preserve exact expected hashes", hashes.source == Text("sourceSha256") && hashes.bSource == Text("bSourceSha256") && hashes.a == savedA && hashes.b == Text("bSourceSha256"));
        Program.Check("electrical installed bundle preserves immutable manifest", hashes.manifest == Text("manifestSha256"));
    }
    private void Payloads()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(Text("bundle"), "manifest.json")));
        foreach (var item in manifest.RootElement.GetProperty("files").EnumerateObject())
            if (!Hash(Path.Combine(Text("bundle"), item.Name)).Equals(item.Value.GetString(), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Installed payload changed: " + item.Name);
    }
    private static JsonElement Value(JsonElement result) => result.GetProperty("execution").GetProperty("value");
    private static string DocumentId(JsonElement result) => result.GetProperty("documentLifecycle").GetProperty("documentId").GetString()!;
}
