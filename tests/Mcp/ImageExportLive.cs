using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using RevitThyme.Core;

namespace RevitThyme.McpTests;

internal static class ImageExportLive
{
    public static async Task Baseline(string executable, string evidence)
    {
        await using var client = await McpClient.CreateAsync(new StdioClientTransport(new() { Command = executable, Arguments = [], Name = "Installed v0.10.0 inventory" }));
        var tools = await client.ListToolsAsync(); var operations = await Program.Call(client, "list_operations");
        Program.Check("installed v0.10.0 has 25 tools without image export", tools.Count == 25 && !tools.Any(t => t.Name == "export_view_image"));
        Program.Check("installed v0.10.0 reports protocol 8 and trusted execution", operations.Data.GetProperty("protocolVersion").GetInt32() == 8 && tools.Any(t => t.Name == "run_query"));
        File.WriteAllText(evidence, JsonSerializer.Serialize(new { executable, tools = tools.Select(t => t.Name), operations = operations.Data }, Protocol.Json));
    }
    public static async Task Run(string folder)
    {
        if (!Path.IsPathFullyQualified(folder)) throw new ArgumentException("Use an absolute owned run folder.");
        using var readyDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "ready.json")));
        var ready = readyDoc.RootElement;
        var evidence = new List<object>();
        string session = Path.Combine(folder, "session.json");
        var binding = SessionBinding.Load(session);
        using var launchDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "launch.json")));
        Program.Check("external MCP target pinned to owned process/start", binding.ProcessId == launchDoc.RootElement.GetProperty("pid").GetInt32()
            && binding.ProcessStart == launchDoc.RootElement.GetProperty("start").GetString());
        await using (var discovery = await Program.Client(directory: folder))
        {
            string staleFile = Path.Combine(folder, "dead-start-session.json");
            File.WriteAllText(staleFile, JsonSerializer.Serialize(binding with { ProcessStart = "0" }, Protocol.Json));
            var stale = await Program.Call(discovery, "connect_session", new() { ["sessionFile"] = staleFile });
            var disconnected = await Program.Call(discovery, "get_context");
            Program.Check("external MCP rejects stale process start without choosing fallback", stale.Result.IsError == true && disconnected.Data.GetProperty("code").GetString() == "not_connected");
            evidence.Add(new { staleSession = stale.Data, disconnected = disconnected.Data });
            var connected = await Program.Call(discovery, "connect_session", new() { ["sessionFile"] = session });
            Program.Check("external MCP recovers explicit connection after stale binding", connected.Result.IsError != true && connected.Data.GetProperty("sessionId").GetString() == binding.SessionId);
        }
        string View(string name) => ready.GetProperty("views").EnumerateArray().Single(v => v.GetProperty("name").GetString() == name).GetProperty("uniqueId").GetString()!;
        async Task<McpClient> Client() => await Program.Client(session);
        var client = await Client();
        try
        {
            async Task<JsonElement> Call(string name, Dictionary<string, object?> args, string? expected = null)
            {
                var reply = await Program.Call(client, name, args); evidence.Add(new { name, args, result = reply.Data, reply.Result.IsError });
                if (reply.Data.TryGetProperty("imageExport", out var inline) && inline.ValueKind == JsonValueKind.Object && reply.Result.IsError != true)
                {
                    var png = reply.Result.Content.OfType<ImageContentBlock>().Single();
                    Program.Check("external agent receives verified inline PNG", png.MimeType == "image/png" && Convert.ToHexString(SHA256.HashData(png.DecodedData.Span)).Equals(inline.GetProperty("sha256").GetString(), StringComparison.OrdinalIgnoreCase));
                }
                if (expected is not null) Program.Check("external MCP " + name + ": " + expected, reply.Data.GetProperty("code").GetString() == expected && reply.Result.IsError == (expected != "image_exported" && expected != "applied"));
                return reply.Data;
            }
            async Task<(string Id, Snapshot Snapshot)> Context()
            {
                var reply = await Program.Call(client, "get_context"); evidence.Add(new { name = "get_context", result = reply.Data });
                var snapshot = reply.Data.GetProperty("result").GetProperty("snapshot").Deserialize<Snapshot>(Protocol.Json)!;
                return (reply.Data.GetProperty("captureId").GetString()!, snapshot);
            }
            Dictionary<string, object?> Args(string capture, string view, string? id = null, int size = 1000) => new() { ["captureId"] = capture, ["requestId"] = id ?? Guid.NewGuid().ToString(), ["viewId"] = view, ["pixelSize"] = size };
            void Image(JsonElement reply, string exactView)
            {
                var image = reply.GetProperty("imageExport").Deserialize<ViewImageResult>(Protocol.Json)!;
                byte[] bytes = File.ReadAllBytes(image.Path);
                Program.Check("external image exact identity and independent file hash", image.ViewId == exactView && bytes.LongLength == image.Bytes
                    && Convert.ToHexString(SHA256.HashData(bytes)).Equals(image.Sha256, StringComparison.OrdinalIgnoreCase));
                Program.Check("external image PNG signature and dimensions", bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
                    && System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4)) == image.Width
                    && System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4)) == image.Height && Math.Max(image.Width, image.Height) <= 4096);
            }
            async Task Command(string command)
            {
                string receipt = Path.Combine(folder, "command-done.json"); if (File.Exists(receipt)) File.Delete(receipt);
                File.WriteAllText(Path.Combine(folder, "command.txt"), command);
                await Wait(receipt);
                await Task.Delay(700);
                if (File.Exists(Path.Combine(folder, "native-failure.json"))) throw new InvalidOperationException(File.ReadAllText(Path.Combine(folder, "native-failure.json")));
            }
            var initial = await Context();
            string active = initial.Snapshot.Target.ViewId;
            var activeImage = await Call("export_view_image", Args(initial.Id, active), "image_exported"); Image(activeImage, active);
            var sheet = await Call("export_view_image", Args(initial.Id, View("Image QA Sheet")), "image_exported"); Image(sheet, View("Image QA Sheet"));
            var inactive = await Call("export_view_image", Args(initial.Id, View("Occupied Name")), "image_exported"); Image(inactive, View("Occupied Name"));
            var beforeGeometry = await Call("export_view_image", Args(initial.Id, View("Image QA 3D")), "image_exported");
            await Command("audit");
            var audit = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "audit.json"))).RootElement.Clone();
            Program.Check("actual native exports preserve element count saved hash active view selection and modified flag", audit.GetProperty("count").GetInt32() == ready.GetProperty("count").GetInt32()
                && audit.GetProperty("savedHash").GetString() == ready.GetProperty("savedHash").GetString() && !audit.GetProperty("isModified").GetBoolean()
                && audit.GetProperty("activeView").GetString() == active && audit.GetProperty("selection").GetArrayLength() == 0);
            await Call("export_view_image", Args(initial.Id, View("Image QA Template")), "unsupported_view");
            await Call("export_view_image", Args(initial.Id, Guid.NewGuid() + "-00000001"), "unsupported_view");
            await Call("export_view_image", Args(initial.Id, active, size: 4097), "invalid_image_export");
            var wall = ready.GetProperty("wall");
            Dictionary<string, object?> Height(string capture, double expected, double value) => new() { ["captureId"] = capture, ["requestId"] = Guid.NewGuid().ToString(), ["edits"] = new[] { new ParameterEdit(wall.GetProperty("uniqueId").GetString()!, wall.GetProperty("heightParameter").GetString()!, new("Double", Number: expected), new("Double", Number: value)) } };
            double originalHeight = wall.GetProperty("height").GetDouble();
            await Call("set_parameters", Height(initial.Id, originalHeight, 18), "applied");
            await Call("export_view_image", Args(initial.Id, View("Image QA 3D")), "stale_target");
            var geometryContext = await Context();
            var afterGeometry = await Call("export_view_image", Args(geometryContext.Id, View("Image QA 3D")), "image_exported"); Image(afterGeometry, View("Image QA 3D"));
            Program.Check("MCP geometry edit is visible in exported 3D pixels", beforeGeometry.GetProperty("imageExport").GetProperty("sha256").GetString() != afterGeometry.GetProperty("imageExport").GetProperty("sha256").GetString());
            await Call("set_parameters", Height(geometryContext.Id, 18, originalHeight), "applied");
            initial = await Context();
            var rename = await Call("rename_view", new() { ["captureId"] = initial.Id, ["requestId"] = Guid.NewGuid().ToString(), ["name"] = "Foundation Plan edited via MCP" }, "applied");
            await Call("export_view_image", Args(initial.Id, active), "stale_target");
            var edited = await Context();
            var editedImage = await Call("export_view_image", Args(edited.Id, active), "image_exported"); Image(editedImage, active);
            Program.Check("edit see verify returns native renamed view", editedImage.GetProperty("imageExport").GetProperty("viewName").GetString() == "Foundation Plan edited via MCP");
            await Call("rename_view", new() { ["captureId"] = edited.Id, ["requestId"] = Guid.NewGuid().ToString(), ["name"] = "Foundation Plan" }, "applied");
            var current = await Context();
            string repeatId = Guid.NewGuid().ToString(); var repeatArgs = Args(current.Id, active, repeatId);
            var exported = await Call("export_view_image", repeatArgs, "image_exported");
            string path = exported.GetProperty("imageExport").GetProperty("path").GetString()!; var timestamp = File.GetLastWriteTimeUtc(path);
            await Call("export_view_image", repeatArgs, "image_exported");
            await Call("export_view_image", Args(current.Id, active, repeatId, 1200), "id_conflict");
            Program.Check("native same-ID replay does not rewrite file", timestamp == File.GetLastWriteTimeUtc(path));
            byte[] originalPng = File.ReadAllBytes(path);
            File.WriteAllBytes(path, new byte[24]);
            await Call("get_operation", new() { ["requestId"] = repeatId }, "image_artifact_unavailable");
            Program.Check("changed image recovery does not regenerate output", File.ReadAllBytes(path).Length == 24);
            File.WriteAllBytes(path, originalPng);
            await Call("get_operation", new() { ["requestId"] = repeatId }, "image_exported");
            // Kill only the owned stdio server after the native queue has accepted the request.
            var beforeServers = Process.GetProcessesByName("dotnet").Select(p => p.Id).ToHashSet();
            await client.DisposeAsync();
            beforeServers = Process.GetProcessesByName("dotnet").Select(p => p.Id).ToHashSet();
            client = await Client();
            var ownedServers = Process.GetProcessesByName("dotnet").Where(p => !beforeServers.Contains(p.Id)).ToArray();
            Program.Check("single owned external MCP server selected for response-loss probe", ownedServers.Length == 1);
            current = await Context(); string lostId = Guid.NewGuid().ToString();
            File.WriteAllText(Path.Combine(folder, "pause-queue"), "owned recovery probe");
            var lost = Program.Call(client, "export_view_image", Args(current.Id, active, lostId));
            await Wait(Path.Combine(folder, "queue-pending.json"));
            ownedServers.Single().Kill(); await ownedServers.Single().WaitForExitAsync();
            try { await lost; } catch (Exception e) { evidence.Add(new { responseLost = e.GetType().Name, requestId = lostId }); }
            File.Delete(Path.Combine(folder, "pause-queue"));
            await client.DisposeAsync(); client = await Client();
            var recovered = await Call("get_operation", new() { ["requestId"] = lostId }, "image_exported"); Image(recovered, active);
            Program.Check("new external client recovers original request identity", recovered.GetProperty("id").GetString() == lostId);
            current = await Context();
            var deadBinding = binding with { ProcessStart = "0" };
            string deadSession = Path.Combine(folder, "stale-session.json"); File.WriteAllText(deadSession, JsonSerializer.Serialize(deadBinding, Protocol.Json));
            await Call("connect_session", new() { ["sessionFile"] = deadSession }, "session_pinned");
            await Command("switch");
            await Call("export_view_image", Args(current.Id, active), "stale_target");
            await Command("wrong-document");
            await Call("export_view_image", Args(current.Id, active), "wrong_fixture");
            await Command("restore-document");
            await Command("audit");
            var finalAudit = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "audit.json"))).RootElement.Clone();
            Program.Check("MCP edits restored values and preserved saved source", finalAudit.GetProperty("viewName").GetString() == "Foundation Plan"
                && finalAudit.GetProperty("count").GetInt32() == ready.GetProperty("count").GetInt32() && finalAudit.GetProperty("savedHash").GetString() == ready.GetProperty("savedHash").GetString());
            await Command("reopen");
            await Call("export_view_image", Args(current.Id, active), "reopened_fixture");
            File.WriteAllText(Path.Combine(folder, "image-mcp-complete.json"), JsonSerializer.Serialize(new { passed = true, binding, evidence, audit, finalAudit }, Protocol.Json));
        }
        finally { await client.DisposeAsync(); File.WriteAllText(Path.Combine(folder, "image-mcp-transcript.json"), JsonSerializer.Serialize(evidence, Protocol.Json)); }
    }
    private static async Task Wait(string path)
    {
        var clock = Stopwatch.StartNew();
        while (!File.Exists(path)) { if (clock.Elapsed.TotalSeconds > 45) throw new TimeoutException(path); await Task.Delay(100); }
    }
}
