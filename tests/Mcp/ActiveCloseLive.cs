using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using ModelContextProtocol.Client;
using RevitThyme.Core;

namespace RevitThyme.McpTests;

internal static class ActiveCloseLive
{
    public static async Task Run(string folder, bool baseline = false)
    {
        using var inputJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "input.json"))); var input = inputJson.RootElement;
        string Text(string key) => input.GetProperty(key).GetString()!;
        string bundle = Text("bundle"), executable = Path.Combine(bundle, "mcp", "revitthyme-mcp.exe"), session = Text("session"), a = Text("a"), b = Text("b"), original = Text("sourceSha256");
        var binding = JsonSerializer.Deserialize<SessionBinding>(File.ReadAllText(session), Protocol.Json)!; binding.AssertProcess();
        using var probe = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "probe-ready.json")));
        Program.Check("close client targets exact installed bundle, owned process and cancellation probe", binding.ProcessId == input.GetProperty("pid").GetInt32()
            && binding.ProcessStart == Text("start") && binding.ProtocolVersion == (baseline ? 11 : Protocol.Version)
            && probe.RootElement.GetProperty("pid").GetInt32() == binding.ProcessId && probe.RootElement.GetProperty("start").GetString() == binding.ProcessStart
            && Hash(Path.Combine(bundle, "manifest.json")) == Text("manifestSha256"));
        var calls = new List<object>();
        async Task<McpClient> Client() => await McpClient.CreateAsync(new StdioClientTransport(new() { Command = executable, Arguments = ["--session", session], Name = "Exact close handoff qualification" }));
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
            Dictionary<string, object?> Args(string cap, string path, string? doc = null, string? hash = null, CloseActivationTarget? next = null, string? id = null)
            {
                var args = new Dictionary<string, object?> { ["captureId"] = cap, ["requestId"] = id ?? Guid.NewGuid().ToString(), ["path"] = path };
                if (doc is not null) args["documentId"] = doc; if (hash is not null) args["expectedSha256"] = hash;
                if (next is not null) args["activateBeforeClose"] = next; return args;
            }
            static string DocId(JsonElement r) => r.GetProperty("documentLifecycle").GetProperty("documentId").GetString()!;
            async Task<JsonElement> CancelClose(string cap, string path, string doc, string hash, CloseActivationTarget? next = null)
            {
                string id = Guid.NewGuid().ToString(); var args = Args(cap, path, doc, hash, next, id);
                File.WriteAllText(Path.Combine(folder, "cancel-close.json"), JsonSerializer.Serialize(new { path, id }));
                File.WriteAllText(Path.Combine(folder, "intent-" + id + ".json"), JsonSerializer.Serialize(new { name = "close_document", id, args }, Protocol.Json));
                var result = await Call("close_document", args, "lifecycle_unconfirmed");
                Program.Check("actual third-party native DocumentClosing cancellation observed", File.Exists(Path.Combine(folder, "cancelled-" + id + ".json")) && result.GetProperty("outcome").GetString() == "Unconfirmed");
                var recovered = await Program.Call(observer, "get_operation", new() { ["requestId"] = id });
                Program.Check("cancelled native close recovers identical unconfirmed receipt", recovered.Data.GetRawText() == result.GetRawText());
                return result;
            }
            var context = await Context(); Program.Check("close qualification starts at Home on unchanged disposable copies", !context.Snapshot.HasDocument && Hash(a) == original && Hash(b) == original);
            string aid = DocId(await Call("open_document", Args(context.Id, a, hash: original), "document_opened"));
            string bid = DocId(await Call("open_document", Args(context.Id, b, hash: original), "document_opened"));
            await Call("activate_document", Args(context.Id, a, aid), "document_activated"); context = await Context(); var beforeCancel = context.Snapshot;
            await CancelClose(context.Id, b, bid, original); context = await Context();
            string sameB = DocId(await Call("open_document", Args(context.Id, b, hash: original), "document_opened"));
            Program.Check("cancelled background close preserves both document identities and active view", sameB == bid && context.Snapshot.Target.DocumentId == aid
                && context.Snapshot.Target.ViewId == beforeCancel.Target.ViewId && !context.Snapshot.IsModified && Hash(a) == original && Hash(b) == original);
            if (baseline) return;
            var next = new CloseActivationTarget(bid, b, original);
            await CancelClose(context.Id, a, aid, original, next); context = await Context();
            Program.Check("first activation of a background-only handoff verifies native clean state", context.Snapshot.Target.DocumentId == bid && !context.Snapshot.IsModified
                && DocId(await Call("open_document", Args(context.Id, a, hash: original), "document_opened")) == aid);
            await Call("activate_document", Args(context.Id, a, aid), "document_activated"); context = await Context();
            await Call("close_document", Args(context.Id, a, aid, original, next with { DocumentId = aid }), "invalid_lifecycle");
            await Call("close_document", Args(context.Id, a, aid, original, next with { DocumentId = "wrong" }), "wrong_document");
            await Call("close_document", Args(context.Id, a, aid, original, next with { Path = a }), "invalid_lifecycle");
            await Call("close_document", Args(context.Id, a, aid, original, next with { ExpectedSha256 = new('0', 64) }), "file_changed");
            Program.Check("wrong handoff preflight preserves exact active target", (await Context()).Snapshot == context.Snapshot);
            await Call("rename_view", new() { ["captureId"] = context.Id, ["requestId"] = Guid.NewGuid().ToString(), ["name"] = "Close A dirty guard" }, "applied"); context = await Context();
            await Call("close_document", Args(context.Id, a, aid, original, next), "document_dirty");
            Program.Check("dirty close target remains active and unsaved", (await Context()).Snapshot.Target.DocumentId == aid && (await Context()).Snapshot.IsModified && Hash(a) == original);
            await Call("save_document", Args(context.Id, a, aid, original), "document_saved"); string savedA = Hash(a); context = await Context();
            await Call("activate_document", Args(context.Id, b, bid), "document_activated"); context = await Context();
            await Call("rename_view", new() { ["captureId"] = context.Id, ["requestId"] = Guid.NewGuid().ToString(), ["name"] = "Close B dirty guard" }, "applied"); context = await Context();
            await Call("activate_document", Args(context.Id, a, aid), "document_activated"); context = await Context();
            await Call("close_document", Args(context.Id, a, aid, savedA, next), "document_dirty");
            Program.Check("dirty handoff refuses before changing active A", (await Context()).Snapshot == context.Snapshot && Hash(b) == original);
            await Call("activate_document", Args(context.Id, b, bid), "document_activated"); context = await Context();
            await Call("save_document", Args(context.Id, b, bid, original), "document_saved"); string savedB = Hash(b); context = await Context();
            string stale = context.Id;
            await Call("activate_document", Args(context.Id, a, aid), "document_activated"); context = await Context();
            next = next with { ExpectedSha256 = savedB };
            await Call("close_document", Args(stale, a, aid, savedA, next), "stale_target");
            await Call("close_document", Args(context.Id, b, bid, savedB), "document_closed"); context = await Context();
            string bid2 = DocId(await Call("open_document", Args(context.Id, b, hash: savedB), "document_opened"));
            Program.Check("reopened handoff has a different exact native identity", bid2 != bid);
            await Call("close_document", Args(context.Id, a, aid, savedA, next), "wrong_document"); next = next with { DocumentId = bid2 };
            await CancelClose(context.Id, a, aid, savedA, next); context = await Context();
            Program.Check("cancelled active close leaves explicit handoff active without rollback or file changes", context.Snapshot.Target.DocumentId == bid2 && !context.Snapshot.IsModified && Hash(a) == savedA && Hash(b) == savedB);
            Program.Check("cancelled active close preserves the still-open original document identity", DocId(await Call("open_document", Args(context.Id, a, hash: savedA), "document_opened")) == aid);
            await Call("activate_document", Args(context.Id, a, aid), "document_activated");
            await client.DisposeAsync(); var prior = Process.GetProcessesByName("revitthyme-mcp").Select(p => p.Id).ToHashSet(); client = await Client();
            using var server = Process.GetProcessesByName("revitthyme-mcp").Single(p => !prior.Contains(p.Id));
            Program.Check("lost-response experiment owns exact installed MCP server", server.MainModule?.FileName == executable); context = await Context();
            string lostId = Guid.NewGuid().ToString(); var lostArgs = Args(context.Id, a, aid, savedA, next, lostId);
            File.WriteAllText(Path.Combine(folder, "intent-" + lostId + ".json"), JsonSerializer.Serialize(new { name = "close_document", lostId, lostArgs, context.Snapshot.Target }, Protocol.Json));
            var pending = Program.Call(client, "close_document", lostArgs); var watch = Stopwatch.StartNew(); JsonElement retained;
            do { retained = (await Program.Call(observer, "get_operation", new() { ["requestId"] = lostId })).Data; if (retained.GetProperty("code").GetString() != "not_found") break; await Task.Delay(2); }
            while (!pending.IsCompleted && watch.Elapsed.TotalSeconds < 10);
            Program.Check("handoff close accepted while original reply is outstanding", retained.GetProperty("code").GetString() != "not_found" && !pending.IsCompleted);
            server.Kill(); await server.WaitForExitAsync(); bool lost = false;
            try { await pending; } catch (Exception error) { lost = true; calls.Add(new { lostId, error = error.GetType().Name }); }
            Program.Check("active-close original response actually lost", lost);
            do { retained = (await Program.Call(observer, "get_operation", new() { ["requestId"] = lostId })).Data; if (retained.GetProperty("code").GetString() != "pending") break; await Task.Delay(50); }
            while (watch.Elapsed.TotalSeconds < 70);
            Program.Check("original active close recovered verified completion with exact handoff", retained.GetProperty("id").GetString() == lostId && retained.GetProperty("code").GetString() == "document_closed"
                && retained.GetProperty("outcome").GetString() == "Completed" && retained.GetProperty("snapshot").GetProperty("target").GetProperty("documentId").GetString() == bid2);
            calls.Add(new { name = "recovered_active_close", lostId, retained });
            await client.DisposeAsync(); client = await Client(); context = await Context();
            Program.Check("native handoff context and saved files verify after recovery", context.Snapshot.Target.DocumentId == bid2 && !context.Snapshot.IsModified && Hash(a) == savedA && Hash(b) == savedB);
            string aid2 = DocId(await Call("open_document", Args(context.Id, a, hash: savedA), "document_opened"));
            Program.Check("native original document closed and reopened with new ID", aid2 != aid);
            var recoveredAgain = await Program.Call(observer, "get_operation", new() { ["requestId"] = lostId });
            Program.Check("repeated recovery never closes reopened target", recoveredAgain.Data.GetRawText() == retained.GetRawText() && DocId(await Call("open_document", Args(context.Id, a, hash: savedA), "document_opened")) == aid2);
            await Call("close_document", Args(context.Id, b, bid2, savedB, new(aid2, a, savedA)), "document_closed"); context = await Context();
            Program.Check("reverse explicit handoff closes only B and activates exact A", context.Snapshot.Target.DocumentId == aid2 && !context.Snapshot.IsModified);
            await Call("close_document", Args(context.Id, a, aid2, savedA), "close_blocked");
            Program.Check("last active close remains explicit blocked with source preserved", (await Context()).Snapshot == context.Snapshot && Hash(Text("source")) == original && Hash(a) == savedA && Hash(b) == savedB);
            File.WriteAllText(Path.Combine(folder, "complete.json"), JsonSerializer.Serialize(new { passed = true, binding, context.Snapshot, savedA, savedB, calls }, Protocol.Json));
        }
        finally { await client.DisposeAsync(); File.WriteAllText(Path.Combine(folder, "transcript.json"), JsonSerializer.Serialize(calls, Protocol.Json)); }
    }
    private static string Hash(string path) { using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); return Convert.ToHexStringLower(SHA256.HashData(stream)); }
}
