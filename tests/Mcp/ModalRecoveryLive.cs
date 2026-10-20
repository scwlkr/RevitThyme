using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using ModelContextProtocol.Client;
using RevitThyme.Core;

namespace RevitThyme.McpTests;

internal static class ModalRecoveryLive
{
    public static async Task Run(string folder)
    {
        using var inputJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "input.json"))); var input = inputJson.RootElement;
        string Text(string key) => input.GetProperty(key).GetString()!;
        string bundle = Text("bundle"), executable = Path.Combine(bundle, "mcp", "revitthyme-mcp.exe"), session = Text("session"), a = Text("a"), b = Text("b"), hash = Text("sourceSha256");
        var binding = SessionBinding.Load(session);
        using var probe = await Receipt("probe-ready.json", 10);
        Program.Check("modal client pins exact owned Revit, observer and immutable installed bundle", binding.ProcessId == input.GetProperty("pid").GetInt32()
            && binding.ProcessStart == Text("start") && Hash(Path.Combine(bundle, "manifest.json")) == Text("manifestSha256")
            && probe.RootElement.GetProperty("pid").GetInt32() == binding.ProcessId && probe.RootElement.GetProperty("start").GetString() == binding.ProcessStart);
        var calls = new List<object>();
        async Task<McpClient> Client() => await McpClient.CreateAsync(new StdioClientTransport(new() { Command = executable, Arguments = ["--session", session], Name = "Native modal recovery qualification" }));
        var client = await Client(); await using var observer = await Client();
        try
        {
            async Task<JsonElement> Call(string name, Dictionary<string, object?>? args = null, string? code = null)
            {
                var reply = await Program.Call(client, name, args); calls.Add(new { name, args, reply.Data, reply.Result.IsError });
                if (code is not null) Program.Check("native " + name + ": " + code, reply.Data.GetProperty("code").GetString() == code
                    && reply.Result.IsError == (code is not ("document_opened" or "document_activated" or "document_closed")));
                else if (reply.Result.IsError == true) throw new InvalidDataException(name + ": " + reply.Data.GetRawText());
                return reply.Data;
            }
            async Task<(string Id, Snapshot Snapshot)> Context()
            { var reply = await Call("get_context"); return (reply.GetProperty("captureId").GetString()!, reply.GetProperty("result").GetProperty("snapshot").Deserialize<Snapshot>(Protocol.Json)!); }
            Dictionary<string, object?> Args(string capture, string path, string? doc = null, string? expected = null, string? id = null)
            {
                var args = new Dictionary<string, object?> { ["captureId"] = capture, ["requestId"] = id ?? Guid.NewGuid().ToString(), ["path"] = path };
                if (doc is not null) args["documentId"] = doc; if (expected is not null) args["expectedSha256"] = expected; return args;
            }
            static DocumentLifecycleResult Doc(JsonElement result) => result.GetProperty("documentLifecycle").Deserialize<DocumentLifecycleResult>(Protocol.Json)!;
            async Task<JsonElement> Outcome(McpClient recovery, string id, bool pending)
            {
                var clock = Stopwatch.StartNew(); var reply = await Program.Call(recovery, "get_operation", new() { ["requestId"] = id }); clock.Stop();
                calls.Add(new { name = "get_operation", requestedId = id, milliseconds = clock.Elapsed.TotalMilliseconds, reply.Data });
                // Pending uses the polling query's ID; the RPC's requestId argument identifies the retained operation.
                // Terminal recovery below must return the original operation ID.
                if (pending) Program.Check("original retained Pending stays responsive during native modal", reply.Result.IsError == true
                    && reply.Data.GetProperty("code").GetString() == "pending" && clock.Elapsed < TimeSpan.FromSeconds(2));
                return reply.Data;
            }
            async Task<JsonElement> ModalOpen(string path, bool loseServer)
            {
                await client.DisposeAsync(); var prior = Process.GetProcessesByName("revitthyme-mcp").Select(p => p.Id).ToHashSet(); client = await Client();
                using var server = Process.GetProcessesByName("revitthyme-mcp").Single(p => !prior.Contains(p.Id));
                Program.Check("modal submission owns exact installed MCP executable", server.MainModule?.FileName == executable);
                var context = await Context(); string id = Guid.NewGuid().ToString(); var args = Args(context.Id, path, expected: hash, id: id);
                File.WriteAllText(Path.Combine(folder, "intent-" + id + ".json"), JsonSerializer.Serialize(new { id, name = "open_document", args, loseServer, context.Snapshot.Target }, Protocol.Json));
                File.WriteAllText(Path.Combine(folder, "modal-open.json"), JsonSerializer.Serialize(new { id, path }));
                var clock = Stopwatch.StartNew(); var original = Program.Call(client, "open_document", args);
                using var visible = await Receipt("modal-visible-" + id + ".json", 30);
                Program.Check("native probe entered exact one-shot open dialog before reply", visible.RootElement.GetProperty("id").GetString() == id
                    && visible.RootElement.GetProperty("path").GetString() == path && !original.IsCompleted && !File.Exists(Path.Combine(folder, "modal-open.json")));
                await Outcome(observer, id, true);
                if (loseServer)
                {
                    server.Kill(); await server.WaitForExitAsync(); bool lost = false;
                    try { await original; } catch (Exception error) { lost = true; calls.Add(new { id, lost, error = error.GetType().Name }); }
                    Program.Check("native-modal original MCP response actually lost", lost);
                }
                else
                {
                    var timedOut = await original.WaitAsync(TimeSpan.FromSeconds(45));
                    calls.Add(new { name = "original_transport_timeout", id, milliseconds = clock.Elapsed.TotalMilliseconds, timedOut.Data });
                    Program.Check("real transport deadline reports original request Unconfirmed", timedOut.Result.IsError == true && timedOut.Data.GetProperty("id").GetString() == id
                        && timedOut.Data.GetProperty("code").GetString() == "transport_lost" && timedOut.Data.GetProperty("outcome").GetString() == "Unconfirmed"
                        && clock.Elapsed >= TimeSpan.FromSeconds(25) && clock.Elapsed < TimeSpan.FromSeconds(45));
                }
                await client.DisposeAsync(); client = await Client();
                await Outcome(client, id, true); await Outcome(observer, id, true);
                Program.Check("both clients recover Pending while test dialog remains open", !File.Exists(Path.Combine(folder, "modal-closed-" + id + ".json")));
                File.WriteAllText(Path.Combine(folder, "dismiss-ready-" + id + ".json"), JsonSerializer.Serialize(new { id, path, loseServer, title = visible.RootElement.GetProperty("title").GetString() }));
                Console.WriteLine("AWAITING EXPLICIT TEST DIALOG CLOSE: " + id);
                using var closed = await Receipt("modal-closed-" + id + ".json", 240);
                Program.Check("explicit test dialog intervention observed", closed.RootElement.GetProperty("id").GetString() == id
                    && closed.RootElement.GetProperty("result").GetString() == "Close");
                JsonElement retained; var recoveryClock = Stopwatch.StartNew();
                do { retained = await Outcome(observer, id, false); if (retained.GetProperty("code").GetString() != "pending") break; await Task.Delay(50); }
                while (recoveryClock.Elapsed < TimeSpan.FromSeconds(20));
                Program.Check("original modal-blocked open recovers verified Completed", retained.GetProperty("id").GetString() == id && retained.GetProperty("code").GetString() == "document_opened"
                    && retained.GetProperty("outcome").GetString() == "Completed" && Doc(retained).Path == path && !Doc(retained).IsActive && !Doc(retained).IsModified && Doc(retained).Sha256 == hash);
                Program.Check("repeated modal recovery returns same outcome without replay", (await Outcome(client, id, false)).GetRawText() == retained.GetRawText());
                var after = await Context();
                Program.Check("background modal recovery preserves exact Home context and saved copies", !after.Snapshot.HasDocument && after.Snapshot.Target == context.Snapshot.Target
                    && Hash(a) == hash && Hash(b) == hash && Hash(Text("source")) == hash);
                string recoveredId = Doc(retained).DocumentId;
                Program.Check("native already-open inspection preserves recovered document identity", Doc(await Call("open_document", Args(after.Id, path, expected: hash), "document_opened")).DocumentId == recoveredId);
                File.WriteAllText(Path.Combine(folder, "recovered-" + id + ".json"), retained.GetRawText());
                return retained;
            }
            var home = await Context(); Program.Check("modal qualification starts at Home on pristine disposable files", !home.Snapshot.HasDocument && Hash(a) == hash && Hash(b) == hash);
            var openedA = await ModalOpen(a, false); var openedB = await ModalOpen(b, true);
            var current = await Context(); string homeCapture = current.Id;
            await Call("activate_document", Args(current.Id, a, Doc(openedA).DocumentId), "document_activated"); current = await Context();
            await Call("close_document", Args(homeCapture, b, Doc(openedB).DocumentId, hash), "stale_target");
            await Call("save_document", Args(current.Id, b, Doc(openedB).DocumentId, hash), "wrong_document");
            await Call("activate_document", Args(current.Id, b, Doc(openedA).DocumentId), "wrong_document");
            await Call("open_document", Args(current.Id, b, expected: new('0', 64)), "file_changed");
            Program.Check("post-recovery wrong targets preserve exact active native state", (await Context()).Snapshot == current.Snapshot);
            await Call("close_document", Args(current.Id, b, Doc(openedB).DocumentId, hash), "document_closed"); current = await Context();
            Program.Check("post-modal native close preserves active A and every saved file", current.Snapshot.Target.DocumentId == Doc(openedA).DocumentId && !current.Snapshot.IsModified
                && Hash(a) == hash && Hash(b) == hash && Hash(Text("source")) == hash);
            File.WriteAllText(Path.Combine(folder, "complete.json"), JsonSerializer.Serialize(new { passed = true, binding, current.Snapshot, savedA = hash, savedB = hash, calls }, Protocol.Json));
        }
        finally { await client.DisposeAsync(); File.WriteAllText(Path.Combine(folder, "transcript.json"), JsonSerializer.Serialize(calls, Protocol.Json)); }

        async Task<JsonDocument> Receipt(string name, int seconds)
        {
            var watch = Stopwatch.StartNew(); string file = Path.Combine(folder, name);
            while (watch.Elapsed < TimeSpan.FromSeconds(seconds))
            {
                try { return JsonDocument.Parse(await File.ReadAllTextAsync(file)); }
                catch (Exception error) when (error is IOException or JsonException) { await Task.Delay(100); }
            }
            throw new TimeoutException("Qualification receipt missing: " + name);
        }
    }
    private static string Hash(string path) { using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); return Convert.ToHexStringLower(SHA256.HashData(stream)); }
}
