using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RevitThyme.Core;

namespace RevitThyme.Mcp;

public sealed partial class RevitTools
{
    [McpServerTool(Name = "save_tool", ReadOnly = false, Destructive = false, Idempotent = true),
        Description("Save a reusable C# Revit tool from the user's request. Supply a new toolId GUID and complete typed definition with inputs, expected result and executable assertion tests/fixture assumptions. Same ID/content replay is safe; different content cannot replace it. Private local storage; does not execute or claim tests passed.")]
    public Task<CallToolResult> SaveTool(string toolId, SavedToolDefinition definition) =>
        Safe(() => Task.FromResult(Reply(new SavedToolStore().Save(toolId, definition))));

    [McpServerTool(Name = "get_tool", ReadOnly = true), Description("Read a saved tool's exact definition and digest by toolId. Review source, inputs, expected result and test assumptions before executing. TestStatus not_run means saving alone provides no verification.")]
    public Task<CallToolResult> GetTool(string toolId) => Safe(() => Task.FromResult(Reply(new SavedToolStore().Get(toolId))));

    [McpServerTool(Name = "run_tool", ReadOnly = false, Destructive = true, Idempotent = true),
        Description("Execute a saved tool with its reviewed digest and validated JSON inputs, fresh capture/request ID and trusted=true. Full process trust, host opt-in, no sandbox. Optional testName executes that stored assertion as a rollback query, after separately running the tool on a disposable fixture; expect boolean true. No managed test status is recorded. On timeout poll get_operation with original ID; do not repeat with a new ID.")]
    public Task<CallToolResult> RunTool(string captureId, string requestId, string toolId, string digest, JsonElement inputs,
        bool trusted = false, int budgetMilliseconds = 10000, string? testName = null, CancellationToken cancellationToken = default) => Safe(async () =>
    {
        var artifact = new SavedToolStore().Get(toolId);
        var code = SavedTools.Invocation(artifact, digest, inputs, trusted, budgetMilliseconds, testName);
        var operation = testName is not null || artifact.Definition.Mode == "query" ? OperationKind.RunQuery : OperationKind.RunModify;
        return await Run(captureId, new(Id(requestId), operation, Code: code), cancellationToken);
    });
}
