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
            else if (args is ["--image-live", var imageFolder]) await ImageExportLive.Run(imageFolder);
            else if (args is ["--image-baseline", var executable, var inventory]) await ImageExportLive.Baseline(executable, inventory);
            else if (args is ["--image-installed", var installedFolder]) await ImageExportInstalled.Run(installedFolder);
            else if (args is ["--lifecycle-live", var lifecycleFolder]) await DocumentLifecycleLive.Run(lifecycleFolder);
            else if (args is ["--ui-lifecycle-images", var uiImagesFolder]) await UiLifecycleLive.Images(uiImagesFolder);
            else if (args is ["--ui-lifecycle-live", var uiLifecycleFolder]) await UiLifecycleLive.Run(uiLifecycleFolder);
            else if (args is ["--active-close-live", var closeFolder]) await ActiveCloseLive.Run(closeFolder);
            else if (args is ["--modal-recovery-live", var modalFolder]) await ModalRecoveryLive.Run(modalFolder);
            else if (args is ["--documents-live", var documentsFolder]) await DocumentDiscoveryLive.Run(documentsFolder);
            else if (args is ["--document-scope-live", var scopeFolder]) await DocumentDiscoveryScopeLive.Run(scopeFolder);
            else if (args is ["--restricted-documents-live", var restrictedFolder]) await RestrictedDocumentsLive.Run(restrictedFolder);
            else if (args is ["--electrical-lab-live", var electricalFolder]) await ElectricalLabLive.Run(electricalFolder);
            else if (args is ["--electrical-lab-observe", var observeFolder, var originalGuid]) await ElectricalLabLive.ObserveRun(observeFolder, originalGuid);
            else if (args is ["--electrical-lab-resume", var resumeFolder, var activationGuid]) await ElectricalLabLive.ResumeRun(resumeFolder, activationGuid);
            else if (args is ["--electrical-lab-duplicate-diagnostic", var diagnosticFolder]) await ElectricalLabLive.DuplicateDiagnostic(diagnosticFolder);
            else if (args is ["--electrical-lab-inspect-applied", var appliedFolder, var appliedGuid]) await ElectricalLabLive.InspectApplied(appliedFolder, appliedGuid);
            else if (args is ["--electrical-lab-continue-applied", var continueFolder, var duplicateGuid]) await ElectricalLabLive.ContinueApplied(continueFolder, duplicateGuid);
            else if (args is ["--close-cancellation-baseline", var baselineFolder]) await ActiveCloseLive.Run(baselineFolder, true);
            else if (args is ["--lifecycle-reopened", var lifecycleReopened]) await DocumentLifecycleLive.Run(lifecycleReopened, true);
            else if (args is ["--utilities-live", var utilityFolder]) await UtilityLive.Run(utilityFolder);
            else if (args is ["--dynamic-live", var dynamicFolder]) await DynamicLive.Run(dynamicFolder);
            else if (args is ["--timberfold-live", var timberFolder, var fixtureReceipt]) await TimberFoldLive.Run(timberFolder, fixtureReceipt);
            else if (args is ["--packaged-stdio", var bundle, var manifestSha256, var evidence]) await PackagedStdio.Run(bundle, manifestSha256, evidence);
            else if (args is ["--parameter-identity-live", var identityFolder, var identityStage]) await ParameterIdentityLive.Run(identityFolder, identityStage);
            else { await Generic(); await ParameterIdentity.Run(); await ImageExportReplies.Run(); await TimberFoldRuns.Run(); await Stdio(); }
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
        Check("official SDK stdio initialization and 32-tool discovery", tools.Count == 32);
        var documentsTool = tools.Single(t => t.Name == "list_documents");
        Check("document discovery exposes retained read and stable continuation without lifecycle flags", documentsTool.ProtocolTool.Annotations is { ReadOnlyHint: true, IdempotentHint: true }
            && documentsTool.JsonSchema.ToString().Contains("requestId", StringComparison.Ordinal)
            && documentsTool.JsonSchema.ToString().Contains("inventoryToken", StringComparison.Ordinal)
            && !documentsTool.JsonSchema.ToString().Contains("expectedSha256", StringComparison.Ordinal));
        Check("image export exposes bounded captured idempotent file operation", tools.Single(t => t.Name == "export_view_image").ProtocolTool.Annotations is { ReadOnlyHint: false, DestructiveHint: false, IdempotentHint: true }
            && tools.Single(t => t.Name == "export_view_image").JsonSchema.ToString().Contains("viewId", StringComparison.Ordinal));
        var reconcileTool = tools.Single(t => t.Name == "timberfold_reconcile");
        Check("reconciliation advertises captured idempotent recovery evidence without geometry overrides", reconcileTool.ProtocolTool.Annotations is
            { ReadOnlyHint: false, DestructiveHint: false, IdempotentHint: true } &&
            reconcileTool.JsonSchema.ToString().Contains("generationRunId", StringComparison.Ordinal) &&
            reconcileTool.JsonSchema.ToString().Contains("captureId", StringComparison.Ordinal) &&
            !reconcileTool.JsonSchema.ToString().Contains("directory", StringComparison.OrdinalIgnoreCase));
        var previewTool = tools.Single(t => t.Name == "timberfold_preview");
        var statusTool = tools.Single(t => t.Name == "timberfold_status");
        var cancelTool = tools.Single(t => t.Name == "timberfold_cancel");
        var generateTool = tools.Single(t => t.Name == "timberfold_generate");
        var verifyTool = tools.Single(t => t.Name == "timberfold_verify");
        Check("TimberFold generation advertises captured reversible writes and retained verification is read-only", generateTool.ProtocolTool.Annotations is
            { ReadOnlyHint: false, DestructiveHint: false, IdempotentHint: true } && verifyTool.ProtocolTool.Annotations?.ReadOnlyHint == true &&
            generateTool.JsonSchema.ToString().Contains("captureId", StringComparison.Ordinal) &&
            generateTool.JsonSchema.ToString().Contains("previewRunId", StringComparison.Ordinal) &&
            !generateTool.JsonSchema.ToString().Contains("stockWidthInches", StringComparison.OrdinalIgnoreCase) &&
            verifyTool.JsonSchema.ToString().Contains("runId", StringComparison.Ordinal) &&
            !verifyTool.JsonSchema.ToString().Contains("captureId", StringComparison.Ordinal));
        Check("TimberFold preview/cancel declare idempotent private writes and status is read-only", previewTool.ProtocolTool.Annotations is
                { ReadOnlyHint: false, IdempotentHint: true } && cancelTool.ProtocolTool.Annotations is { ReadOnlyHint: false, IdempotentHint: true } &&
            statusTool.ProtocolTool.Annotations?.ReadOnlyHint == true);
        Check("TimberFold run schemas distinguish captured preview from direct status/cancellation", previewTool.JsonSchema.ToString().Contains("captureId", StringComparison.Ordinal) &&
            previewTool.JsonSchema.ToString().Contains("requestId", StringComparison.Ordinal) && previewTool.JsonSchema.ToString().Contains("scaleDenominator", StringComparison.OrdinalIgnoreCase) &&
            statusTool.JsonSchema.ToString().Contains("runId", StringComparison.Ordinal) && !statusTool.JsonSchema.ToString().Contains("captureId", StringComparison.Ordinal) &&
            cancelTool.JsonSchema.ToString().Contains("runId", StringComparison.Ordinal) && !cancelTool.JsonSchema.ToString().Contains("captureId", StringComparison.Ordinal));
        Check("TimberFold inspection declares private evidence writes and captured typed settings", tools.Single(t => t.Name == "timberfold_inspect").ProtocolTool.Annotations?.ReadOnlyHint == false
            && tools.Single(t => t.Name == "timberfold_inspect").JsonSchema.ToString().Contains("stockWidthInches", StringComparison.OrdinalIgnoreCase));
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
