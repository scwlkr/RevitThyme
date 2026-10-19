using System.Text.Json;
using ModelContextProtocol.Client;
using RevitThyme.Core;

namespace RevitThyme.McpTests;

internal static class Live
{
    public static async Task Run(string folder)
    {
        using var ready = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "ready.json")));
        using var launch = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "launch.json")));
        var binding = ready.RootElement.GetProperty("binding");
        Program.Check("readiness bound to owned process", binding.GetProperty("processId").GetInt32() == launch.RootElement.GetProperty("pid").GetInt32()
            && binding.GetProperty("processStart").GetString() == launch.RootElement.GetProperty("start").GetString());
        string session = Path.Combine(folder, "sessions", binding.GetProperty("sessionId").GetString() + ".json");
        await using var client = await Program.Client(session, Path.Combine(folder, "sessions"));
        var evidence = new List<object>();
        async Task<JsonElement> Call(string tool, Dictionary<string, object?>? args = null, string? expected = null)
        {
            var call = await Program.Call(client, tool, args); evidence.Add(new { tool, args, error = call.Result.IsError, data = call.Data });
            if (expected is not null) Program.Check(tool + " " + expected, call.Data.GetProperty("code").GetString() == expected
                && call.Result.IsError == (expected is not ("applied" or "inspected" or "unchanged")));
            else Program.Check(tool + " succeeds", call.Result.IsError != true);
            return call.Data;
        }
        async Task<string> Context()
        {
            var data = await Call("get_context");
            Program.Check("context stays in pinned fixture", data.GetProperty("result").GetProperty("snapshot").GetProperty("documentPath").GetString() == ready.RootElement.GetProperty("fixture").GetString());
            return data.GetProperty("captureId").GetString()!;
        }
        try
        {
            Program.Check("live 19-tool discovery", (await client.ListToolsAsync()).Count == 19);
            var sessions = await Call("list_sessions"); Program.Check("owned session discovered", sessions.EnumerateArray().Any(s => s.GetProperty("binding").GetProperty("processId").GetInt32() == binding.GetProperty("processId").GetInt32()));
            await Call("connect_session", new() { ["sessionFile"] = session });
            await Call("connect_session", new() { ["sessionFile"] = Path.Combine(folder, "wrong.json") }, "session_pinned");
            await Call("list_operations"); string capture = await Context();
            var views = await Call("list_views", new() { ["captureId"] = capture, ["limit"] = 1 });
            Program.Check("view paging available", views.GetProperty("elements").GetProperty("nextOffset").GetInt32() == 1);
            await Call("list_views", new() { ["captureId"] = capture, ["offset"] = 1, ["limit"] = 1 });
            var first = await Call("query_elements", new() { ["captureId"] = capture, ["categoryId"] = "-2000011", ["limit"] = 1 });
            var second = await Call("query_elements", new() { ["captureId"] = capture, ["categoryId"] = "-2000011", ["limit"] = 1, ["offset"] = 1 });
            var ids = new[] { first.GetProperty("elements").GetProperty("items")[0].GetProperty("uniqueId").GetString()!, second.GetProperty("elements").GetProperty("items")[0].GetProperty("uniqueId").GetString()! };
            Program.Check("query pages return distinct native walls", ids.Distinct().Count() == 2 && second.GetProperty("elements").GetProperty("nextOffset").ValueKind == JsonValueKind.Null);
            var parameters = new Dictionary<string, List<ParameterInfo>>();
            foreach (string id in ids)
            {
                var list = new List<ParameterInfo>(); int? offset = 0;
                while (offset is not null)
                {
                    var detail = await Call("inspect_elements", new() { ["captureId"] = capture, ["elementIds"] = new[] { id }, ["parameterOffset"] = offset.Value });
                    var d = detail.GetProperty("details")[0]; list.AddRange(JsonSerializer.Deserialize<ParameterInfo[]>(d.GetProperty("parameters"), Core.Protocol.Json)!);
                    offset = d.GetProperty("nextParameterOffset").ValueKind == JsonValueKind.Null ? null : d.GetProperty("nextParameterOffset").GetInt32();
                }
                parameters.Add(id, list);
            }
            await Call("query_elements", new() { ["captureId"] = capture, ["limit"] = 0 }, "invalid_query");
            await Call("inspect_elements", new() { ["captureId"] = capture, ["elementIds"] = new[] { "missing" } }, "element_missing");
            await Call("get_selection", new() { ["captureId"] = capture });
            await Call("set_selection", new() { ["captureId"] = capture, ["requestId"] = Guid.NewGuid().ToString(), ["elementIds"] = ids }, "applied");
            var selected = await Call("get_selection", new() { ["captureId"] = capture });
            Program.Check("native selection contains both walls", selected.GetProperty("selection").GetArrayLength() == 2);
            string renameId = Guid.NewGuid().ToString(); var renameArgs = new Dictionary<string, object?> { ["captureId"] = capture, ["requestId"] = renameId, ["name"] = "MCP Renamed" };
            await Call("rename_view", renameArgs, "applied"); await Call("rename_view", renameArgs, "applied");
            await Call("get_operation", new() { ["requestId"] = renameId }, "applied");
            await Call("rename_view", new() { ["captureId"] = capture, ["requestId"] = renameId, ["name"] = "Conflict" }, "id_conflict");
            await Call("query_elements", new() { ["captureId"] = capture }, "stale_target");
            capture = await Context(); await Call("rename_view", new() { ["captureId"] = capture, ["requestId"] = Guid.NewGuid().ToString(), ["name"] = "Occupied Name" }, "rolled_back");
            capture = await Context(); await Call("rename_view", new() { ["captureId"] = capture, ["requestId"] = Guid.NewGuid().ToString(), ["name"] = "Foundation Plan" }, "applied");
            capture = await Context();
            var edits = ids.SelectMany(id => new[] { "Comments", "Unconnected Height", "Room Bounding" }.Select(name =>
            {
                var p = parameters[id].Single(p => p.Name == name);
                var value = name switch { "Comments" => new ParameterValue("String", Text: "MCP edited " + id), "Unconnected Height" => new ParameterValue("Double", Number: 12), _ => new ParameterValue("Integer", Integer: 0) };
                return new ParameterEdit(id, p.Id, p.Value, value);
            })).ToArray();
            string editId = Guid.NewGuid().ToString(); var editArgs = new Dictionary<string, object?> { ["captureId"] = capture, ["requestId"] = editId, ["edits"] = edits };
            await Call("set_parameters", editArgs, "applied"); await Call("set_parameters", editArgs, "applied");
            await Call("get_operation", new() { ["requestId"] = editId }, "applied");
            capture = await Context();
            await Call("set_parameters", new() { ["captureId"] = capture, ["requestId"] = Guid.NewGuid().ToString(), ["edits"] = edits }, "stale_parameter");
            var restore = edits.Select(e => e with { Expected = e.Value, Value = e.Expected }).ToArray();
            await Call("set_parameters", new() { ["captureId"] = capture, ["requestId"] = Guid.NewGuid().ToString(), ["edits"] = restore }, "applied");
            capture = await Context();
            var noOp = edits.Select(e => e with { Value = e.Expected }).ToArray();
            await Call("set_parameters", new() { ["captureId"] = capture, ["requestId"] = Guid.NewGuid().ToString(), ["edits"] = noOp }, "unchanged");
            // First setter succeeds; second setter duplicates a native view name, proving whole-batch rollback.
            var context = await Call("get_context"); capture = context.GetProperty("captureId").GetString()!;
            string viewId = context.GetProperty("result").GetProperty("snapshot").GetProperty("target").GetProperty("viewId").GetString()!;
            var viewParameters = new List<ParameterInfo>(); int? viewOffset = 0;
            while (viewOffset is not null)
            {
                var viewDetail = await Call("inspect_elements", new() { ["captureId"] = capture, ["elementIds"] = new[] { viewId }, ["parameterOffset"] = viewOffset.Value });
                var d = viewDetail.GetProperty("details")[0]; viewParameters.AddRange(JsonSerializer.Deserialize<ParameterInfo[]>(d.GetProperty("parameters"), Core.Protocol.Json)!);
                viewOffset = d.GetProperty("nextParameterOffset").ValueKind == JsonValueKind.Null ? null : d.GetProperty("nextParameterOffset").GetInt32();
            }
            var viewName = viewParameters.Single(p => p.Name == "View Name");
            await Call("set_parameters", new() { ["captureId"] = capture, ["requestId"] = Guid.NewGuid().ToString(), ["edits"] = new[] { edits[0], new ParameterEdit(viewId, viewName.Id, viewName.Value, new("String", Text: "Occupied Name")) } }, "rolled_back");
            capture = await Context();
            var clear = await Call("set_selection", new() { ["captureId"] = capture, ["requestId"] = Guid.NewGuid().ToString(), ["elementIds"] = Array.Empty<string>() });
            Program.Check("selection cleared or already empty after native rollback", clear.GetProperty("selection").GetArrayLength() == 0);
            await Call("get_operation", new() { ["requestId"] = Guid.NewGuid().ToString() }, "not_found");
            File.WriteAllText(Path.Combine(folder, "mcp-complete.json"), JsonSerializer.Serialize(new { passed = true, ids, parameters, evidence }, Core.Protocol.Json));
        }
        catch
        {
            File.WriteAllText(Path.Combine(folder, "mcp-failed-" + Guid.NewGuid() + ".json"), JsonSerializer.Serialize(evidence, Core.Protocol.Json)); throw;
        }
    }
}
