using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using RevitThyme.Core;

namespace RevitThyme.McpTests;

// Normal installed host: no trusted C#, test loader or native callback fixture wiring.
internal static class UtilitiesInstalled
{
    public static async Task Run(string folder)
    {
        if (File.Exists(Path.Combine(folder, "pending.json")) || File.Exists(Path.Combine(folder, "complete.json")))
            throw new InvalidOperationException("Recover the retained pending request, or use a fresh run after completion; do not replay this workflow.");
        using var inputJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "installed-input.json"))); var input = inputJson.RootElement;
        string Text(string key) => input.GetProperty(key).GetString()!;
        string bundle = Text("bundle"), session = Text("session"), fixture = Text("fixture"), original = Text("original"), savedHash = Text("fixtureSha256");
        var binding = SessionBinding.Load(session); binding.AssertProcess();
        Program.Check("installed utility client pins owned process and unchanged source/copy", binding.ProcessId == input.GetProperty("pid").GetInt32()
            && binding.ProcessStart == Text("start") && binding.ProtocolVersion == 13 && Text("folder") == folder
            && Path.GetDirectoryName(fixture) == folder && fixture != original && Hash(fixture) == savedHash && Hash(original) == savedHash);
        Program.Check("installed utility client pins immutable runtime manifest", Hash(Path.Combine(bundle, "manifest.json")) == Text("manifestSha256"));
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(bundle, "manifest.json")));
        foreach (var item in manifest.RootElement.GetProperty("files").EnumerateObject())
            if (!Hash(Path.Combine(bundle, item.Name)).Equals(item.Value.GetString(), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Installed payload changed: " + item.Name);
        var calls = new List<object>();
        void Save(string name, object value) => File.WriteAllText(Path.Combine(folder, name), JsonSerializer.Serialize(value, Protocol.Json));
        async Task<McpClient> Client() => await McpClient.CreateAsync(new StdioClientTransport(new()
        { Command = Path.Combine(bundle, "mcp", "revitthyme-mcp.exe"), Arguments = ["--session", session], Name = "Installed native utilities qualification" }));
        var client = await Client();
        try
        {
            async Task<JsonElement> Call(string name, Dictionary<string, object?>? args = null, string? code = null)
            {
                string? requestId = name != "get_operation" && args?.GetValueOrDefault("requestId") is string id ? id : null;
                if (requestId is not null)
                {
                    var intent = new { name, requestId, args }; Save("intent-" + requestId + ".json", intent);
                    using var pending = new FileStream(Path.Combine(folder, "pending.json"), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                    pending.Write(JsonSerializer.SerializeToUtf8Bytes(intent, Protocol.Json));
                }
                var reply = await Program.Call(client, name, args); calls.Add(new { name, args, reply.Data, reply.Result.IsError }); Save("transcript.json", calls);
                if (requestId is not null && reply.Data.TryGetProperty("id", out var outcomeId) && outcomeId.GetString() == requestId
                    && reply.Data.TryGetProperty("outcome", out var outcome) && outcome.GetString() != "Unconfirmed" && reply.Data.GetProperty("code").GetString() != "pending")
                { Save("terminal-" + requestId + ".json", reply.Data); File.Delete(Path.Combine(folder, "pending.json")); }
                if (code is not null) Program.Check("installed " + name + ": " + code, reply.Data.GetProperty("code").GetString() == code
                    && reply.Result.IsError == (code is not ("inspected" or "applied" or "unchanged" or "view_activated" or "image_exported")));
                else if (reply.Result.IsError == true) throw new InvalidDataException(name + ": " + reply.Data.GetRawText());
                if (reply.Data.TryGetProperty("imageExport", out var metadata) && metadata.ValueKind == JsonValueKind.Object && reply.Result.IsError != true)
                {
                    var image = metadata.Deserialize<ViewImageResult>(Protocol.Json)!; var bytes = File.ReadAllBytes(image.Path);
                    Program.Check("utility native PNG matches inline bytes, hash and dimensions", image.Bytes == bytes.LongLength && image.Sha256 == Hash(image.Path)
                        && reply.Result.Content.OfType<ImageContentBlock>().Single().DecodedData.Span.SequenceEqual(bytes)
                        && System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4)) == image.Width
                        && System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4)) == image.Height);
                }
                return reply.Data;
            }
            async Task<(string Id, Snapshot Snapshot)> Context()
            {
                var r = await Call("get_context"); var snapshot = r.GetProperty("result").GetProperty("snapshot").Deserialize<Snapshot>(Protocol.Json)!;
                Program.Check("utility context stays on exact disposable native document", snapshot.DocumentPath == fixture && !snapshot.IsWorkshared);
                return (r.GetProperty("captureId").GetString()!, snapshot);
            }
            var context = await Context(); var baseline = context.Snapshot;
            Program.Check("utility normal startup is clean and exposes current MCP inventory", !baseline.IsModified && (await client.ListToolsAsync()).Count == 32);
            await Call("run_query", new() { ["captureId"] = context.Id, ["requestId"] = Guid.NewGuid().ToString(), ["trusted"] = true, ["source"] = "return 1;" }, "trusted_code_disabled");
            var page = await Call("list_views", new() { ["captureId"] = context.Id, ["limit"] = 100 });
            var views = page.GetProperty("elements").Deserialize<ElementPage>(Protocol.Json)!;
            Program.Check("utility baseline exposes complete small view inventory", views.NextOffset is null);
            string View(string name) => views.Items.Single(v => v.Name == name && v.IsTemplate == false).UniqueId;
            async Task Activate(string name)
            { await Call("activate_view", new() { ["captureId"] = context.Id, ["requestId"] = Guid.NewGuid().ToString(), ["viewId"] = View(name) }, "view_activated"); context = await Context(); }
            async Task<ViewRangeInfo> Range() => (await Call("get_view_range", new() { ["captureId"] = context.Id }, "inspected")).GetProperty("viewRange").Deserialize<ViewRangeInfo>(Protocol.Json)!;
            async Task<JsonElement> Set(ViewRangeSettings expected, ViewRangeSettings wanted, string code = "applied", string? id = null) => await Call("set_view_range",
                new() { ["captureId"] = context.Id, ["requestId"] = id ?? Guid.NewGuid().ToString(), ["expectedRange"] = expected, ["range"] = wanted }, code);
            async Task<JsonElement> Image(string name) => await Call("export_view_image", new() { ["captureId"] = context.Id, ["requestId"] = Guid.NewGuid().ToString(), ["viewId"] = View(name), ["pixelSize"] = 1600 }, "image_exported");
            await Activate("Utilities Plan"); var floor = await Range();
            Program.Check("floor native four-plane oracle matches fixture", floor.CanEdit && floor.Settings.Top.OffsetFeet == 8 && floor.Settings.Cut.OffsetFeet == 4
                && floor.Settings.Bottom.OffsetFeet == 0 && floor.Settings.Depth.OffsetFeet == -1 && floor.AssociatedLevelFeet == 0
                && floor.Levels.Single(l => l.Name == "Utilities Level").ElevationFeet == 0 && floor.Levels.Single(l => l.Name == "Upper Level").ElevationFeet == 12
                && floor.Levels.Single(l => l.Name.StartsWith("Long Level ", StringComparison.Ordinal)).ElevationFeet == 24);
            var wanted = floor.Settings with { Cut = floor.Settings.Cut with { OffsetFeet = 5 + 1.0 / 96 } };
            await Set(floor.Settings, floor.Settings, "unchanged");
            string editId = Guid.NewGuid().ToString(); var applied = await Set(floor.Settings, wanted, id: editId);
            await Call("get_view_range", new() { ["captureId"] = context.Id }, "stale_target"); context = await Context();
            Program.Check("floor exact eighth-inch edit reads back natively", (await Range()).Settings == wanted);
            await Image("Utilities Plan");
            await client.DisposeAsync(); client = await Client();
            var recovered = await Call("get_operation", new() { ["requestId"] = editId }, "applied");
            Program.Check("reconnected utility client recovers original edit receipt", recovered.GetRawText() == applied.GetRawText()); context = await Context();
            await Set(floor.Settings, floor.Settings, "stale_range");
            await Set(wanted, wanted with { Cut = wanted.Cut with { OffsetFeet = 50 } }, "invalid_view_range"); context = await Context();
            Program.Check("native invalid ordering rolls back all four planes", (await Range()).Settings == wanted);
            await Set(wanted, floor.Settings); context = await Context();
            Program.Check("floor original native range restored without saving", (await Range()).Settings == floor.Settings && Hash(fixture) == savedHash);
            Save("range-complete.json", new { passed = true, binding, baseline, floor, editId, fixtureSha256 = savedHash, calls });
            await Activate("Template Controlled"); var controlled = await Range();
            Program.Check("assigned native template explains view-range refusal", !controlled.CanEdit && controlled.EditBlock?.Contains("template", StringComparison.OrdinalIgnoreCase) == true);
            await Set(controlled.Settings, controlled.Settings with { Cut = controlled.Settings.Cut with { OffsetFeet = 5 } }, "write_blocked");
            Program.Check("template refusal preserves exact native snapshot and range", (await Context()).Snapshot == context.Snapshot && (await Range()).Settings == controlled.Settings);
            await Activate("Utilities Ceiling"); var ceiling = await Range();
            var ceilingWanted = ceiling.Settings with { Cut = ceiling.Settings.Cut with { OffsetFeet = ceiling.Settings.Cut.OffsetFeet + 1.0 / 96 },
                Bottom = ceiling.Settings.Bottom with { OffsetFeet = ceiling.Settings.Bottom.OffsetFeet + 1.0 / 96 } };
            await Set(ceiling.Settings, ceilingWanted); context = await Context();
            Program.Check("RCP native coincident planes move together exactly", (await Range()).Settings == ceilingWanted);
            await Image("Utilities Ceiling"); await Set(ceilingWanted, ceiling.Settings); context = await Context();
            Program.Check("RCP original four-plane range is restored", (await Range()).Settings == ceiling.Settings);
            await Activate("Utilities 3D"); await Call("get_view_range", new() { ["captureId"] = context.Id }, "unsupported_view");
            await Activate("Utilities Plan");
            var rooms = (await Call("query_elements", new() { ["captureId"] = context.Id, ["categoryId"] = "-2000160", ["limit"] = 10 })).GetProperty("elements").Deserialize<ElementPage>(Protocol.Json)!;
            Program.Check("utility fixture contains three native test rooms", rooms.NextOffset is null && rooms.Items.Length == 3 && rooms.Items.All(e => e.Class == "Room"));
            string roomId = rooms.Items.Single(e => e.Name.StartsWith("Rectangle", StringComparison.Ordinal)).UniqueId;
            string irregularId = rooms.Items.Single(e => e.Name.StartsWith("L Shape", StringComparison.Ordinal)).UniqueId;
            async Task<double> Number(string element, string parameter)
            {
                int offset = 0;
                while (true)
                {
                    var detail = (await Call("inspect_elements", new() { ["captureId"] = context.Id, ["elementIds"] = new[] { element }, ["parameterOffset"] = offset }))
                        .GetProperty("details")[0].Deserialize<ElementDetail>(Protocol.Json)!;
                    if (detail.Parameters.SingleOrDefault(p => p.BuiltInParameter == parameter) is { } p) return p.Value.Number ?? throw new InvalidDataException("Expected native double " + parameter);
                    offset = detail.NextParameterOffset ?? throw new InvalidDataException("Native parameter missing: " + parameter);
                }
            }
            async Task<RoomDimensionPreview> Preview() => (await Call("preview_room_dimensions", new() { ["captureId"] = context.Id, ["roomId"] = roomId }, "inspected"))
                .GetProperty("roomDimensions").Deserialize<RoomDimensionPreview>(Protocol.Json)!;
            var preview = await Preview(); double nativeArea = await Number(roomId, "ROOM_AREA");
            Program.Check("read-only finish-face preview agrees with native room area", preview.Lines.Length == 2 && preview.ExistingIds.Length == 0
                && Math.Abs(preview.WidthFeet * preview.DepthFeet - nativeArea) < 1e-4);
            await Call("preview_room_dimensions", new() { ["captureId"] = context.Id, ["roomId"] = irregularId }, "unsupported_room");
            await Call("create_room_dimensions", new() { ["captureId"] = context.Id, ["requestId"] = Guid.NewGuid().ToString(), ["preview"] = preview with { Fingerprint = "forged" } }, "stale_room");
            var beforeImage = await Image("Utilities Plan");
            string dimensionsId = Guid.NewGuid().ToString(); var createArgs = new Dictionary<string, object?> { ["captureId"] = context.Id, ["requestId"] = dimensionsId, ["preview"] = preview };
            var created = await Call("create_room_dimensions", createArgs, "applied");
            Program.Check("same room request replays its original native result", (await Call("create_room_dimensions", createArgs, "applied")).GetRawText() == created.GetRawText());
            context = await Context(); var duplicate = await Preview();
            var ids = created.GetProperty("changedIds").EnumerateArray().Select(x => x.GetInt64()).Order().ToArray();
            Program.Check("native pair is independently detected after create", ids.Length == 2 && duplicate.ExistingIds.Order().SequenceEqual(ids));
            await Call("create_room_dimensions", new() { ["captureId"] = context.Id, ["requestId"] = Guid.NewGuid().ToString(), ["preview"] = duplicate }, "unchanged");
            var dimensions = (await Call("query_elements", new() { ["captureId"] = context.Id, ["categoryId"] = "-2000260", ["limit"] = 100 })).GetProperty("elements").Deserialize<ElementPage>(Protocol.Json)!;
            Program.Check("typed native inspection finds exactly the created dimensions", dimensions.NextOffset is null && dimensions.Items.Length == 2 && dimensions.Items.All(e => ids.Contains(e.Id)));
            var measured = new List<double>(); foreach (var e in dimensions.Items) measured.Add(await Number(e.UniqueId, "DIM_VALUE_LENGTH"));
            var expected = new[] { preview.WidthFeet, preview.DepthFeet }.Order().ToArray();
            Program.Check("independent native dimension values equal room finish extents", measured.Order().Zip(expected).All(x => Math.Abs(x.First - x.Second) < 1e-5));
            var afterImage = await Image("Utilities Plan");
            Program.Check("new native dimensions change the exact plan image", beforeImage.GetProperty("imageExport").GetProperty("sha256").GetString() != afterImage.GetProperty("imageExport").GetProperty("sha256").GetString());
            await client.DisposeAsync(); client = await Client();
            Program.Check("new external connection recovers original dimension pair without creation", (await Call("get_operation", new() { ["requestId"] = dimensionsId }, "applied")).GetRawText() == created.GetRawText());
            context = await Context();
            Save("undo-ready.json", new { binding, context.Snapshot, dimensionsId, ids, instruction = "In the exact owned Revit window apply Undo once for RevitThyme Room Dimensions, then write undo-done.json." });
            var wait = Stopwatch.StartNew();
            while (!File.Exists(Path.Combine(folder, "undo-done.json")) && wait.Elapsed.TotalMinutes < 10) await Task.Delay(250);
            if (!File.Exists(Path.Combine(folder, "undo-done.json"))) throw new TimeoutException("Native Undo observation remains outstanding; do not replay creation.");
            context = await Context(); var afterUndo = await Preview();
            Program.Check("one native Undo removes both dimensions and restores room preview", afterUndo.ExistingIds.Length == 0 && afterUndo.WidthFeet == preview.WidthFeet && afterUndo.DepthFeet == preview.DepthFeet);
            await Image("Utilities Plan");
            Program.Check("installed utility workflow preserves original range and both saved files", (await Range()).Settings == floor.Settings && Hash(fixture) == savedHash && Hash(original) == savedHash);
            Save("complete.json", new { passed = true, binding, baseline, floor, ceiling, editId, dimensionsId, ids, measured, nativeArea, context.Snapshot, savedHash, calls });
        }
        finally { await client.DisposeAsync(); Save("transcript.json", calls); }
    }
    private static string Hash(string path) { using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); return Convert.ToHexStringLower(SHA256.HashData(stream)); }
}
