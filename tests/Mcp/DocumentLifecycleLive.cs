using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using ModelContextProtocol.Client;
using RevitThyme.Core;

namespace RevitThyme.McpTests;

// Actual installed stdio client; no submitted C#, callbacks or private host wiring.
internal static class DocumentLifecycleLive
{
    public static async Task Run(string folder, bool verifySaved = false)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "lifecycle-input.json")));
        var input = json.RootElement;
        string Text(string name) => input.GetProperty(name).GetString()!;
        string bundle = Text("bundle"), session = Text("session"), a = Text("initial"), b = Text("other"), c = Text("recovery");
        string executable = Path.Combine(bundle, "mcp", "revitthyme-mcp.exe"), source = Text("source"), originalHash = Text("sourceSha256");
        var binding = SessionBinding.Load(session);
        Program.Check("lifecycle session matches exact owned PID/start", binding.ProcessId == input.GetProperty("pid").GetInt32() && binding.ProcessStart == Text("start"));
        Program.Check("lifecycle installed bundle identity and payloads", Hash(Path.Combine(bundle, "manifest.json")) == Text("manifestSha256") && Payloads(bundle));
        Program.Check("lifecycle original source remains unchanged", Hash(source) == originalHash);
        var calls = new List<object>();
        async Task<McpClient> Client() => await McpClient.CreateAsync(new StdioClientTransport(new() { Command = executable, Arguments = ["--session", session], Name = "Native lifecycle qualification" }));
        var client = await Client();
        try
        {
            async Task<JsonElement> Call(string name, Dictionary<string, object?>? args = null, string? expected = null)
            {
                var reply = await Program.Call(client, name, args); calls.Add(new { name, args, result = reply.Data, reply.Result.IsError });
                if (expected is not null) Program.Check("native MCP " + name + ": " + expected, reply.Data.GetProperty("code").GetString() == expected);
                else if (reply.Result.IsError == true) throw new InvalidDataException(name + ": " + reply.Data.GetRawText());
                return reply.Data;
            }
            async Task<(string Id, Snapshot Snapshot)> Context()
            {
                var context = await Call("get_context");
                return (context.GetProperty("captureId").GetString()!, context.GetProperty("result").GetProperty("snapshot").Deserialize<Snapshot>(Protocol.Json)!);
            }
            Dictionary<string, object?> Args(string capture, string path, string? document = null, string? hash = null, string? id = null)
            {
                var args = new Dictionary<string, object?> { ["captureId"] = capture, ["requestId"] = id ?? Guid.NewGuid().ToString(), ["path"] = path };
                if (document is not null) args["documentId"] = document;
                if (hash is not null) args["expectedSha256"] = hash;
                return args;
            }
            static DocumentLifecycleResult Document(JsonElement data) => data.GetProperty("documentLifecycle").Deserialize<DocumentLifecycleResult>(Protocol.Json)!;
            var context = await Context();
            if (verifySaved)
            {
                using var expectedJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "expected-saved.json")));
                var expected = expectedJson.RootElement;
                Program.Check("saved lifecycle change persists in fresh native process", context.Snapshot.DocumentPath == c && !context.Snapshot.IsModified
                    && context.Snapshot.ViewName == expected.GetProperty("viewName").GetString() && Hash(c) == expected.GetProperty("sha256").GetString());
                File.WriteAllText(Path.Combine(folder, "lifecycle-reopened.json"), JsonSerializer.Serialize(new { binding, context.Snapshot, passed = true, calls }, Protocol.Json)); return;
            }
            Program.Check("lifecycle starts on clean disposable A", context.Snapshot.DocumentPath == a && !context.Snapshot.IsModified && !context.Snapshot.IsWorkshared && Hash(a) == originalHash && Hash(b) == originalHash && Hash(c) == originalHash);
            Program.Check("lifecycle actual installed MCP has 29 tools", (await client.ListToolsAsync()).Count == 29);
            await Call("run_query", new() { ["captureId"] = context.Id, ["requestId"] = Guid.NewGuid().ToString(), ["source"] = "return 1;", ["trusted"] = true }, "trusted_code_disabled");
            string aid = context.Snapshot.Target.DocumentId;
            await Call("open_document", Args(context.Id, b, hash: new string('0', 64)), "file_changed");
            await Call("open_document", Args(context.Id, Path.Combine(folder, "missing.rvt"), hash: originalHash), "file_missing");
            await Call("open_document", Args(context.Id, "relative.rvt", hash: originalHash), "invalid_lifecycle");
            string openId = Guid.NewGuid().ToString();
            var opened = await Call("open_document", Args(context.Id, b, hash: originalHash, id: openId), "document_opened"); string bid = Document(opened).DocumentId;
            Program.Check("explicit background open preserves active native A and files", !Document(opened).IsActive && Document(opened).Path == b && (await Context()).Snapshot == context.Snapshot && Hash(a) == originalHash && Hash(b) == originalHash);
            var retained = await Call("get_operation", new() { ["requestId"] = openId }, "document_opened");
            Program.Check("open recovery returns original native identity", retained.GetRawText() == opened.GetRawText());
            await Call("open_document", Args(context.Id, a, hash: originalHash, id: openId), "id_conflict");
            await Call("activate_document", Args(context.Id, b, aid), "wrong_document");
            await Call("activate_document", Args(context.Id, c, Guid.NewGuid().ToString()), "wrong_document");
            string oldCapture = context.Id;
            await Call("activate_document", Args(context.Id, b, bid), "document_activated"); context = await Context();
            Program.Check("native activation selects exact B with no save", context.Snapshot.DocumentPath == b && context.Snapshot.Target.DocumentId == bid && !context.Snapshot.IsModified && Hash(b) == originalHash);
            await Call("save_document", Args(oldCapture, a, aid, originalHash), "stale_target");
            await Call("save_document", Args(context.Id, a, aid, originalHash), "wrong_document");
            await Call("save_document", Args(context.Id, b, bid, new string('0', 64)), "file_changed");
            string name = "Lifecycle save " + Guid.NewGuid().ToString("N")[..8];
            await Call("rename_view", new() { ["captureId"] = context.Id, ["requestId"] = Guid.NewGuid().ToString(), ["name"] = name }, "applied"); context = await Context();
            Program.Check("native typed edit is unsaved before explicit save", context.Snapshot.IsModified && context.Snapshot.ViewName == name && Hash(b) == originalHash);
            string saveId = Guid.NewGuid().ToString();
            var saved = await Call("save_document", Args(context.Id, b, bid, originalHash, saveId), "document_saved"); context = await Context();
            Program.Check("explicit save verifies native clean state and changed disk bytes", !context.Snapshot.IsModified && Document(saved).Sha256 == Hash(b) && Hash(b) != originalHash && Hash(a) == originalHash);
            DateTime stamp = File.GetLastWriteTimeUtc(b);
            await Call("get_operation", new() { ["requestId"] = saveId }, "document_saved");
            Program.Check("save recovery does not save again", stamp == File.GetLastWriteTimeUtc(b));
            await Call("save_document", Args(context.Id, b, bid, originalHash), "file_changed");
            await Call("activate_document", Args(context.Id, a, aid), "document_activated"); context = await Context();
            Program.Check("return to A preserves its original native and saved state", context.Snapshot.DocumentPath == a && !context.Snapshot.IsModified && Hash(a) == originalHash);

            async Task<JsonElement> Lost(string tool, Func<string, string, Dictionary<string, object?>> build, Func<bool> accepted)
            {
                await client.DisposeAsync(); var before = Process.GetProcessesByName("revitthyme-mcp").Select(p => p.Id).ToHashSet(); client = await Client();
                using var server = Process.GetProcessesByName("revitthyme-mcp").Single(p => !before.Contains(p.Id));
                Program.Check("interruption probe owns exact installed MCP server", server.MainModule?.FileName == executable);
                var current = await Context(); string id = Guid.NewGuid().ToString(); var args = build(current.Id, id);
                var pending = Program.Call(client, tool, args); var watch = Stopwatch.StartNew();
                while (!pending.IsCompleted && !accepted() && watch.Elapsed.TotalSeconds < 20) await Task.Delay(2);
                Program.Check("native " + tool + " started while response outstanding", accepted() && !pending.IsCompleted);
                server.Kill(); await server.WaitForExitAsync(); bool lost = false;
                try { await pending; } catch (Exception e) { lost = true; calls.Add(new { tool, id, lost, exception = e.GetType().Name }); }
                Program.Check("native " + tool + " original response actually lost", lost);
                await client.DisposeAsync(); client = await Client();
                JsonElement result;
                do
                {
                    var reply = await Program.Call(client, "get_operation", new() { ["requestId"] = id }); result = reply.Data;
                    calls.Add(new { name = "get_operation", requestId = id, result, reply.Result.IsError });
                    if (result.GetProperty("code").GetString() != "pending") break;
                    await Task.Delay(50);
                } while (watch.Elapsed.TotalSeconds < 30);
                Program.Check("native " + tool + " recovered original completed outcome", result.GetProperty("id").GetString() == id && result.GetProperty("outcome").GetString() == "Completed");
                return result;
            }
            Program.Check("recovery fixture is initially closed", !Locked(c));
            var recoveredOpen = await Lost("open_document", (capture, id) => Args(capture, c, hash: originalHash, id: id), () => Locked(c));
            string cid = Document(recoveredOpen).DocumentId; context = await Context();
            Program.Check("interrupted open keeps A active and original files unchanged", context.Snapshot.DocumentPath == a && Hash(a) == originalHash && Hash(c) == originalHash);
            await Call("activate_document", Args(context.Id, c, cid), "document_activated"); context = await Context();
            string finalName = "Recovered save " + Guid.NewGuid().ToString("N")[..8];
            await Call("rename_view", new() { ["captureId"] = context.Id, ["requestId"] = Guid.NewGuid().ToString(), ["name"] = finalName }, "applied");
            stamp = File.GetLastWriteTimeUtc(c);
            var recoveredSave = await Lost("save_document", (capture, id) => Args(capture, c, cid, originalHash, id), () => File.GetLastWriteTimeUtc(c) != stamp);
            context = await Context(); string finalHash = Hash(c);
            Program.Check("interrupted save native state and disk hash independently agree", context.Snapshot.DocumentPath == c && context.Snapshot.ViewName == finalName && !context.Snapshot.IsModified && Document(recoveredSave).Sha256 == finalHash && finalHash != originalHash);
            stamp = File.GetLastWriteTimeUtc(c);
            var again = await Call("get_operation", new() { ["requestId"] = recoveredSave.GetProperty("id").GetString() }, "document_saved");
            Program.Check("interrupted save repeated recovery preserves receipt and timestamp", again.GetRawText() == recoveredSave.GetRawText() && File.GetLastWriteTimeUtc(c) == stamp);
            Program.Check("lifecycle originals installation and untouched A preserved", Hash(source) == originalHash && Hash(a) == originalHash && Payloads(bundle));
            File.WriteAllText(Path.Combine(folder, "expected-saved.json"), JsonSerializer.Serialize(new { viewName = finalName, sha256 = finalHash }, Protocol.Json));
            File.WriteAllText(Path.Combine(folder, "lifecycle-complete.json"), JsonSerializer.Serialize(new { passed = true, binding, source, originalHash, finalHash, calls }, Protocol.Json));
        }
        finally { await client.DisposeAsync(); File.WriteAllText(Path.Combine(folder, verifySaved ? "reopened-transcript.json" : "lifecycle-transcript.json"), JsonSerializer.Serialize(calls, Protocol.Json)); }
    }
    private static bool Locked(string path) { try { using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None); return false; } catch (IOException) { return true; } }
    private static string Hash(string path) { using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); return Convert.ToHexStringLower(SHA256.HashData(file)); }
    private static bool Payloads(string bundle)
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(bundle, "manifest.json")));
        return manifest.RootElement.GetProperty("files").EnumerateObject().All(e => Hash(Path.Combine(bundle, e.Name)).Equals(e.Value.GetString(), StringComparison.OrdinalIgnoreCase));
    }
}
