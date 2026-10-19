using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using RevitThyme.Core;

namespace RevitThyme.McpTests;

// No test loader, private host wiring or submitted callbacks. All operations use
// the actual installed stdio executable and its normally started native host.
internal static class ImageExportInstalled
{
    public static async Task Run(string folder)
    {
        using var inputJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "installed-input.json")));
        var input = inputJson.RootElement;
        string Text(string key) => input.GetProperty(key).GetString()!;
        string session = Text("session"), bundle = Text("bundle"), model = Text("fixture"), original = Text("original");
        string executable = Path.Combine(bundle, "mcp", "revitthyme-mcp.exe"), savedHash = Hash(model);
        var binding = SessionBinding.Load(session);
        Program.Check("installed image session matches exact owned process/start", binding.ProcessId == input.GetProperty("pid").GetInt32() && binding.ProcessStart == Text("start"));
        Program.Check("installed image fixture matches unchanged original", savedHash == Hash(original) && savedHash == Text("fixtureSha256"));
        Program.Check("installed image bundle is the pinned immutable manifest", Hash(Path.Combine(bundle, "manifest.json")) == Text("manifestSha256") && Path.GetFileName(bundle) == Text("manifestSha256"));
        void Payloads()
        {
            using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(bundle, "manifest.json")));
            foreach (var entry in manifest.RootElement.GetProperty("files").EnumerateObject())
                if (!Hash(Path.Combine(bundle, entry.Name)).Equals(entry.Value.GetString(), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Installed payload changed: " + entry.Name);
        }
        Payloads();
        using var oracleJson = JsonDocument.Parse(File.ReadAllText(Text("oracle"))); var oracle = oracleJson.RootElement;
        string View(string name) => oracle.GetProperty("views").EnumerateArray().Single(v => v.GetProperty("name").GetString() == name).GetProperty("uniqueId").GetString()!;
        var calls = new List<object>();
        async Task<McpClient> Client(bool pinned = true) => await McpClient.CreateAsync(new StdioClientTransport(new()
        { Command = executable, Arguments = pinned ? ["--session", session] : ["--sessions-directory", folder], Name = "Installed native image qualification" }));
        var client = await Client();
        try
        {
            async Task<JsonElement> Call(string name, Dictionary<string, object?>? args = null, string? code = null)
            {
                var reply = await Program.Call(client, name, args); calls.Add(new { name, args, result = reply.Data, reply.Result.IsError });
                if (code is not null) Program.Check("installed MCP " + name + ": " + code, reply.Data.GetProperty("code").GetString() == code && reply.Result.IsError == (code is not ("image_exported" or "applied")));
                else if (reply.Result.IsError == true) throw new InvalidDataException("Installed MCP " + name + " failed: " + reply.Data.GetRawText());
                if (reply.Data.TryGetProperty("imageExport", out var data) && data.ValueKind == JsonValueKind.Object && reply.Result.IsError != true)
                {
                    var image = data.Deserialize<ViewImageResult>(Protocol.Json)!; byte[] bytes = File.ReadAllBytes(image.Path);
                    Program.Check("installed native PNG and inline bytes independently match receipt", Hash(image.Path) == image.Sha256 && bytes.LongLength == image.Bytes && reply.Result.Content.OfType<ImageContentBlock>().Single().DecodedData.Span.SequenceEqual(bytes));
                    Program.Check("installed PNG dimensions match actual IHDR", System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4)) == image.Width
                        && System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4)) == image.Height);
                }
                return reply.Data;
            }
            async Task<(string Id, Snapshot Snapshot)> Context()
            {
                var data = await Call("get_context");
                return (data.GetProperty("captureId").GetString()!, data.GetProperty("result").GetProperty("snapshot").Deserialize<Snapshot>(Protocol.Json)!);
            }
            Dictionary<string, object?> Export(string capture, string view, string? id = null, int pixels = 1000) => new() { ["captureId"] = capture, ["requestId"] = id ?? Guid.NewGuid().ToString(), ["viewId"] = view, ["pixelSize"] = pixels };
            var context = await Context(); var baseline = context.Snapshot;
            Program.Check("normal installed host selects disposable fixture without trust opt-in", baseline.DocumentPath == model && !baseline.IsModified && !baseline.IsWorkshared);
            var inventory = await client.ListToolsAsync(); var operations = await Call("list_operations");
            Program.Check("actual installed server exposes 26 tools and protocol 9", inventory.Count == 26 && inventory.Any(t => t.Name == "export_view_image") && operations.GetProperty("protocolVersion").GetInt32() == 9);
            await Call("run_query", new() { ["captureId"] = context.Id, ["requestId"] = Guid.NewGuid().ToString(), ["source"] = "return 1;", ["trusted"] = true }, "trusted_code_disabled");
            async Task<int> Count(string capture)
            {
                int count = 0, offset = 0;
                while (true)
                {
                    var data = await Call("query_elements", new() { ["captureId"] = capture, ["offset"] = offset, ["limit"] = 100, ["includeTypes"] = true });
                    var page = data.GetProperty("elements").Deserialize<ElementPage>(Protocol.Json)!; count += page.Items.Length;
                    if (page.NextOffset is not { } next) return count;
                    if (next <= offset || next > 100000) throw new InvalidDataException("Invalid native element page."); offset = next;
                }
            }
            int beforeCount = await Count(context.Id); var selection = await Call("get_selection", new() { ["captureId"] = context.Id });
            foreach (string name in new[] { "Foundation Plan", "Occupied Name", "Image QA Sheet", "Image QA 3D" })
            {
                var image = await Call("export_view_image", Export(context.Id, View(name)), "image_exported");
                Program.Check("installed export targets explicit native view", image.GetProperty("imageExport").GetProperty("viewId").GetString() == View(name));
            }
            await Call("export_view_image", Export(context.Id, View("Image QA 3D"), pixels: 128), "image_exported");
            await Call("export_view_image", Export(context.Id, View("Image QA Template")), "unsupported_view");
            await Call("export_view_image", Export(context.Id, Guid.NewGuid() + "-00000001"), "unsupported_view");
            await Call("export_view_image", Export(context.Id, View("Foundation Plan"), pixels: 4097), "invalid_image_export");
            Program.Check("installed export does not change native context or element count", (await Context()).Snapshot == baseline && await Count(context.Id) == beforeCount && beforeCount == oracle.GetProperty("count").GetInt32());
            Program.Check("installed export preserves selection and saved files", (await Call("get_selection", new() { ["captureId"] = context.Id })).GetProperty("selection").GetRawText() == selection.GetProperty("selection").GetRawText() && Hash(model) == savedHash && Hash(original) == savedHash);
            var beforeImage = await Call("export_view_image", Export(context.Id, View("Image QA 3D")), "image_exported");
            var wall = oracle.GetProperty("wall"); string wallId = wall.GetProperty("uniqueId").GetString()!, heightId = wall.GetProperty("heightParameter").GetString()!; double height = wall.GetProperty("height").GetDouble();
            async Task<double> ReadHeight(string capture)
            {
                int offset = 0;
                while (true)
                {
                    var data = await Call("inspect_elements", new() { ["captureId"] = capture, ["elementIds"] = new[] { wallId }, ["parameterOffset"] = offset });
                    var detail = data.GetProperty("details")[0].Deserialize<ElementDetail>(Protocol.Json)!;
                    if (detail.Parameters.SingleOrDefault(p => p.Id == heightId) is { } parameter) return parameter.Value.Number!.Value;
                    offset = detail.NextParameterOffset ?? throw new InvalidDataException("Height parameter not found.");
                }
            }
            async Task Height(string capture, double expected, double value) => await Call("set_parameters", new() { ["captureId"] = capture, ["requestId"] = Guid.NewGuid().ToString(), ["edits"] = new[] { new ParameterEdit(wallId, heightId, new("Double", Number: expected), new("Double", Number: value)) } }, "applied");
            Program.Check("native height readback matches independent original", await ReadHeight(context.Id) == height);
            await Height(context.Id, height, 18); await Call("export_view_image", Export(context.Id, View("Image QA 3D")), "stale_target");
            context = await Context();
            var afterImage = await Call("export_view_image", Export(context.Id, View("Image QA 3D")), "image_exported");
            Program.Check("installed MCP edit see verify changes pixels and native value", await ReadHeight(context.Id) == 18 && beforeImage.GetProperty("imageExport").GetProperty("sha256").GetString() != afterImage.GetProperty("imageExport").GetProperty("sha256").GetString());
            await Height(context.Id, 18, height); context = await Context();
            Program.Check("installed native height restored without saving", await ReadHeight(context.Id) == height && Hash(model) == savedHash);
            var oldProcesses = Process.GetProcessesByName("revitthyme-mcp").Select(p => p.Id).ToHashSet();
            await client.DisposeAsync(); oldProcesses = Process.GetProcessesByName("revitthyme-mcp").Select(p => p.Id).ToHashSet();
            client = await Client();
            using var server = Process.GetProcessesByName("revitthyme-mcp").Single(p => !oldProcesses.Contains(p.Id));
            Program.Check("response-loss probe owns the exact installed MCP process", server.MainModule?.FileName == executable);
            context = await Context(); string lostId = Guid.NewGuid().ToString();
            var args = Export(context.Id, View("Image QA 3D"), lostId, 4096);
            var pending = Program.Call(client, "export_view_image", args);
            string marker = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RevitThyme", "view-images", binding.SessionId, lostId, "request.lock");
            var clock = Stopwatch.StartNew();
            while (!File.Exists(marker) && !pending.IsCompleted && clock.Elapsed.TotalSeconds < 20) await Task.Delay(2);
            Program.Check("installed native export accepted with reply still outstanding", File.Exists(marker) && !pending.IsCompleted);
            server.Kill(); await server.WaitForExitAsync();
            bool responseLost = false;
            try { await pending; } catch (Exception e) { responseLost = true; calls.Add(new { requestId = lostId, responseLost, exception = e.GetType().Name }); }
            Program.Check("installed accepted request actually loses its original response", responseLost);
            await client.DisposeAsync(); client = await Client();
            var recovered = await Call("get_operation", new() { ["requestId"] = lostId }, "image_exported");
            Program.Check("installed retained recovery returns original ID without a capture", recovered.GetProperty("id").GetString() == lostId);
            string path = recovered.GetProperty("imageExport").GetProperty("path").GetString()!; var stamp = File.GetLastWriteTimeUtc(path);
            var again = await Call("get_operation", new() { ["requestId"] = lostId }, "image_exported");
            Program.Check("installed recovery never re-renders completed output", File.GetLastWriteTimeUtc(path) == stamp && recovered.GetRawText() == again.GetRawText());
            context = await Context(); await Call("export_view_image", Export(context.Id, View("Image QA 3D"), lostId, 2048), "id_conflict");
            string stale = Path.Combine(folder, "stale.json"); File.WriteAllText(stale, JsonSerializer.Serialize(binding with { ProcessStart = "0" }, Protocol.Json));
            await Call("connect_session", new() { ["sessionFile"] = stale }, "session_pinned");
            await using (var disconnected = await Client(false))
            {
                var rejected = await Program.Call(disconnected, "connect_session", new() { ["sessionFile"] = stale });
                var notConnected = await Program.Call(disconnected, "get_context");
                Program.Check("installed stale process cannot connect or fall back", rejected.Result.IsError == true && notConnected.Data.GetProperty("code").GetString() == "not_connected");
            }
            var final = await Context();
            Program.Check("installed native final values counts selection and source preserved", await ReadHeight(final.Id) == height && await Count(final.Id) == beforeCount && Hash(model) == savedHash && Hash(original) == savedHash);
            Payloads();
            File.WriteAllText(Path.Combine(folder, "installed-mcp-complete.json"), JsonSerializer.Serialize(new { passed = true, binding, executable, responseLost, nativeCount = beforeCount, savedHash, baseline, final = final.Snapshot, calls }, Protocol.Json));
        }
        finally { await client.DisposeAsync(); File.WriteAllText(Path.Combine(folder, "installed-mcp-transcript.json"), JsonSerializer.Serialize(calls, Protocol.Json)); }
    }
    private static string Hash(string path) { using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); return Convert.ToHexStringLower(SHA256.HashData(stream)); }
}
