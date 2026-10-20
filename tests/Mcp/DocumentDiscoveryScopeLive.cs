using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using ModelContextProtocol.Client;
using RevitThyme.Core;

namespace RevitThyme.McpTests;

// Probe commands author/clean owned fixtures only; all toolkit assertions use installed SDK stdio.
internal static class DocumentDiscoveryScopeLive
{
    private sealed record NativeDocument(string Title, string Path, bool IsActive, bool IsModified, bool IsFamilyDocument,
        bool IsLinked, bool IsWorkshared, bool IsReadOnly, bool HasSavedPath);

    public static async Task Run(string folder)
    {
        if (!Path.IsPathFullyQualified(folder)) throw new ArgumentException("Use an absolute owned run folder.");
        using var inputJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "input.json"))); var input = inputJson.RootElement;
        string Text(string key) => input.GetProperty(key).GetString()!;
        string mode = Text("mode"), bundle = Text("bundle"), session = Text("session"), a = Text("a"), b = Text("b");
        string source = Text("source"), original = Text("sourceSha256"), template = Text("template"), templateHash = Text("templateSha256");
        string executable = Path.Combine(bundle, "mcp", "revitthyme-mcp.exe"); bool filtered = mode == "fixture";
        var binding = SessionBinding.Load(session); var calls = new List<object>();
        void Save(string name, object value) => File.WriteAllText(Path.Combine(folder, name), JsonSerializer.Serialize(value, Protocol.Json));
        void Log(object value) { calls.Add(value); Save("transcript.json", calls); }
        using var plan = await Receipt("plan.json"); using var readyJson = await Receipt("ready.json"); var ready = readyJson.RootElement;
        Program.Check("scope qualification pins selected mode, owned session and fixture locations", mode is "normal" or "fixture"
            && Text("folder") == folder && binding.ProtocolVersion == 13 && binding.ProcessId == input.GetProperty("pid").GetInt32() && binding.ProcessStart == Text("start")
            && Path.GetDirectoryName(a) == folder && Path.GetDirectoryName(b) == folder && a != b && source != a && source != b);
        foreach (string key in new[] { "folder", "mode", "bundle", "manifestSha256", "a", "b", "source", "sourceSha256", "template", "templateSha256" })
            Program.Check("scope preparation pins " + key, plan.RootElement.GetProperty(key).GetString() == Text(key));
        void Files()
        {
            var hashes = new { a = Hash(a), b = Hash(b), source = Hash(source), template = Hash(template), manifest = Hash(Path.Combine(bundle, "manifest.json")) };
            Log(new { channel = "preservation readback", hashes });
            Program.Check("scope read and refusal preserve source, template and both saved copies", hashes.a == original && hashes.b == original
                && hashes.source == original && hashes.template == templateHash);
            Program.Check("scope SDK uses exact installed immutable bundle", hashes.manifest == Text("manifestSha256"));
            using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(bundle, "manifest.json")));
            foreach (var item in manifest.RootElement.GetProperty("files").EnumerateObject())
                if (!Hash(Path.Combine(bundle, item.Name)).Equals(item.Value.GetString(), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Installed payload changed: " + item.Name);
        }
        Files();
        NativeDocument[] Documents(JsonElement receipt) => receipt.GetProperty("documents").EnumerateArray().Select(d => d.Deserialize<NativeDocument>(Protocol.Json)!).ToArray();
        void Pin(JsonElement receipt)
        {
            Program.Check("independent native receipt pins owned process and exact active A/view", receipt.GetProperty("pid").GetInt32() == binding.ProcessId
                && receipt.GetProperty("start").GetString() == binding.ProcessStart && receipt.GetProperty("activePath").GetString() == a
                && receipt.GetProperty("activeView").GetString() == ready.GetProperty("activeView").GetString());
        }
        Pin(ready); var initialNative = Documents(ready);
        Program.Check("independent probe prepared saved A/B, unsaved project and unsaved family", initialNative.Length == 4
            && initialNative.Count(d => d.Path == a && d.IsActive && !d.IsModified && !d.IsFamilyDocument) == 1
            && initialNative.Count(d => d.Path == b && !d.IsActive && !d.IsModified && !d.IsFamilyDocument) == 1
            && initialNative.Count(d => d.Path == "" && !d.IsFamilyDocument && !d.HasSavedPath && !d.IsActive) == 1
            && initialNative.Count(d => d.Path == "" && d.IsFamilyDocument && !d.HasSavedPath && !d.IsActive) == 1
            && initialNative.All(d => !d.IsLinked && !d.IsWorkshared && !d.IsReadOnly));
        static NativeDocument Native(OpenDocumentInfo d) => new(d.Title, d.Path, d.IsActive, d.IsModified, d.IsFamilyDocument, d.IsLinked, d.IsWorkshared, d.IsReadOnly, d.HasSavedPath);
        static NativeDocument[] Sort(IEnumerable<NativeDocument> docs) => docs.OrderBy(d => d.Path, StringComparer.Ordinal).ThenBy(d => d.IsFamilyDocument).ThenBy(d => d.Title, StringComparer.Ordinal).ToArray();
        static bool Same(IEnumerable<NativeDocument> left, IEnumerable<NativeDocument> right) => Sort(left).SequenceEqual(Sort(right));
        NativeDocument[] Visible(IEnumerable<NativeDocument> docs) => docs.Where(d => !filtered || d.Path == a).ToArray();
        async Task<JsonElement> Command(string action)
        {
            string id = Guid.NewGuid().ToString(); var intent = new { id, action }; Log(new { channel = "test-only fixture command", phase = "submitted", intent });
            Save("command-" + id + ".json", intent); var watch = Stopwatch.StartNew(); using var receipt = await Receipt("receipt-" + id + ".json"); var r = receipt.RootElement.Clone();
            Log(new { channel = "test-only fixture command", phase = "returned", intent, milliseconds = watch.Elapsed.TotalMilliseconds, receipt = r });
            Program.Check("probe acknowledges only exact " + action + " command", r.GetProperty("id").GetString() == id && r.GetProperty("action").GetString() == action); Pin(r); return r;
        }
        async Task<McpClient> Client(string actor) => await McpClient.CreateAsync(new StdioClientTransport(new()
        { Command = executable, Arguments = ["--session", session], Name = "Document scope " + mode + " " + actor }));
        var client = await Client("initial");
        try
        {
            async Task<JsonElement> Call(string name, Dictionary<string, object?>? args = null, string? code = null)
            {
                var watch = Stopwatch.StartNew(); Log(new { channel = "installed MCP", phase = "submitted", name, args, utc = DateTime.UtcNow });
                try
                {
                    var r = await Program.Call(client, name, args); Log(new { channel = "installed MCP", phase = "returned", name, args, milliseconds = watch.Elapsed.TotalMilliseconds, r.Data, r.Result.IsError });
                    Program.Check("scope " + name + (code is null ? " succeeds" : ": " + code), code is null ? r.Result.IsError != true
                        : r.Data.GetProperty("code").GetString() == code && r.Result.IsError == (code is not ("inspected" or "document_closed")));
                    return r.Data;
                }
                catch (Exception error) { Log(new { channel = "installed MCP", phase = "threw", name, args, milliseconds = watch.Elapsed.TotalMilliseconds, error = error.GetType().Name, error.Message }); throw; }
            }
            async Task<(string Id, Snapshot Snapshot)> Context()
            { var r = await Call("get_context"); return (r.GetProperty("captureId").GetString()!, r.GetProperty("result").GetProperty("snapshot").Deserialize<Snapshot>(Protocol.Json)!); }
            Dictionary<string, object?> Query(string capture, int offset = 0, int limit = 50, string? token = null, string? id = null) =>
                new() { ["captureId"] = capture, ["requestId"] = id ?? Guid.NewGuid().ToString(), ["offset"] = offset, ["limit"] = limit, ["inventoryToken"] = token };
            Dictionary<string, object?> Lifecycle(string capture, string path, string document, bool hash = false)
            {
                var args = new Dictionary<string, object?> { ["captureId"] = capture, ["requestId"] = Guid.NewGuid().ToString(), ["documentId"] = document, ["path"] = path };
                if (hash) args["expectedSha256"] = original; return args;
            }
            static DocumentPage Page(JsonElement data) => data.GetProperty("documents").Deserialize<DocumentPage>(Protocol.Json)!;
            async Task<(OpenDocumentInfo[] Items, string Token)> Pages(string capture, NativeDocument[] expected)
            {
                var items = new List<OpenDocumentInfo>(); string? token = null; int offset = 0;
                while (true)
                {
                    var page = Page(await Call("list_documents", Query(capture, offset, 1, token), "inspected"));
                    Program.Check("stable scope page has exact count and consistent inventory token", page.Total == expected.Length && page.Items.Length == 1
                        && (token is null || token == page.InventoryToken) && page.InventoryToken.Length == 64);
                    token ??= page.InventoryToken; items.AddRange(page.Items);
                    if (page.NextOffset is not { } next) break;
                    Program.Check("one-item scope pagination advances exactly once", next == offset + 1 && next < expected.Length); offset = next;
                }
                Program.Check("enumerated metadata exactly matches independent native receipt by path/family/title", items.Count == expected.Length
                    && items.Select(d => d.DocumentId).All(id => !string.IsNullOrWhiteSpace(id)) && items.Select(d => d.DocumentId).Distinct(StringComparer.Ordinal).Count() == items.Count
                    && items.Select(d => (d.Path, d.IsFamilyDocument, d.Title)).Distinct().Count() == items.Count && Same(items.Select(Native), expected));
                return (items.ToArray(), token!);
            }
            var tools = await client.ListToolsAsync(); Log(new { channel = "installed MCP", name = "list_tools", tools = tools.Select(t => new { t.Name, t.ProtocolTool.Annotations, schema = t.JsonSchema }) });
            Program.Check("scope pass uses actual installed 32-tool protocol 13 SDK server", tools.Count == 32
                && tools.Single(t => t.Name == "list_documents").ProtocolTool.Annotations is { ReadOnlyHint: true, IdempotentHint: true }
                && (await Call("list_operations")).GetProperty("protocolVersion").GetInt32() == 13);
            var context = await Context(); var baseline = context.Snapshot;
            Program.Check("toolkit context agrees with independent active saved A", baseline.DocumentPath == a && baseline.Target.ViewId == ready.GetProperty("activeView").GetString() && !baseline.IsModified);
            var before = await Command("snapshot"); Program.Check("probe initial snapshot is unchanged", Same(Documents(before), initialNative));
            var expectedInitial = Visible(initialNative); var initialPages = await Pages(context.Id, expectedInitial);
            string readId = Guid.NewGuid().ToString(); var originalRead = await Call("list_documents", Query(context.Id, id: readId), "inspected");
            Program.Check("caller GUID retains complete original visible scope", originalRead.GetProperty("id").GetString() == readId
                && originalRead.GetProperty("outcome").GetString() == "Inspected" && Page(originalRead).Items.SequenceEqual(initialPages.Items));
            var discoveredA = initialPages.Items.Single(d => d.Path == a);
            foreach (string name in new[] { "activate_document", "save_document", "close_document" })
            {
                await Call(name, Lifecycle(context.Id, a, Guid.NewGuid().ToString(), hash: name != "activate_document"), "wrong_document");
                await Call(name, Lifecycle(context.Id, "", discoveredA.DocumentId, hash: name != "activate_document"), "invalid_lifecycle");
            }
            await Call("activate_document", Lifecycle(context.Id, b, discoveredA.DocumentId), filtered ? "wrong_fixture" : "wrong_document");
            if (filtered)
                await Call("open_document", new() { ["captureId"] = context.Id, ["requestId"] = Guid.NewGuid().ToString(), ["path"] = b, ["expectedSha256"] = original }, "wrong_fixture");
            else
                foreach (var unsaved in initialPages.Items.Where(d => !d.HasSavedPath))
                {
                    await Call("activate_document", Lifecycle(context.Id, a, unsaved.DocumentId), "wrong_document");
                    await Call("save_document", Lifecycle(context.Id, a, unsaved.DocumentId, true), "wrong_document");
                    await Call("activate_document", Lifecycle(context.Id, "", unsaved.DocumentId), "invalid_lifecycle");
                    await Call("close_document", Lifecycle(context.Id, "", unsaved.DocumentId, true), "invalid_lifecycle");
                }
            var afterReads = await Command("snapshot");
            Program.Check("discovery and every refused target preserve all four native documents and exact A/view", Same(Documents(afterReads), initialNative) && (await Context()).Snapshot == baseline); Files();
            var extrasClosed = await Command("close-extra"); var remaining = Documents(extrasClosed);
            Program.Check("test-only close-extra removes only the two unsaved fixtures", remaining.Length == 2 && Same(remaining, initialNative.Where(d => d.Path == a || d.Path == b)));
            var fresh = await Context(); Program.Check("fixture cleanup preserves exact saved A identity/view/state", fresh.Snapshot.Target.DocumentId == discoveredA.DocumentId
                && fresh.Snapshot.Target.ViewId == baseline.Target.ViewId && fresh.Snapshot.DocumentPath == a && !fresh.Snapshot.IsModified);
            await Call("list_documents", Query(context.Id), "stale_target");
            var changed = await Call("list_documents", Query(fresh.Id, token: initialPages.Token), filtered ? "inspected" : "stale_inventory");
            if (filtered) Program.Check("hidden document membership does not alter fixture-visible token or metadata", Page(changed).InventoryToken == initialPages.Token && Page(changed).Items.SequenceEqual(initialPages.Items));
            await client.DisposeAsync(); client = await Client("reconnected-after-completed-read");
            var recovered = await Call("get_operation", new() { ["requestId"] = readId }, "inspected");
            Program.Check("completed read survives external client disposal as original point-in-time result", recovered.GetRawText() == originalRead.GetRawText()
                && recovered.GetProperty("id").GetString() == readId && Page(recovered).Total == expectedInitial.Length);
            Program.Check("completed read recovery is repeatable without a new discovery", (await Call("get_operation", new() { ["requestId"] = readId }, "inspected")).GetRawText() == originalRead.GetRawText());
            fresh = await Context(); var current = await Pages(fresh.Id, Visible(remaining));
            Program.Check("fresh read preserves IDs of every still-visible saved document", current.Items.All(d => initialPages.Items.Any(previous => previous.DocumentId == d.DocumentId && previous.Path == d.Path)));
            if (!filtered)
            {
                var discoveredB = current.Items.Single(d => d.Path == b);
                await Call("close_document", Lifecycle(fresh.Id, b, discoveredB.DocumentId, true), "document_closed");
            }
            var cleanup = await Command("cleanup"); var finalNative = Documents(cleanup);
            Program.Check("explicit fixture cleanup leaves only exact saved clean active A", finalNative.Length == 1 && Same(finalNative, initialNative.Where(d => d.Path == a)));
            var final = await Context(); var finalPages = await Pages(final.Id, finalNative);
            Program.Check("final discovery leaves exact clean A/view unchanged", final.Snapshot.Target.DocumentId == discoveredA.DocumentId && final.Snapshot.Target.ViewId == baseline.Target.ViewId
                && final.Snapshot.DocumentPath == a && !final.Snapshot.IsModified && finalPages.Items.Single().DocumentId == discoveredA.DocumentId);
            var finalToken = await Call("list_documents", Query(final.Id, token: current.Token), filtered ? "inspected" : "stale_inventory");
            if (filtered) Program.Check("fixture token remains stable after hidden B cleanup", Page(finalToken).InventoryToken == initialPages.Token);
            Program.Check("old completed read remains original after every cleanup", (await Call("get_operation", new() { ["requestId"] = readId }, "inspected")).GetRawText() == originalRead.GetRawText());
            var finalOracle = await Command("snapshot"); Program.Check("final pure reads leave independent native metadata unchanged", Same(Documents(finalOracle), finalNative)); Files();
            Save("complete.json", new { passed = true, mode, binding, savedA = original, savedB = original, templateSha256 = templateHash, final.Snapshot, readId,
                resultSource = "immutable installed external SDK; independent native fixture receipts; retained completed read after client disposal, no interrupted-request claim", originalRead, current = finalPages.Items, calls });
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
                    try { using var errorJson = JsonDocument.Parse(await File.ReadAllTextAsync(errorPath)); failure = errorJson.RootElement.GetRawText(); }
                    catch (Exception error) when (error is IOException or JsonException) { await Task.Delay(100); continue; }
                }
                if (failure is not null) throw new InvalidDataException("Discovery probe failed while waiting for " + name + ": " + failure);
                try { return JsonDocument.Parse(await File.ReadAllTextAsync(path)); }
                catch (Exception error) when (error is IOException or JsonException) { await Task.Delay(100); }
            }
            throw new TimeoutException("Scope qualification receipt missing: " + name);
        }
    }
    private static string Hash(string path) { using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); return Convert.ToHexStringLower(SHA256.HashData(stream)); }
}
