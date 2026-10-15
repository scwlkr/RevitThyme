using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using RevitThyme.Core;

namespace RevitThyme.McpTests;

internal static class Program
{
    private static int checks;
    public static void Check(string name, bool value)
    { if (!value) throw new InvalidOperationException("FAIL " + name); checks++; Console.WriteLine("PASS " + name); }
    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args is ["--live", var folder]) await Live.Run(folder);
            else if (args is ["--utilities-live", var utilityFolder]) await UtilityLive.Run(utilityFolder);
            else if (args is ["--dynamic-live", var dynamicFolder]) await DynamicLive.Run(dynamicFolder);
            else { await Generic(); await ParameterIdentity.Run(); await Stdio(); }
            Console.WriteLine($"MCP checks: {checks} passed."); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
    private static async Task Generic()
    {
        var executor = new OperationExecutor(); var model = new FakeGenericModel();
        ParameterEdit[] Edits() => [new("element", "1", model.Values["1"], new("String", Text: "changed")), new("element", "2", model.Values["2"], new("Double", Number: 12))];
        OperationRequest Request() => new(Guid.NewGuid(), OperationKind.SetParameters, model.Target, Edits: Edits());
        var applied = executor.Execute(model, Request());
        Check("atomic typed batch verifies native readback", applied.Outcome == Outcome.Applied && model.Values["2"].Number == 12 && model.Transactions == 1);
        Check("unchanged batch opens no transaction", executor.Execute(model, Request()).Outcome == Outcome.Unchanged && model.Transactions == 1);
        model = new();
        Check("stale target rejected before writes", executor.Execute(model, Request() with { Target = model.Target with { DocumentId = "wrong" } }).Code == "stale_target" && model.Transactions == 0);
        Check("stale expected value rejected before writes", executor.Execute(model, Request() with { Edits = [Edits()[0] with { Expected = new("String", Text: "stale") }] }).Code == "stale_parameter" && model.Transactions == 0);
        Check("mixed storage type rejected", executor.Execute(model, Request() with { Edits = [Edits()[0] with { Value = new("Integer", Integer: 1) }] }).Code == "parameter_blocked");
        Check("duplicate parameters rejected", executor.Execute(model, Request() with { Edits = [Edits()[0], Edits()[0]] }).Code == "invalid_edits");
        Check("bad typed shape rejected", executor.Execute(model, Request() with { Edits = [Edits()[0] with { Value = new("String", Text: "x", Number: 1) }] }).Code == "invalid_edits");
        var large = Enumerable.Range(1, 20).Select(i => new ParameterEdit("element", i.ToString(), new("String", Text: new string('\u2603', 512)), new("String", Text: new string('\u2604', 512)))).ToArray();
        Check("oversized batch rejected before any mutation", executor.Execute(model, Request() with { Edits = large }).Code == "request_too_large" && model.Transactions == 0);
        model.ReadOnly = true; Check("read-only context rejects batch", executor.Execute(model, Request()).Code == "write_blocked");
        foreach (string fault in new[] { "second-set", "commit", "readback" })
        {
            model = new() { FailWrite = fault == "second-set" ? 2 : 0, FailCommit = fault == "commit", WrongReadback = fault == "readback" };
            Check("whole batch rollback after " + fault, executor.Execute(model, Request()).Outcome == Outcome.RolledBack && model.Values["1"].Text == "original" && model.Values["2"].Number == 9);
        }
        model = new() { FailWrite = 2, FailRollback = true };
        Check("rollback failure reports unconfirmed", executor.Execute(model, Request()).Outcome == Outcome.Unconfirmed);
        model = new(); var dispatcher = new OperationDispatcher(); var request = Request();
        var task = dispatcher.ExecuteAsync(request); var copy = JsonSerializer.Deserialize<OperationRequest>(JsonSerializer.Serialize(request, Core.Protocol.Json), Core.Protocol.Json)!;
        Check("array arguments replay by value", dispatcher.ExecuteAsync(copy) == task);
        Check("pending recovery does not queue a new edit", (await dispatcher.ExecuteAsync(new(Guid.NewGuid(), OperationKind.GetOperation, OperationId: request.Id))).Code == "pending");
        dispatcher.ExecuteNext(model);
        Check("native retained result recovered", (await dispatcher.ExecuteAsync(new(Guid.NewGuid(), OperationKind.GetOperation, OperationId: request.Id))).Id == request.Id && model.Transactions == 1);
        Check("replayed batch applies once", (await dispatcher.ExecuteAsync(copy)).Outcome == Outcome.Applied && model.Transactions == 1);
        Check("conflicting ID rejected", (await dispatcher.ExecuteAsync(request with { Edits = [Edits()[0]] })).Code == "id_conflict");
        using var cancel = new CancellationTokenSource(); var cancelled = dispatcher.ExecuteAsync(Request(), cancel.Token); cancel.Cancel(); dispatcher.ExecuteNext(model);
        Check("queued cancellation prevents mutation", (await cancelled).Code == "cancelled" && model.Transactions == 1);
        Check("missing retained ID reported", (await dispatcher.ExecuteAsync(new(Guid.NewGuid(), OperationKind.GetOperation, OperationId: Guid.NewGuid()))).Code == "not_found");
        Check("empty recovery request ID rejected", (await dispatcher.ExecuteAsync(new(Guid.Empty, OperationKind.GetOperation, OperationId: request.Id))).Code == "invalid_id");
        Check("invalid query limit rejected", executor.Execute(model, new(Guid.NewGuid(), OperationKind.QueryElements, model.Target, Query: new(Limit: 101))).Code == "invalid_query");
        Check("selection verifies without model transaction", executor.Execute(model, new(Guid.NewGuid(), OperationKind.SetSelection, model.Target, ElementIds: ["element"])).Outcome == Outcome.Applied && model.Transactions == 1);
        Check("selection clear supported", executor.Execute(model, new(Guid.NewGuid(), OperationKind.SetSelection, model.Target, ElementIds: [])).Selection?.Length == 0);
    }
    public static async Task<McpClient> Client(string? session = null, string? directory = null)
    {
        var dll = Path.Combine(AppContext.BaseDirectory, "revitthyme-mcp.dll");
        var args = new List<string> { dll };
        if (session is not null) args.AddRange(["--session", session]);
        if (directory is not null) args.AddRange(["--sessions-directory", directory]);
        return await McpClient.CreateAsync(new StdioClientTransport(new() { Command = "dotnet", Arguments = args, Name = "RevitThyme" }));
    }
    public static async Task<(CallToolResult Result, JsonElement Data)> Call(McpClient client, string name, Dictionary<string, object?>? args = null)
    {
        var result = await client.CallToolAsync(name, args ?? []);
        var text = result.Content.OfType<TextContentBlock>().Single().Text;
        return (result, JsonDocument.Parse(text).RootElement.Clone());
    }
    private static async Task Stdio()
    {
        await using var client = await Client(directory: Path.Combine(Path.GetTempPath(), "absent-" + Guid.NewGuid()));
        var tools = await client.ListToolsAsync();
        Check("official SDK stdio initialization and 18-tool discovery", tools.Count == 18);
        Check("trusted query is not advertised as sandboxed read-only", tools.Single(t => t.Name == "run_query").ProtocolTool.Annotations?.ReadOnlyHint == false
            && tools.Single(t => t.Name == "run_modify").JsonSchema.ToString().Contains("trusted"));
        Check("native utility tools expose structured schemas", tools.Single(t => t.Name == "create_room_dimensions").JsonSchema.ToString().Contains("fingerprint", StringComparison.OrdinalIgnoreCase)
            && tools.Single(t => t.Name == "set_view_range").JsonSchema.ToString().Contains("offsetFeet", StringComparison.OrdinalIgnoreCase));
        Check("write tools annotated", tools.Single(t => t.Name == "set_parameters").ProtocolTool.Annotations?.ReadOnlyHint == false);
        Check("parameter batch schema exposes typed fields", tools.Single(t => t.Name == "set_parameters").JsonSchema.ToString().Contains("expected", StringComparison.OrdinalIgnoreCase));
        var sessions = await Call(client, "list_sessions"); Check("empty discovery succeeds offline", sessions.Data.GetArrayLength() == 0);
        var context = await Call(client, "get_context"); Check("unconnected tool is structured MCP error", context.Result.IsError == true && context.Data.GetProperty("code").GetString() == "not_connected");
        var badId = await Call(client, "get_operation", new() { ["requestId"] = "bad" });
        Check("invalid request ID is MCP error", badId.Result.IsError == true && badId.Data.GetProperty("code").GetString() == "invalid_id");
        var ops = await Call(client, "list_operations"); Check("discovery declares units and recovery", ops.Data.GetProperty("parameterUnits").GetString()!.Contains("feet"));
        var badSession = await Call(client, "connect_session", new() { ["sessionFile"] = Path.Combine(Path.GetTempPath(), "absent.json") });
        Check("missing session fails without fallback", badSession.Result.IsError == true);
        var bad = await client.CallToolAsync("rename_view", new Dictionary<string, object?>());
        Check("missing required arguments rejected by SDK", bad.IsError == true);
    }
}
