using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using RevitThyme.Core;

namespace RevitThyme.McpTests;

internal static class UiLifecycleLive
{
    public static async Task Run(string folder)
    {
        using var inputJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "input.json"))); var input = inputJson.RootElement;
        string Text(string key) => input.GetProperty(key).GetString()!;
        string bundle = Text("bundle"), executable = Path.Combine(bundle, "mcp", "revitthyme-mcp.exe"), session = Text("session"), a = Text("a"), b = Text("b"), source = Text("source"), original = Text("sourceSha256");
        var binding = SessionBinding.Load(session);
        Program.Check("UI lifecycle targets exact owned PID/start and signed bundle", binding.ProcessId == input.GetProperty("pid").GetInt32() && binding.ProcessStart == Text("start") && Hash(Path.Combine(bundle, "manifest.json")) == Text("manifestSha256"));
        var calls = new List<object>();
        async Task<McpClient> Client() => await McpClient.CreateAsync(new StdioClientTransport(new() { Command = executable, Arguments = ["--session", session], Name = "Home close view native qualification" }));
        var client = await Client(); await using var observer = await Client();
        try
        {
            async Task<JsonElement> Call(string name, Dictionary<string, object?>? args = null, string? code = null)
            {
                var reply = await Program.Call(client, name, args); calls.Add(new { name, args, reply.Data, reply.Result.IsError });
                if (code is not null) Program.Check("native " + name + ": " + code, reply.Data.GetProperty("code").GetString() == code
                    && reply.Result.IsError == (code is not ("document_opened" or "document_activated" or "document_saved" or "document_closed" or "view_activated" or "applied")));
                else if (reply.Result.IsError == true) throw new InvalidDataException(name + ": " + reply.Data.GetRawText());
                return reply.Data;
            }
            async Task<(string Id, Snapshot Snapshot)> Context()
            { var r = await Call("get_context"); return (r.GetProperty("captureId").GetString()!, r.GetProperty("result").GetProperty("snapshot").Deserialize<Snapshot>(Protocol.Json)!); }
            Dictionary<string, object?> Args(string capture, string path, string? doc = null, string? hash = null, string? id = null)
            {
                var args = new Dictionary<string, object?> { ["captureId"] = capture, ["requestId"] = id ?? Guid.NewGuid().ToString(), ["path"] = path };
                if (doc is not null) args["documentId"] = doc; if (hash is not null) args["expectedSha256"] = hash; return args;
            }
            static DocumentLifecycleResult Doc(JsonElement r) => r.GetProperty("documentLifecycle").Deserialize<DocumentLifecycleResult>(Protocol.Json)!;
            async Task<JsonElement> Lost(string name, Func<string, string, Dictionary<string, object?>> build, string expected)
            {
                await client.DisposeAsync(); var prior = Process.GetProcessesByName("revitthyme-mcp").Select(p => p.Id).ToHashSet(); client = await Client();
                using var server = Process.GetProcessesByName("revitthyme-mcp").Single(p => !prior.Contains(p.Id));
                Program.Check("interruption owns exact installed MCP process", server.MainModule?.FileName == executable);
                var context = await Context(); string id = Guid.NewGuid().ToString(); var args = build(context.Id, id);
                File.WriteAllText(Path.Combine(folder, "intent-" + id + ".json"), JsonSerializer.Serialize(new { name, id, args, context.Snapshot.Target }, Protocol.Json));
                var pending = Program.Call(client, name, args); var watch = Stopwatch.StartNew(); JsonElement retained;
                do
                {
                    var reply = await Program.Call(observer, "get_operation", new() { ["requestId"] = id }); retained = reply.Data;
                    if (retained.GetProperty("code").GetString() != "not_found") break;
                    await Task.Delay(2);
                } while (!pending.IsCompleted && watch.Elapsed.TotalSeconds < 10);
                Program.Check("outcome control observes accepted " + name + " before original response", retained.GetProperty("code").GetString() is not "not_found" && !pending.IsCompleted);
                server.Kill(); await server.WaitForExitAsync(); bool lost = false;
                try { await pending; } catch (Exception e) { lost = true; calls.Add(new { name, id, lost, exception = e.GetType().Name }); }
                Program.Check("original " + name + " response actually lost", lost);
                do
                {
                    var reply = await Program.Call(observer, "get_operation", new() { ["requestId"] = id }); retained = reply.Data;
                    if (retained.GetProperty("code").GetString() != "pending") break;
                    await Task.Delay(50);
                } while (watch.Elapsed.TotalSeconds < 70);
                calls.Add(new { name = "recovered_" + name, id, retained });
                Program.Check("original " + name + " retained completion recovered", retained.GetProperty("id").GetString() == id && retained.GetProperty("code").GetString() == expected && retained.GetProperty("outcome").GetString() == "Completed");
                var again = await Program.Call(observer, "get_operation", new() { ["requestId"] = id });
                Program.Check("recovered " + name + " is stable without replay", retained.GetRawText() == again.Data.GetRawText());
                await client.DisposeAsync(); client = await Client(); return retained;
            }
            var context = await Context();
            Program.Check("native Home context has valid capture and no invented document/view", context.Id.Length > 0 && !context.Snapshot.HasDocument && context.Snapshot.Target.ViewId == "" && context.Snapshot.DocumentPath == "" && !context.Snapshot.CanModify);
            Program.Check("native Home fixture hashes and source intact", Hash(a) == original && Hash(b) == original && Hash(source) == original);
            Program.Check("native server advertises 31 typed tools", (await client.ListToolsAsync()).Count == 31);
            await Call("list_views", new() { ["captureId"] = context.Id }, "no_document");
            var openedA = await Lost("open_document", (cap, id) => Args(cap, a, hash: original, id: id), "document_opened"); string aid = Doc(openedA).DocumentId;
            context = await Context(); Program.Check("Home background open does not implicitly activate", !context.Snapshot.HasDocument && !Doc(openedA).IsActive && Hash(a) == original);
            await Call("save_document", Args(context.Id, a, aid, original), "lifecycle_blocked");
            await Lost("close_document", (cap, id) => Args(cap, a, aid, original, id), "document_closed"); context = await Context();
            Program.Check("last background close stays at Home in same process", !context.Snapshot.HasDocument && context.Snapshot.Target.ProcessId == binding.ProcessId && Hash(a) == original);
            var firstAid = aid;
            openedA = await Call("open_document", Args(context.Id, a, hash: original), "document_opened"); aid = Doc(openedA).DocumentId;
            Program.Check("Home reopen assigns new native identity", aid != firstAid);
            await Call("activate_document", Args(context.Id, a, firstAid), "wrong_document");
            await Call("activate_document", Args(context.Id, a, aid), "document_activated"); context = await Context();
            var views = await Call("list_views", new() { ["captureId"] = context.Id, ["limit"] = 100 });
            var page = views.GetProperty("elements").Deserialize<ElementPage>(Protocol.Json)!;
            string View(string name) => page.Items.Single(v => v.Name == name).UniqueId;
            string threeD = View("Image QA 3D"), template = View("Image QA Template"), sheet = View("Image QA Sheet");
            await Call("activate_view", new() { ["captureId"] = context.Id, ["requestId"] = Guid.NewGuid().ToString(), ["viewId"] = template }, "unsupported_view");
            await Call("activate_view", new() { ["captureId"] = context.Id, ["requestId"] = Guid.NewGuid().ToString(), ["viewId"] = Guid.NewGuid() + "-00000001" }, "unsupported_view");
            string staleView = context.Id;
            await Lost("activate_view", (cap, id) => new() { ["captureId"] = cap, ["requestId"] = id, ["viewId"] = threeD }, "view_activated");
            context = await Context(); Program.Check("native view switches exact document/view without save", context.Snapshot.Target.DocumentId == aid && context.Snapshot.Target.ViewId == threeD && !context.Snapshot.IsModified && Hash(a) == original);
            // Capture tokens are connection-local; an old token from the interrupted server is refused.
            await Call("activate_view", new() { ["captureId"] = staleView, ["requestId"] = Guid.NewGuid().ToString(), ["viewId"] = sheet }, "capture_missing");
            await Call("activate_view", new() { ["captureId"] = context.Id, ["requestId"] = Guid.NewGuid().ToString(), ["viewId"] = sheet }, "view_activated");
            context = await Context(); Program.Check("sheet activation verifies native exact sheet", context.Snapshot.Target.ViewId == sheet && context.Snapshot.ViewType == "DrawingSheet");
            await Call("rename_view", new() { ["captureId"] = context.Id, ["requestId"] = Guid.NewGuid().ToString(), ["name"] = "Explicit close guard" }, "applied"); context = await Context();
            await Call("close_document", Args(context.Id, a, aid, original), "document_dirty");
            Program.Check("dirty close preserves unsaved native state and disk", (await Context()).Snapshot.IsModified && Hash(a) == original);
            await Call("save_document", Args(context.Id, a, aid, original), "document_saved"); context = await Context(); string savedA = Hash(a);
            var openedB = await Call("open_document", Args(context.Id, b, hash: original), "document_opened"); string bid = Doc(openedB).DocumentId;
            await Call("close_document", Args(context.Id, b, aid, original), "wrong_document");
            await Call("close_document", Args(context.Id, b, bid, new string('0', 64)), "file_changed");
            await Call("close_document", Args(context.Id, b, bid, original), "document_closed"); context = await Context();
            Program.Check("background close preserves exact active A and untouched B", context.Snapshot.Target.DocumentId == aid && Hash(a) == savedA && Hash(b) == original);
            openedB = await Call("open_document", Args(context.Id, b, hash: original), "document_opened"); string bid2 = Doc(openedB).DocumentId;
            Program.Check("closed/reopened background document has new native ID", bid2 != bid);
            await Call("activate_document", Args(context.Id, b, bid), "wrong_document");
            string beforeB = context.Id; await Call("activate_document", Args(context.Id, b, bid2), "document_activated"); context = await Context();
            await Call("activate_view", new() { ["captureId"] = beforeB, ["requestId"] = Guid.NewGuid().ToString(), ["viewId"] = threeD }, "stale_target");
            await Call("close_document", Args(context.Id, b, bid2, original), "close_blocked");
            Program.Check("active close is rejected with exact native target unchanged", (await Context()).Snapshot.Target.DocumentId == bid2 && Hash(b) == original);
            await Lost("close_document", (cap, id) => Args(cap, a, aid, savedA, id), "document_closed"); context = await Context();
            Program.Check("background A close preserves active B and source", context.Snapshot.Target.DocumentId == bid2 && !context.Snapshot.IsModified && Hash(a) == savedA && Hash(b) == original && Hash(source) == original);
            await Call("close_document", Args(context.Id, a, aid, savedA), "wrong_document");
            File.WriteAllText(Path.Combine(folder, "complete.json"), JsonSerializer.Serialize(new { passed = true, binding, context.Snapshot, original, savedA, calls }, Protocol.Json));
        }
        finally { await client.DisposeAsync(); File.WriteAllText(Path.Combine(folder, "transcript.json"), JsonSerializer.Serialize(calls, Protocol.Json)); }
    }
    public static async Task Images(string folder)
    {
        using var inputJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "input.json"))); var input = inputJson.RootElement;
        string Text(string key) => input.GetProperty(key).GetString()!;
        var binding = SessionBinding.Load(Text("session"));
        Program.Check("visual client verifies exact owned process/start and immutable bundle", binding.ProcessId == input.GetProperty("pid").GetInt32()
            && binding.ProcessStart == Text("start") && Hash(Path.Combine(Text("bundle"), "manifest.json")) == Text("manifestSha256"));
        await using var client = await McpClient.CreateAsync(new StdioClientTransport(new() { Command = Path.Combine(Text("bundle"), "mcp", "revitthyme-mcp.exe"), Arguments = ["--session", Text("session")], Name = "Native view switch image readback" }));
        var calls = new List<object>();
        async Task<(string Id, Snapshot Snapshot)> Context()
        {
            var r = await Program.Call(client, "get_context");
            if (r.Result.IsError == true) throw new InvalidDataException(r.Data.GetRawText());
            return (r.Data.GetProperty("captureId").GetString()!, r.Data.GetProperty("result").GetProperty("snapshot").Deserialize<Snapshot>(Protocol.Json)!);
        }
        var context = await Context();
        Program.Check("visual client starts only on clean disposable B", context.Snapshot.DocumentPath == Text("b") && !context.Snapshot.IsModified);
        var views = await Program.Call(client, "list_views", new() { ["captureId"] = context.Id, ["limit"] = 100 });
        var page = views.Data.GetProperty("elements").Deserialize<ElementPage>(Protocol.Json)!;
        foreach (string name in new[] { "Image QA 3D", "Image QA Sheet" })
        {
            string view = page.Items.Single(v => v.Name == name).UniqueId, oldCapture = context.Id;
            var switched = await Program.Call(client, "activate_view", new() { ["captureId"] = context.Id, ["requestId"] = Guid.NewGuid().ToString(), ["viewId"] = view });
            Program.Check("visual native exact activation: " + name, switched.Data.GetProperty("code").GetString() == "view_activated");
            var stale = await Program.Call(client, "export_view_image", new() { ["captureId"] = oldCapture, ["requestId"] = Guid.NewGuid().ToString(), ["viewId"] = view });
            Program.Check("visual export rejects capture before view switch", stale.Data.GetProperty("code").GetString() == "stale_target");
            context = await Context(); var baseline = context.Snapshot; string id = Guid.NewGuid().ToString();
            var exported = await Program.Call(client, "export_view_image", new() { ["captureId"] = context.Id, ["requestId"] = id, ["viewId"] = view, ["pixelSize"] = 1000 });
            Program.Check("visual exact native view exported: " + name, exported.Result.IsError != true && exported.Data.GetProperty("code").GetString() == "image_exported");
            var image = exported.Data.GetProperty("imageExport").Deserialize<ViewImageResult>(Protocol.Json)!;
            byte[] bytes = File.ReadAllBytes(image.Path);
            Program.Check("visual native PNG inline bytes and hash verify", image.ViewId == view && Hash(image.Path) == image.Sha256
                && exported.Result.Content.OfType<ImageContentBlock>().Single().DecodedData.Span.SequenceEqual(bytes)
                && System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4)) == image.Width
                && System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4)) == image.Height);
            var recovered = await Program.Call(client, "get_operation", new() { ["requestId"] = id });
            Program.Check("visual PNG recovery works through independent outcome pipe", recovered.Data.GetRawText() == exported.Data.GetRawText()
                && recovered.Result.Content.OfType<ImageContentBlock>().Single().DecodedData.Span.SequenceEqual(bytes));
            Program.Check("visual export preserves native view and modified state", (await Context()).Snapshot == baseline && !baseline.IsModified);
            File.WriteAllBytes(Path.Combine(folder, name.Replace(' ', '-') + ".png"), bytes);
            calls.Add(new { name, view, switched = switched.Data, exported = exported.Data, recovered = recovered.Data, baseline });
        }
        using var complete = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "complete.json")));
        Program.Check("visual checks preserve both saved copies and original source", Hash(Text("b")) == Text("sourceSha256") && Hash(Text("source")) == Text("sourceSha256") && Hash(Text("a")) == complete.RootElement.GetProperty("savedA").GetString());
        File.WriteAllText(Path.Combine(folder, "images-complete.json"), JsonSerializer.Serialize(new { passed = true, binding, calls, final = context.Snapshot }, Protocol.Json));
    }
    private static string Hash(string path) { using var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); return Convert.ToHexStringLower(SHA256.HashData(f)); }
}
