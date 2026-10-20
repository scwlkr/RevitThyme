using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using ModelContextProtocol.Client;
using RevitThyme.Core;

namespace RevitThyme.McpTests;

// Fixture authoring/explicit teardown belongs to the probe; toolkit calls use installed SDK stdio.
internal static class RestrictedDocumentsLive
{
    private sealed record NativeDocument(string Title, string Path, bool IsActive, bool IsModified, bool IsFamilyDocument,
        bool IsLinked, bool IsWorkshared, bool IsReadOnly, bool HasSavedPath, bool IsReadOnlyFile);

    public static async Task Run(string folder)
    {
        if (!Path.IsPathFullyQualified(folder)) throw new ArgumentException("Use an absolute owned run folder.");
        using var inputJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "input.json"))); var input = inputJson.RootElement;
        string Text(string key) => input.GetProperty(key).GetString()!;
        string bundle = Text("bundle"), session = Text("session"), a = Text("a"), w = Text("w"), r = Text("r"), link = Text("link"), family = Text("family");
        string source = Text("source"), template = Text("template"), expectedSave = Text("expectSaveCode"), executable = Path.Combine(bundle, "mcp", "revitthyme-mcp.exe");
        var binding = SessionBinding.Load(session); var calls = new List<object>();
        void Save(string name, object value) => File.WriteAllText(Path.Combine(folder, name), JsonSerializer.Serialize(value, Protocol.Json));
        void Log(object value) { calls.Add(value); Save("transcript.json", calls); }
        using var plan = await Receipt("plan.json"); using var readyJson = await Receipt("ready.json"); var ready = readyJson.RootElement;
        Program.Check("restricted pass pins exact owned protocol-13 native session and save expectation", Text("folder") == folder && binding.ProtocolVersion == 13
            && binding.ProcessId == input.GetProperty("pid").GetInt32() && binding.ProcessStart == Text("start") && expectedSave is "lifecycle_unconfirmed" or "read_only_file");
        foreach (string key in new[] { "folder", "bundle", "manifestSha256", "source", "sourceSha256", "template", "templateSha256", "a", "b", "w", "r", "link", "family" })
            Program.Check("restricted preparation pins " + key, plan.RootElement.GetProperty(key).GetString() == Text(key));
        string[] ownedPaths = [a, Text("b"), w, r, link, family];
        Program.Check("all restricted fixtures are distinct owned local run files", ownedPaths.All(p => Path.IsPathFullyQualified(p) && Path.GetDirectoryName(p) == folder)
            && ownedPaths.Distinct(StringComparer.OrdinalIgnoreCase).Count() == ownedPaths.Length && !ownedPaths.Contains(source, StringComparer.OrdinalIgnoreCase));
        var hashes = ready.GetProperty("hashes").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!, StringComparer.OrdinalIgnoreCase);
        Program.Check("independent fixture author records every final saved file hash", ownedPaths.All(hashes.ContainsKey));
        void Files()
        {
            var actual = hashes.Keys.ToDictionary(path => path, Hash, StringComparer.OrdinalIgnoreCase);
            string sourceHash = Hash(source), templateHash = Hash(template), manifestHash = Hash(Path.Combine(bundle, "manifest.json"));
            Log(new { channel = "preservation readback", actual, sourceHash, templateHash, manifestHash });
            Program.Check("native fixture hashes, source and template remain exact", actual.All(p => p.Value == hashes[p.Key]) && sourceHash == Text("sourceSha256") && templateHash == Text("templateSha256"));
            Program.Check("restricted SDK uses pinned immutable installed manifest", manifestHash == Text("manifestSha256"));
            using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(bundle, "manifest.json")));
            foreach (var item in manifest.RootElement.GetProperty("files").EnumerateObject())
                if (!Hash(Path.Combine(bundle, item.Name)).Equals(item.Value.GetString(), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Installed payload changed: " + item.Name);
        }
        NativeDocument[] Documents(JsonElement receipt) => receipt.GetProperty("documents").EnumerateArray().Select(d => d.Deserialize<NativeDocument>(Protocol.Json)!).ToArray();
        static object ProductFields(NativeDocument d) => new { d.Title, d.Path, d.IsActive, d.IsModified, d.IsFamilyDocument, d.IsLinked, d.IsWorkshared, d.IsReadOnly, d.HasSavedPath };
        static object ProductFieldsFromSdk(OpenDocumentInfo d) => new { d.Title, d.Path, d.IsActive, d.IsModified, d.IsFamilyDocument, d.IsLinked, d.IsWorkshared, d.IsReadOnly, d.HasSavedPath };
        static string[] Fields(IEnumerable<object> docs) => docs.Select(d => JsonSerializer.Serialize(d, Protocol.Json)).Order(StringComparer.Ordinal).ToArray();
        static bool Same(IEnumerable<NativeDocument> left, IEnumerable<NativeDocument> right) => Fields(left.Cast<object>()).SequenceEqual(Fields(right.Cast<object>()));
        void Pin(JsonElement receipt, Snapshot? snapshot = null)
        {
            Program.Check("native oracle pins process and exact active document/view", receipt.GetProperty("pid").GetInt32() == binding.ProcessId
                && receipt.GetProperty("start").GetString() == binding.ProcessStart && receipt.GetProperty("activePath").GetString() == (snapshot?.DocumentPath ?? a)
                && receipt.GetProperty("activeView").GetString() == (snapshot?.Target.ViewId ?? ready.GetProperty("activeView").GetString()));
            Program.Check("native oracle retains authored hash baseline", receipt.GetProperty("hashes").EnumerateObject().Count() == hashes.Count
                && receipt.GetProperty("hashes").EnumerateObject().All(p => hashes.TryGetValue(p.Name, out string? hash) && p.Value.GetString() == hash));
        }
        Pin(ready); Files(); var initialNative = Documents(ready); Log(new { channel = "independent native baseline", documents = initialNative });
        Program.Check("actual native fixtures include active A, linked document, workshared W, file-read-only R and saved family", initialNative.Length == 5
            && initialNative.Single(d => d.Path == a) is { IsActive: true, IsModified: false, IsWorkshared: false, IsLinked: false }
            && initialNative.Single(d => d.Path == link) is { IsLinked: true, IsActive: false }
            && initialNative.Single(d => d.Path == w) is { IsWorkshared: true, IsActive: false, IsModified: false }
            && initialNative.Single(d => d.Path == r) is { IsReadOnlyFile: true, IsActive: false, IsModified: false }
            && initialNative.Single(d => d.Path == family) is { IsFamilyDocument: true, HasSavedPath: true, IsActive: false });
        async Task<JsonElement> Command(string action, Snapshot snapshot)
        {
            string id = Guid.NewGuid().ToString(); var intent = new { id, action }; Log(new { channel = "test-only fixture command", phase = "submitted", intent });
            Save("command-" + id + ".json", intent); var watch = Stopwatch.StartNew(); using var json = await Receipt("receipt-" + id + ".json"); var receipt = json.RootElement.Clone();
            Log(new { channel = "test-only fixture command", phase = "returned", intent, milliseconds = watch.Elapsed.TotalMilliseconds, receipt });
            Program.Check("probe acknowledges exact " + action, receipt.GetProperty("id").GetString() == id && receipt.GetProperty("action").GetString() == action); Pin(receipt, snapshot); return receipt;
        }
        async Task<McpClient> Client(string actor) => await McpClient.CreateAsync(new StdioClientTransport(new()
        { Command = executable, Arguments = ["--session", session], Name = "Restricted documents " + actor }));
        var client = await Client("initial");
        try
        {
            async Task<JsonElement> Call(string name, Dictionary<string, object?>? args = null, string? code = null)
            {
                Log(new { channel = "installed MCP", phase = "submitted", name, args, utc = DateTime.UtcNow }); var watch = Stopwatch.StartNew();
                try
                {
                    var reply = await Program.Call(client, name, args).WaitAsync(TimeSpan.FromSeconds(45));
                    Log(new { channel = "installed MCP", phase = "returned", name, args, milliseconds = watch.Elapsed.TotalMilliseconds, reply.Data, reply.Result.IsError });
                    Program.Check("restricted " + name + (code is null ? " succeeds" : ": " + code), code is null ? reply.Result.IsError != true
                        : reply.Data.GetProperty("code").GetString() == code && reply.Result.IsError == (code is not ("inspected" or "document_opened" or "document_activated" or "applied")));
                    return reply.Data;
                }
                catch (Exception error) { Log(new { channel = "installed MCP", phase = "threw", name, args, milliseconds = watch.Elapsed.TotalMilliseconds, error = error.GetType().Name, error.Message }); throw; }
            }
            async Task<(string Id, Snapshot Snapshot)> Context()
            { var data = await Call("get_context"); return (data.GetProperty("captureId").GetString()!, data.GetProperty("result").GetProperty("snapshot").Deserialize<Snapshot>(Protocol.Json)!); }
            Dictionary<string, object?> Query(string capture, int offset = 0, int limit = 50, string? token = null, string? id = null) =>
                new() { ["captureId"] = capture, ["requestId"] = id ?? Guid.NewGuid().ToString(), ["offset"] = offset, ["limit"] = limit, ["inventoryToken"] = token };
            Dictionary<string, object?> Lifecycle(string capture, string path, string? document = null, bool hash = false, string? id = null)
            {
                var args = new Dictionary<string, object?> { ["captureId"] = capture, ["requestId"] = id ?? Guid.NewGuid().ToString(), ["path"] = path };
                if (document is not null) args["documentId"] = document; if (hash) args["expectedSha256"] = hashes[path]; return args;
            }
            static DocumentPage Page(JsonElement data) => data.GetProperty("documents").Deserialize<DocumentPage>(Protocol.Json)!;
            async Task<(OpenDocumentInfo[] Items, string Token)> Pages(string capture, NativeDocument[] native)
            {
                int offset = 0; string? token = null; var items = new List<OpenDocumentInfo>();
                while (true)
                {
                    var page = Page(await Call("list_documents", Query(capture, offset, 1, token), "inspected"));
                    Program.Check("restricted pages keep exact count and stable inventory token", page.Total == native.Length && page.Items.Length == 1
                        && page.InventoryToken.Length == 64 && (token is null || token == page.InventoryToken));
                    token ??= page.InventoryToken; items.AddRange(page.Items); if (page.NextOffset is not { } next) break;
                    Program.Check("restricted one-item pages advance exactly once", next == offset + 1 && next < native.Length); offset = next;
                }
                Program.Check("all current product fields match independent native metadata by path/family/title", items.Count == native.Length
                    && items.Select(d => d.DocumentId).All(id => !string.IsNullOrWhiteSpace(id)) && items.Select(d => d.DocumentId).Distinct(StringComparer.Ordinal).Count() == items.Count
                    && items.Select(d => (d.Path, d.IsFamilyDocument, d.Title)).Distinct().Count() == items.Count
                    && Fields(items.Select(ProductFieldsFromSdk)).SequenceEqual(Fields(native.Select(ProductFields))));
                return (items.ToArray(), token!);
            }
            var tools = await client.ListToolsAsync(); Log(new { channel = "installed MCP", name = "list_tools", tools = tools.Select(t => new { t.Name, t.ProtocolTool.Annotations, schema = t.JsonSchema }) });
            Program.Check("restricted pass uses actual installed 32-tool protocol-13 server", tools.Count == 32
                && tools.Single(t => t.Name == "list_documents").ProtocolTool.Annotations is { ReadOnlyHint: true, IdempotentHint: true }
                && (await Call("list_operations")).GetProperty("protocolVersion").GetInt32() == 13);
            var context = await Context(); var baseline = context.Snapshot; Pin(ready, baseline);
            var before = await Command("snapshot", baseline); Program.Check("independent initial snapshot matches authored fixtures", Same(Documents(before), initialNative));
            var initialPages = await Pages(context.Id, initialNative); string readId = Guid.NewGuid().ToString();
            var originalRead = await Call("list_documents", Query(context.Id, id: readId), "inspected");
            Program.Check("original complete inventory is retained under caller GUID", originalRead.GetProperty("id").GetString() == readId && Page(originalRead).Items.SequenceEqual(initialPages.Items));
            var discoveredA = initialPages.Items.Single(d => d.Path == a); var discoveredR = initialPages.Items.Single(d => d.Path == r);
            foreach (string path in new[] { link, w })
            {
                string document = initialPages.Items.Single(d => d.Path == path).DocumentId;
                await Call("open_document", Lifecycle(context.Id, path, hash: true), "lifecycle_blocked");
                await Call("activate_document", Lifecycle(context.Id, path, document), "lifecycle_blocked");
                await Call("close_document", Lifecycle(context.Id, path, document, true), "lifecycle_blocked");
                await Call("activate_document", Lifecycle(context.Id, path, Guid.NewGuid().ToString()), "wrong_document");
            }
            await Call("activate_document", Lifecycle(context.Id, a, discoveredR.DocumentId), "wrong_document");
            var afterReads = await Command("snapshot", baseline);
            Program.Check("discovery and unsupported-target refusals preserve every native document and exact A/view", Same(Documents(afterReads), initialNative) && (await Context()).Snapshot == baseline); Files();
            var worksharedClosed = await Command("close-workshared", baseline); var remaining = Documents(worksharedClosed);
            Program.Check("explicit fixture teardown removes only W without saved-file mutation", remaining.Length == 4 && Same(remaining, initialNative.Where(d => d.Path != w)));
            var fresh = await Context(); await Call("list_documents", Query(context.Id), "stale_target");
            await Call("list_documents", Query(fresh.Id, token: initialPages.Token), "stale_inventory");
            await Call("open_document", Lifecycle(fresh.Id, w, hash: true), "unsupported_document");
            await client.DisposeAsync(); client = await Client("recovered-completed-read");
            Program.Check("retained completed inventory survives client replacement after membership change", (await Call("get_operation", new() { ["requestId"] = readId }, "inspected")).GetRawText() == originalRead.GetRawText());
            Program.Check("retained completed read recovery is byte-stable", (await Call("get_operation", new() { ["requestId"] = readId }, "inspected")).GetRawText() == originalRead.GetRawText());
            fresh = await Context(); var current = await Pages(fresh.Id, remaining);
            Program.Check("current inventory preserves identities of still-open native objects", current.Items.All(d => initialPages.Items.Any(old => old.DocumentId == d.DocumentId && old.Path == d.Path)));
            var openedR = await Call("open_document", Lifecycle(fresh.Id, r, hash: true), "document_opened");
            Program.Check("existing file-read-only open is an exact no-op receipt", openedR.GetProperty("documentLifecycle").GetProperty("documentId").GetString() == discoveredR.DocumentId
                && !openedR.GetProperty("documentLifecycle").GetProperty("performed").GetBoolean() && (await Context()).Snapshot == fresh.Snapshot);
            await Call("activate_document", Lifecycle(fresh.Id, r, discoveredR.DocumentId), "document_activated"); var rContext = await Context(); string originalName = rContext.Snapshot.ViewName;
            var activeROracle = await Command("snapshot", rContext.Snapshot); var rNative = Documents(activeROracle).Single(d => d.Path == r);
            Program.Check("native active R distinguishes file read-only constraint from dynamic document state", rNative.IsReadOnlyFile && !rNative.IsReadOnly && rNative.IsActive && !rNative.IsModified
                && rContext.Snapshot.Target.DocumentId == discoveredR.DocumentId && rContext.Snapshot.DocumentPath == r);
            string renameId = Guid.NewGuid().ToString(), saveId = Guid.NewGuid().ToString();
            Save("readonly-edit-intent.json", new { path = r, documentId = discoveredR.DocumentId, originalName, renameId, saveId, expectedSave });
            await Call("rename_view", new() { ["captureId"] = rContext.Id, ["requestId"] = renameId, ["name"] = "Read-only file baseline " + renameId[..8] }, "applied");
            rContext = await Context(); Program.Check("owned R edit is native dirty before explicit save attempt", rContext.Snapshot.IsModified && Hash(r) == hashes[r]);
            var saveResult = await Call("save_document", Lifecycle(rContext.Id, r, discoveredR.DocumentId, true, saveId), expectedSave);
            Program.Check("read-only save outcome keeps exact original GUID and required certainty", saveResult.GetProperty("id").GetString() == saveId
                && saveResult.GetProperty("outcome").GetString() == (expectedSave == "lifecycle_unconfirmed" ? "Unconfirmed" : "Rejected"));
            Program.Check("save recovery returns exact original outcome without retry", (await Call("get_operation", new() { ["requestId"] = saveId }, expectedSave)).GetRawText() == saveResult.GetRawText());
            rContext = await Context(); var afterSave = await Command("snapshot", rContext.Snapshot); var afterSaveR = Documents(afterSave).Single(d => d.Path == r);
            Program.Check("failed or preflight-refused read-only save preserves dirty native R and all authored disk hashes", rContext.Snapshot.IsModified && afterSaveR.IsModified
                && afterSaveR.IsReadOnlyFile && !afterSaveR.IsReadOnly && rContext.Snapshot.Target.DocumentId == discoveredR.DocumentId); Files();
            await Call("rename_view", new() { ["captureId"] = rContext.Id, ["requestId"] = Guid.NewGuid().ToString(), ["name"] = originalName }, "applied");
            rContext = await Context(); Program.Check("owned R exact original view name restored without a save", rContext.Snapshot.ViewName == originalName && rContext.Snapshot.IsModified && Hash(r) == hashes[r]);
            await Call("activate_document", Lifecycle(rContext.Id, a, discoveredA.DocumentId), "document_activated"); fresh = await Context();
            var cleanup = await Command("cleanup", fresh.Snapshot); var finalNative = Documents(cleanup);
            Program.Check("explicit known-fixture teardown leaves only unchanged clean A and its actual link", finalNative.Length == 2 && Same(finalNative, initialNative.Where(d => d.Path == a || d.Path == link)));
            var final = await Context(); var finalPages = await Pages(final.Id, finalNative);
            Program.Check("final active clean A and original view match authored baseline", final.Snapshot.Target.DocumentId == discoveredA.DocumentId
                && final.Snapshot.Target.ViewId == baseline.Target.ViewId && final.Snapshot.ViewName == baseline.ViewName && final.Snapshot.DocumentPath == a && !final.Snapshot.IsModified);
            Program.Check("old completed read still preserves original five-document point-in-time inventory", (await Call("get_operation", new() { ["requestId"] = readId }, "inspected")).GetRawText() == originalRead.GetRawText());
            var finalOracle = await Command("snapshot", final.Snapshot); Program.Check("final pure reads preserve independent native state", Same(Documents(finalOracle), finalNative)); Files();
            Save("complete.json", new { passed = true, binding, expectedSave, savedA = hashes[a], savedB = hashes[Text("b")], hashes, final.Snapshot, readId, saveId, saveResult,
                resultSource = "immutable installed SDK; independent native metadata including IsReadOnlyFile; completed-read recovery after client replacement, no interrupted-request claim",
                observedDynamicReadOnly = initialNative.Where(d => d.IsReadOnly).Select(d => d.Path), originalRead, initialNative, final = finalPages.Items, calls });
        }
        finally { await client.DisposeAsync(); }

        async Task<JsonDocument> Receipt(string name)
        {
            var watch = Stopwatch.StartNew(); string path = Path.Combine(folder, name);
            while (watch.Elapsed.TotalSeconds < 60)
            {
                string? failure = null; string errorPath = Path.Combine(folder, "probe-error.json");
                if (File.Exists(errorPath))
                {
                    try { using var json = JsonDocument.Parse(await File.ReadAllTextAsync(errorPath)); failure = json.RootElement.GetRawText(); }
                    catch (Exception error) when (error is IOException or JsonException) { await Task.Delay(100); continue; }
                }
                if (failure is not null) throw new InvalidDataException("Restricted probe failed while waiting for " + name + ": " + failure);
                try { return JsonDocument.Parse(await File.ReadAllTextAsync(path)); }
                catch (Exception error) when (error is IOException or JsonException) { await Task.Delay(100); }
            }
            throw new TimeoutException("Restricted qualification receipt missing: " + name);
        }
    }
    private static string Hash(string path) { using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); return Convert.ToHexStringLower(SHA256.HashData(stream)); }
}
