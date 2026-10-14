using System.Diagnostics;
using System.Text.Json;
using RevitThyme.Core;

namespace RevitThyme.McpTests;

internal static class UtilityLive
{
    public static async Task Run(string folder)
    {
        using var ready = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "ready.json")));
        using var launch = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "launch.json")));
        var binding = ready.RootElement.GetProperty("binding");
        Program.Check("utilities readiness uses owned process/start", binding.GetProperty("processId").GetInt32() == launch.RootElement.GetProperty("pid").GetInt32()
            && binding.GetProperty("processStart").GetString() == launch.RootElement.GetProperty("start").GetString());
        string session = Path.Combine(folder, "sessions", binding.GetProperty("sessionId").GetString() + ".json");
        await using var client = await Program.Client(session, Path.Combine(folder, "sessions"));
        var evidence = new List<object>();
        async Task<JsonElement> Call(string tool, Dictionary<string, object?>? args = null, string? code = null)
        {
            var call = await Program.Call(client, tool, args); evidence.Add(new { tool, args, error = call.Result.IsError, data = call.Data });
            Program.Check(tool + " " + (code ?? "succeeds"), code is null ? call.Result.IsError != true : call.Data.GetProperty("code").GetString() == code
                && call.Result.IsError == (code is not ("applied" or "inspected" or "unchanged")));
            return call.Data;
        }
        async Task<string> Context()
        {
            var data = await Call("get_context");
            Program.Check("utilities MCP pinned to exact fixture", data.GetProperty("result").GetProperty("snapshot").GetProperty("documentPath").GetString() == ready.RootElement.GetProperty("fixture").GetString());
            return data.GetProperty("captureId").GetString()!;
        }
        async Task<JsonElement> Cli(params string[] arguments)
        {
            var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            start.ArgumentList.Add(Path.Combine(folder, "bundle", "cli", "revitthyme.dll"));
            foreach (var arg in arguments) start.ArgumentList.Add(arg);
            using var p = Process.Start(start)!; var stdout = p.StandardOutput.ReadToEndAsync(); var stderr = p.StandardError.ReadToEndAsync();
            await p.WaitForExitAsync(); var text = await stdout;
            evidence.Add(new { cli = arguments, exit = p.ExitCode, text, error = await stderr });
            Program.Check("native CLI " + arguments[0] + " " + arguments[1], p.ExitCode == 0);
            return JsonDocument.Parse(text).RootElement.Clone();
        }
        try
        {
            Program.Check("18 official SDK tools in live session", (await client.ListToolsAsync()).Count == 18);
            var cliRange = await Cli("view-range", "get", "--session", session, "--json");
            Program.Check("CLI reads native range", cliRange.GetProperty("viewRange").GetProperty("settings").GetProperty("cut").GetProperty("offsetFeet").GetDouble() == 4);
            string captureFile = Path.Combine(folder, "cli-range.json"), rangeFile = Path.Combine(folder, "cli-settings.json");
            File.WriteAllText(captureFile, cliRange.GetRawText()); File.WriteAllText(rangeFile, cliRange.GetProperty("viewRange").GetProperty("settings").GetRawText());
            var cliNoop = await Cli("view-range", "set", "--session", session, "--capture", captureFile, "--range", rangeFile, "--json");
            Program.Check("CLI range no-op shares operation", cliNoop.GetProperty("code").GetString() == "unchanged");
            string roomId = ready.RootElement.GetProperty("roomId").GetString()!;
            var cliRoom = await Cli("room-dimensions", "preview", "--session", session, "--room", roomId, "--json");
            Program.Check("CLI reads native room finish dimensions", cliRoom.GetProperty("roomDimensions").GetProperty("lines").GetArrayLength() == 2);
            string capture = await Context();
            var read = await Call("get_view_range", new() { ["captureId"] = capture }, "inspected");
            var original = JsonSerializer.Deserialize<ViewRangeSettings>(read.GetProperty("viewRange").GetProperty("settings"), Core.Protocol.Json)!;
            var wanted = original with { Cut = original.Cut with { OffsetFeet = 5 + 1.0 / 96 } };
            await Call("set_view_range", new() { ["captureId"] = capture, ["requestId"] = Guid.NewGuid().ToString(), ["expectedRange"] = original, ["range"] = original }, "unchanged");
            string id = Guid.NewGuid().ToString();
            var arguments = new Dictionary<string, object?> { ["captureId"] = capture, ["requestId"] = id, ["expectedRange"] = original, ["range"] = wanted };
            await Call("set_view_range", arguments, "applied"); await Call("set_view_range", arguments, "applied");
            await Call("get_operation", new() { ["requestId"] = id }, "applied");
            await Call("get_view_range", new() { ["captureId"] = capture }, "stale_target");
            capture = await Context(); read = await Call("get_view_range", new() { ["captureId"] = capture }, "inspected");
            var actual = JsonSerializer.Deserialize<ViewRangeSettings>(read.GetProperty("viewRange").GetProperty("settings"), Core.Protocol.Json)!;
            Program.Check("MCP exact native eighth-inch readback", actual == wanted);
            await Call("set_view_range", new() { ["captureId"] = capture, ["requestId"] = Guid.NewGuid().ToString(), ["expectedRange"] = original, ["range"] = original }, "stale_range");
            await Call("set_view_range", new() { ["captureId"] = capture, ["requestId"] = Guid.NewGuid().ToString(), ["expectedRange"] = actual,
                ["range"] = actual with { Cut = actual.Cut with { OffsetFeet = 50 } } }, "invalid_view_range");
            capture = await Context();
            await Call("set_view_range", new() { ["captureId"] = capture, ["requestId"] = Guid.NewGuid().ToString(), ["expectedRange"] = actual, ["range"] = original }, "applied");
            capture = await Context();
            var previewData = await Call("preview_room_dimensions", new() { ["captureId"] = capture, ["roomId"] = roomId }, "inspected");
            var preview = JsonSerializer.Deserialize<RoomDimensionPreview>(previewData.GetProperty("roomDimensions"), Core.Protocol.Json)!;
            Program.Check("native room preview contains exact face pair and no dimensions", preview.Lines.Length == 2 && preview.ExistingIds.Length == 0);
            await Call("create_room_dimensions", new() { ["captureId"] = capture, ["requestId"] = Guid.NewGuid().ToString(), ["preview"] = preview with { Fingerprint = "forged" } }, "stale_room");
            id = Guid.NewGuid().ToString(); arguments = new() { ["captureId"] = capture, ["requestId"] = id, ["preview"] = preview };
            var made = await Call("create_room_dimensions", arguments, "applied"); await Call("create_room_dimensions", arguments, "applied");
            Program.Check("MCP returns two native element ids", made.GetProperty("changedIds").GetArrayLength() == 2);
            await Call("get_operation", new() { ["requestId"] = id }, "applied");
            await Call("preview_room_dimensions", new() { ["captureId"] = capture, ["roomId"] = roomId }, "stale_target");
            capture = await Context();
            var duplicate = await Call("preview_room_dimensions", new() { ["captureId"] = capture, ["roomId"] = roomId }, "inspected");
            Program.Check("native duplicate pair detected", duplicate.GetProperty("roomDimensions").GetProperty("existingIds").GetArrayLength() == 2);
            await Call("create_room_dimensions", new() { ["captureId"] = capture, ["requestId"] = Guid.NewGuid().ToString(), ["preview"] = duplicate.GetProperty("roomDimensions") }, "unchanged");
            File.WriteAllText(Path.Combine(folder, "utilities-mcp-complete.json"), JsonSerializer.Serialize(new { passed = true, evidence }, Core.Protocol.Json));
        }
        catch
        {
            File.WriteAllText(Path.Combine(folder, "utilities-mcp-failed.json"), JsonSerializer.Serialize(evidence, Core.Protocol.Json)); throw;
        }
    }
}
