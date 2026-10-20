using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using RevitThyme.Core;

namespace RevitThyme.McpTests;

// External installed SDK/CLI callers; the signed probe only holds a native test dialog open.
internal static class DocumentDiscoveryLive
{
    public static async Task Run(string folder)
    {
        if (!Path.IsPathFullyQualified(folder)) throw new ArgumentException("Use an absolute owned run folder.");
        using var inputJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "input.json"))); var input = inputJson.RootElement;
        string Text(string key) => input.GetProperty(key).GetString()!;
        string bundle = Text("bundle"), session = Text("session"), a = Text("a"), b = Text("b"), source = Text("source"), original = Text("sourceSha256");
        string executable = Path.Combine(bundle, "mcp", "revitthyme-mcp.exe"), cli = Path.Combine(bundle, "cli", "revitthyme.exe");
        var binding = SessionBinding.Load(session); var calls = new List<object>(); var logGate = new object();
        void Save(string name, object value) => File.WriteAllText(Path.Combine(folder, name), JsonSerializer.Serialize(value, Protocol.Json));
        void Log(object value) { lock (logGate) { calls.Add(value); Save("transcript.json", calls); } }
        using var plan = await Receipt("plan.json", 10); using var launch = await Receipt("launch.json", 10); using var probe = await Receipt("probe-ready.json", 10);
        Program.Check("document qualification pins protocol 13, owned process/start, copied fixtures and signed probe", binding.ProtocolVersion == 13
            && binding.ProcessId == input.GetProperty("pid").GetInt32() && binding.ProcessStart == Text("start")
            && launch.RootElement.GetProperty("pid").GetInt32() == binding.ProcessId && launch.RootElement.GetProperty("start").GetString() == binding.ProcessStart
            && launch.RootElement.GetProperty("manifestSha256").GetString() == Text("manifestSha256") && launch.RootElement.GetProperty("bundle").GetString() == bundle
            && probe.RootElement.GetProperty("pid").GetInt32() == binding.ProcessId && probe.RootElement.GetProperty("start").GetString() == binding.ProcessStart
            && plan.RootElement.GetProperty("a").GetString() == a && plan.RootElement.GetProperty("b").GetString() == b
            && plan.RootElement.GetProperty("source").GetString() == source && plan.RootElement.GetProperty("sourceSha256").GetString() == original
            && probe.RootElement.GetProperty("allowed").EnumerateArray().Select(x => x.GetString()).Order().SequenceEqual(new[] { a, b }.Order())
            && Path.GetDirectoryName(a) == folder && Path.GetDirectoryName(b) == folder && a != b && source != a && source != b);
        void Payloads()
        {
            Program.Check("installed document bundle matches immutable manifest", Hash(Path.Combine(bundle, "manifest.json")) == Text("manifestSha256"));
            using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(bundle, "manifest.json")));
            foreach (var item in manifest.RootElement.GetProperty("files").EnumerateObject())
                if (!Hash(Path.Combine(bundle, item.Name)).Equals(item.Value.GetString(), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Installed payload changed: " + item.Name);
        }
        Payloads();
        async Task<McpClient> Client(string actor) => await McpClient.CreateAsync(new StdioClientTransport(new()
        { Command = executable, Arguments = ["--session", session], Name = "Document discovery " + actor }));
        await using var owner = await Client("owner"); await using var initialAgent = await Client("initial-agent"); await using var observer = await Client("observer"); var agent = initialAgent;
        async Task<JsonElement> Call(McpClient client, string actor, string name, Dictionary<string, object?>? args = null, string? expected = null)
        {
            Log(new { phase = "submitted", actor, name, args, utc = DateTime.UtcNow }); var watch = Stopwatch.StartNew();
            try
            {
                var reply = await Program.Call(client, name, args); Log(new { phase = "returned", actor, name, args, milliseconds = watch.Elapsed.TotalMilliseconds, reply.Data, reply.Result.IsError });
                if (expected != "any") Program.Check(actor + " " + name + (expected is null ? " succeeds" : ": " + expected), expected is null ? reply.Result.IsError != true
                    : reply.Data.GetProperty("code").GetString() == expected && reply.Result.IsError == (expected is not ("inspected" or "applied" or "document_opened" or "document_activated" or "document_saved" or "document_closed" or "image_exported")));
                if (name == "export_view_image" && reply.Result.IsError != true)
                {
                    var image = reply.Data.GetProperty("imageExport").Deserialize<ViewImageResult>(Protocol.Json)!; byte[] bytes = File.ReadAllBytes(image.Path);
                    Program.Check("native 512px PNG, inline bytes, hash and dimensions agree", image.ViewId == reply.Data.GetProperty("snapshot").Deserialize<Snapshot>(Protocol.Json)!.Target.ViewId
                        && Hash(image.Path) == image.Sha256 && bytes.LongLength == image.Bytes && Math.Max(image.Width, image.Height) == 512
                        && reply.Result.Content.OfType<ImageContentBlock>().Single().DecodedData.Span.SequenceEqual(bytes)
                        && System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4)) == image.Width
                        && System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4)) == image.Height);
                }
                return reply.Data;
            }
            catch (Exception error) { Log(new { phase = "threw", actor, name, args, milliseconds = watch.Elapsed.TotalMilliseconds, error = error.GetType().Name, error.Message }); throw; }
        }
        async Task<(string Id, Snapshot Snapshot)> Context(McpClient client, string actor)
        { var r = await Call(client, actor, "get_context"); return (r.GetProperty("captureId").GetString()!, r.GetProperty("result").GetProperty("snapshot").Deserialize<Snapshot>(Protocol.Json)!); }
        Dictionary<string, object?> Query(string capture, int offset = 0, int limit = 50, string? token = null, string? id = null) =>
            new() { ["captureId"] = capture, ["requestId"] = id ?? Guid.NewGuid().ToString(), ["offset"] = offset, ["limit"] = limit, ["inventoryToken"] = token };
        Dictionary<string, object?> Lifecycle(string capture, string path, string? document = null, string? hash = null, string? id = null)
        {
            var args = new Dictionary<string, object?> { ["captureId"] = capture, ["requestId"] = id ?? Guid.NewGuid().ToString(), ["path"] = path };
            if (document is not null) args["documentId"] = document; if (hash is not null) args["expectedSha256"] = hash; return args;
        }
        static DocumentPage Page(JsonElement r) => r.GetProperty("documents").Deserialize<DocumentPage>(Protocol.Json)!;
        static DocumentLifecycleResult Doc(JsonElement r) => r.GetProperty("documentLifecycle").Deserialize<DocumentLifecycleResult>(Protocol.Json)!;
        async Task<DocumentPage> List(string capture, int offset = 0, int limit = 50, string? token = null) => Page(await Call(agent, "agent", "list_documents", Query(capture, offset, limit, token), "inspected"));
        async Task<JsonElement> Recover(McpClient client, string actor, string id, bool pending = false)
        {
            var clock = Stopwatch.StartNew(); var r = await Call(client, actor, "get_operation", new() { ["requestId"] = id }, "any");
            if (pending) Program.Check("original read remains Pending promptly during native modal", r.GetProperty("code").GetString() == "pending" && clock.Elapsed < TimeSpan.FromSeconds(2));
            return r;
        }
        async Task<JsonElement> Terminal(McpClient client, string actor, string id)
        {
            var watch = Stopwatch.StartNew(); JsonElement r;
            do { r = await Recover(client, actor, id); if (r.GetProperty("code").GetString() != "pending") return r; await Task.Delay(50); } while (watch.Elapsed.TotalSeconds < 20);
            throw new TimeoutException("Original operation still Pending: " + id);
        }
        var inventory = await agent.ListToolsAsync(); Log(new { actor = "agent", name = "list_tools", tools = inventory.Select(t => new { t.Name, t.ProtocolTool.Annotations, schema = t.JsonSchema }) });
        Program.Check("installed SDK discovers 32 tools with read-only retained document discovery", inventory.Count == 32
            && inventory.Single(t => t.Name == "list_documents").ProtocolTool.Annotations is { ReadOnlyHint: true, IdempotentHint: true }
            && (await Call(agent, "agent", "list_operations")).GetProperty("protocolVersion").GetInt32() == 13);
        var home = await Context(owner, "owner"); var initial = await Context(agent, "agent");
        Program.Check("qualification starts at exact Home with empty native inventory and pristine copies", !home.Snapshot.HasDocument && initial.Snapshot == home.Snapshot
            && (await List(initial.Id)).Total == 0 && Hash(a) == original && Hash(b) == original && Hash(source) == original);
        foreach (var args in new[] { Query(initial.Id, -1), Query(initial.Id, 4097), Query(initial.Id, limit: 0), Query(initial.Id, limit: 101), Query(initial.Id, 1), Query(initial.Id, token: "invalid") })
            await Call(agent, "agent", "list_documents", args, "invalid_query");
        await Call(agent, "agent", "list_documents", Query(home.Id), "capture_missing");
        await Call(agent, "agent", "list_documents", Query(initial.Id, id: Guid.Empty.ToString()), "invalid_id");
        var openedA = Doc(await Call(owner, "owner", "open_document", Lifecycle(home.Id, a, hash: original), "document_opened"));
        await using var freshAgent = await Client("fresh-agent-after-owner-open"); agent = freshAgent;
        var beforeB = await Context(agent, "agent"); var one = await List(beforeB.Id, limit: 1);
        Program.Check("independent agent discovers background A exact native identity at unchanged Home", one.Total == 1 && one.Items.Single().DocumentId == openedA.DocumentId
            && beforeB.Snapshot == home.Snapshot && one.Items[0].Path == a && !one.Items[0].IsActive);
        var openedB = Doc(await Call(owner, "owner", "open_document", Lifecycle(home.Id, b, hash: original), "document_opened"));
        await Call(agent, "agent", "list_documents", Query(beforeB.Id, 1, 1, one.InventoryToken), "stale_inventory");
        var stable = await Context(agent, "agent"); var first = await List(stable.Id, limit: 1); var second = await List(stable.Id, first.NextOffset!.Value, 1, first.InventoryToken);
        OpenDocumentInfo[] all = [.. first.Items, .. second.Items];
        Program.Check("stable one-item native pages enumerate exactly owner-opened A/B and fixture flags", first.Total == 2 && second.Total == 2 && first.NextOffset == 1 && second.NextOffset is null
            && second.InventoryToken == first.InventoryToken && all.Select(d => d.DocumentId).Distinct().Count() == 2
            && new[] { openedA, openedB }.All(d => all.Any(i => i.DocumentId == d.DocumentId && i.Path == d.Path && i.Title == d.Title && i.IsActive == d.IsActive && i.IsModified == d.IsModified))
            && all.All(i => i.HasSavedPath && !i.IsActive && !i.IsModified && !i.IsFamilyDocument && !i.IsLinked && !i.IsWorkshared && !i.IsReadOnly)
            && stable.Snapshot == home.Snapshot && (await Context(owner, "owner")).Snapshot == home.Snapshot);
        var discoveredA = all.Single(d => d.Path == a); var discoveredB = all.Single(d => d.Path == b);
        async Task<OperationResult> Cli(string name, params string[] extra)
        {
            string id = Guid.NewGuid().ToString(); string[] args = ["list-documents", "--session", session, "--limit", "1", "--request-id", id, "--json", .. extra];
            Log(new { actor = "cli", phase = "submitted", executable = cli, args }); var watch = Stopwatch.StartNew();
            var start = new ProcessStartInfo(cli) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string arg in args) start.ArgumentList.Add(arg);
            using var process = Process.Start(start)!; var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(45)); string text = await stdout; string errors = await stderr;
            File.WriteAllText(Path.Combine(folder, name), text); var r = JsonSerializer.Deserialize<OperationResult>(text, Protocol.Json)!;
            Log(new { actor = "cli", phase = "returned", args, process.ExitCode, errors, milliseconds = watch.Elapsed.TotalMilliseconds, result = r });
            Program.Check("immutable installed CLI returns exact caller read GUID and Home target", process.ExitCode == 0 && r.Succeeded && r.Id.ToString() == id && r.Snapshot == home.Snapshot); return r;
        }
        var cliFirst = await Cli("cli-page-1.json"); var cliSecond = await Cli("cli-page-2.json", "--capture", Path.Combine(folder, "cli-page-1.json"), "--offset", cliFirst.Documents!.NextOffset!.Value.ToString());
        Program.Check("installed CLI pagination independently matches MCP identities and token", cliFirst.Documents!.InventoryToken == first.InventoryToken && cliSecond.Documents!.InventoryToken == first.InventoryToken
            && cliFirst.Documents.Items.Concat(cliSecond.Documents.Items).SequenceEqual(all));
        await Call(agent, "agent", "activate_document", Lifecycle(stable.Id, a, discoveredA.DocumentId), "document_activated");
        var active = await Context(agent, "agent"); string originalName = active.Snapshot.ViewName;
        await Call(agent, "agent", "list_documents", Query(stable.Id), "stale_target");
        await Call(agent, "agent", "list_documents", Query(active.Id, token: first.InventoryToken), "stale_inventory");
        await Call(agent, "agent", "activate_document", Lifecycle(active.Id, b, openedA.DocumentId), "wrong_document");
        await Call(agent, "agent", "close_document", Lifecycle(active.Id, b, discoveredB.DocumentId, new('0', 64)), "file_changed");
        Program.Check("wrong lifecycle targets preserve exact active A", active.Snapshot == (await Context(agent, "agent")).Snapshot && active.Snapshot.Target.DocumentId == openedA.DocumentId);
        await Call(agent, "agent", "rename_view", new() { ["captureId"] = active.Id, ["requestId"] = Guid.NewGuid().ToString(), ["name"] = "Document discovery " + Guid.NewGuid().ToString("N")[..8] }, "applied");
        var dirty = await Context(agent, "agent"); var dirtyPage = await List(dirty.Id);
        Program.Check("native document inventory observes unsaved A modification without saving", dirty.Snapshot.IsModified && dirtyPage.Items.Single(d => d.DocumentId == openedA.DocumentId).IsModified && Hash(a) == original);
        await Call(agent, "agent", "rename_view", new() { ["captureId"] = dirty.Id, ["requestId"] = Guid.NewGuid().ToString(), ["name"] = originalName }, "applied");
        active = await Context(agent, "agent"); var saved = Doc(await Call(agent, "agent", "save_document", Lifecycle(active.Id, a, openedA.DocumentId, original), "document_saved"));
        string savedA = Hash(a); active = await Context(agent, "agent"); var clean = await List(active.Id);
        Program.Check("explicit save only copied A restores exact name and clean metadata", !active.Snapshot.IsModified && active.Snapshot.ViewName == originalName && saved.Sha256 == savedA
            && clean.Items.Single(d => d.DocumentId == openedA.DocumentId).IsModified == false && Hash(b) == original && Hash(source) == original);
        await Call(agent, "agent", "list_documents", Query(active.Id, token: dirtyPage.InventoryToken), "stale_inventory");
        await Call(agent, "agent", "close_document", Lifecycle(active.Id, b, discoveredB.DocumentId, original), "document_closed");
        active = await Context(agent, "agent"); Program.Check("native close removes only B", (await List(active.Id)).Items is [{ DocumentId: var onlyId }] && onlyId == openedA.DocumentId);
        await Call(agent, "agent", "list_documents", Query(active.Id, token: clean.InventoryToken), "stale_inventory");
        var prior = Process.GetProcessesByName("revitthyme-mcp").Select(p => p.Id).ToHashSet(); await using var lost = await Client("lost");
        using var server = Process.GetProcessesByName("revitthyme-mcp").Single(p => !prior.Contains(p.Id));
        Program.Check("lost-read experiment owns only newly created installed MCP server", string.Equals(server.MainModule?.FileName, executable, StringComparison.OrdinalIgnoreCase));
        var lostContext = await Context(lost, "lost"); var ownerContext = await Context(owner, "owner"); string openId = Guid.NewGuid().ToString(), readId = Guid.NewGuid().ToString();
        var openArgs = Lifecycle(ownerContext.Id, b, hash: original, id: openId); var readArgs = Query(lostContext.Id, id: readId);
        Save("intent-" + readId + ".json", new { name = "list_documents", openId, readId, openArgs, readArgs, lostContext.Snapshot.Target, serverPid = server.Id, serverStart = server.StartTime.ToUniversalTime().Ticks.ToString(), executable });
        Save("modal-open.json", new { id = openId, path = b }); var originalOpen = Call(owner, "owner", "open_document", openArgs, "any");
        using var visible = await Receipt("modal-visible-" + openId + ".json", 30);
        Program.Check("signed native open probe holds exact B test dialog before reply", visible.RootElement.GetProperty("id").GetString() == openId
            && visible.RootElement.GetProperty("path").GetString() == b && !originalOpen.IsCompleted && !File.Exists(Path.Combine(folder, "modal-open.json")));
        var openReply = await originalOpen;
        Program.Check("original open releases its ordinary pipe at the actual transport deadline while modal remains", openReply.GetProperty("id").GetString() == openId
            && openReply.GetProperty("code").GetString() == "transport_lost" && openReply.GetProperty("outcome").GetString() == "Unconfirmed"
            && !File.Exists(Path.Combine(folder, "modal-closed-" + openId + ".json")));
        await Recover(observer, "observer", openId, true);
        var readClock = Stopwatch.StartNew(); var originalRead = Call(lost, "lost", "list_documents", readArgs, "any"); JsonElement pending;
        do { pending = await Recover(observer, "observer", readId); if (pending.GetProperty("code").GetString() != "not_found") break; await Task.Delay(5); } while (readClock.Elapsed.TotalSeconds < 5);
        Program.Check("original queued read is Pending promptly while native open blocks", pending.GetProperty("code").GetString() == "pending" && readClock.Elapsed < TimeSpan.FromSeconds(2) && !originalRead.IsCompleted);
        Log(new { name = "original_read_pending", openId, readId, milliseconds = readClock.Elapsed.TotalMilliseconds, pending });
        server.Kill(); await server.WaitForExitAsync(); bool responseLost = false;
        try { await originalRead.WaitAsync(TimeSpan.FromSeconds(10)); } catch (Exception error) when (error is not TimeoutException) { responseLost = true; Log(new { readId, responseLost, error = error.GetType().Name }); }
        Program.Check("original read SDK transport actually throws after exact owned server loss", responseLost);
        await using var recovery = await Client("recovery"); await Recover(recovery, "recovery", readId, true); await Recover(observer, "observer", readId, true);
        Program.Check("recovery stays Pending before explicit modal dismissal", !File.Exists(Path.Combine(folder, "modal-closed-" + openId + ".json")));
        Save("dismiss-ready-" + openId + ".json", new { id = openId, path = b, readId, title = visible.RootElement.GetProperty("title").GetString(), responseLost });
        Console.WriteLine("AWAITING EXPLICIT TEST DIALOG CLOSE: " + openId + "; queued read has its own 30-second deadline, so dismiss this identified test dialog promptly.");
        using var closed = await Receipt("modal-closed-" + openId + ".json", 120);
        Program.Check("explicit operator intervention closes only the identified test dialog", closed.RootElement.GetProperty("id").GetString() == openId && closed.RootElement.GetProperty("result").GetString() == "Close");
        var completedOpen = await Terminal(observer, "observer", openId); var reopenedB = Doc(completedOpen);
        Program.Check("original owner background open completes under its same GUID", completedOpen.GetProperty("id").GetString() == openId && completedOpen.GetProperty("code").GetString() == "document_opened"
            && completedOpen.GetProperty("outcome").GetString() == "Completed"
            && reopenedB.Path == b && reopenedB.Sha256 == original && !reopenedB.IsActive && !reopenedB.IsModified
            && openReply.GetProperty("id").GetString() == openId && (openReply.GetProperty("code").GetString() == "document_opened"
                || openReply.GetProperty("code").GetString() == "transport_lost" && openReply.GetProperty("outcome").GetString() == "Unconfirmed"));
        var retained = await Terminal(recovery, "recovery", readId);
        Log(new { name = "original_read_recovered", openId, readId, milliseconds = readClock.Elapsed.TotalMilliseconds, retained });
        Program.Check("original lost read recovers Inspected and original GUID with two native documents", retained.GetProperty("id").GetString() == readId
            && retained.GetProperty("outcome").GetString() == "Inspected" && retained.GetProperty("code").GetString() == "inspected" && Page(retained).Total == 2
            && retained.GetProperty("snapshot").Deserialize<Snapshot>(Protocol.Json) == lostContext.Snapshot
            && Page(retained).Items.Single(d => d.DocumentId == reopenedB.DocumentId) is { IsActive: false, IsModified: false, HasSavedPath: true, Path: var reopenedPath } && reopenedPath == b
            && Page(retained).Items.Select(d => d.DocumentId).Order().SequenceEqual(new[] { openedA.DocumentId, reopenedB.DocumentId }.Order()));
        Program.Check("repeated original read recovery is byte-stable", (await Recover(observer, "observer", readId)).GetRawText() == retained.GetRawText());
        active = await Context(agent, "agent"); Program.Check("reopened B receives a new native identity", reopenedB.DocumentId != openedB.DocumentId);
        await Call(agent, "agent", "activate_document", Lifecycle(active.Id, b, openedB.DocumentId), "wrong_document");
        await Call(agent, "agent", "close_document", Lifecycle(active.Id, b, reopenedB.DocumentId, original), "document_closed");
        active = await Context(agent, "agent"); var currentPage = await List(active.Id);
        Program.Check("retained read remains point-in-time two after B closes while fresh read reports one", currentPage.Total == 1 && currentPage.Items.Single().DocumentId == openedA.DocumentId
            && (await Recover(recovery, "recovery", readId)).GetRawText() == retained.GetRawText() && Page(retained).Total == 2);
        await Call(agent, "agent", "export_view_image", new() { ["captureId"] = active.Id, ["requestId"] = Guid.NewGuid().ToString(), ["viewId"] = active.Snapshot.Target.ViewId, ["pixelSize"] = 512 }, "image_exported");
        var final = await Context(agent, "agent"); Payloads();
        Program.Check("native final A/view remain exact clean and all saved files verify", final.Snapshot == active.Snapshot && final.Snapshot.Target.DocumentId == openedA.DocumentId
            && final.Snapshot.ViewName == originalName && !final.Snapshot.IsModified && Hash(a) == savedA && Hash(b) == original && Hash(source) == original);
        Save("complete.json", new { passed = true, binding, savedA, savedB = original, calls, final.Snapshot, resultSource = "immutable installed SDK and CLI; observed local RVT fixture flags only", readId, openId, responseLost, retained, currentPage });

        async Task<JsonDocument> Receipt(string name, int seconds)
        {
            var watch = Stopwatch.StartNew(); string path = Path.Combine(folder, name);
            while (watch.Elapsed.TotalSeconds < seconds)
            {
                try { return JsonDocument.Parse(await File.ReadAllTextAsync(path)); }
                catch (Exception error) when (error is IOException or JsonException) { await Task.Delay(100); }
            }
            throw new TimeoutException("Qualification receipt missing: " + name);
        }
    }
    private static string Hash(string path) { using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); return Convert.ToHexStringLower(SHA256.HashData(stream)); }
}
