using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RevitThyme.Core;

namespace RevitThyme.Mcp;

public sealed partial class RevitTools
{
    [McpServerTool(Name = "prepare_result_checks", ReadOnly = false, Destructive = true, Idempotent = true),
        Description("Before executing a standalone C# workflow, observe its permitted modified/deleted native IDs at the exact captured target. Supply the ORIGINAL action requestId and SHA-256 of its exact C# source, reviewed plan and inputs. Returns deterministic preparation/verification IDs and retained native receipt. Checker runs outside an active transaction under a rollback guard; full process trust, no sandbox. Do not execute the action if preparation fails. Use unchanged plan/inputs/source with verify_result after action.")]
    public Task<CallToolResult> PrepareResultChecks(string captureId, string requestId, ResultCheckPlan plan, JsonElement inputs, string sourceSha256,
        bool trusted = false, int budgetMilliseconds = 10000, CancellationToken cancellationToken = default) => Safe(async () =>
    {
        var result = await connection.PrepareChecks(captureId, Id(requestId), plan, inputs, sourceSha256, trusted, budgetMilliseconds, cancellationToken);
        return Reply(result, !result.Receipt.Succeeded);
    });

    [McpServerTool(Name = "verify_result", ReadOnly = false, Destructive = true, Idempotent = true),
        Description("Verify/recover a retained original native C# action without executing it again. Fetches get_operation, checks exact source, pre-action policy and post-action revision, runs independent native observations and compares declared expected results/change limits. Supply the identical prepared plan/inputs/source SHA-256; no caller-supplied action result is trusted. Original outcome is preserved even when verification fails/errors/unconfirmed; stop and inspect those statuses. Checker is full-process trusted, not sandboxed.")]
    public Task<CallToolResult> VerifyResult(string requestId, ResultCheckPlan plan, JsonElement inputs, string sourceSha256,
        bool trusted = false, int budgetMilliseconds = 10000, CancellationToken cancellationToken = default) => Safe(async () =>
    {
        var result = await connection.VerifyChecks(Id(requestId), plan, inputs, sourceSha256, trusted, budgetMilliseconds, cancellationToken);
        return Reply(result, !result.Succeeded || result.Verification?.Status != "passed");
    });

    [McpServerTool(Name = "verify_tool", ReadOnly = false, Destructive = true, Idempotent = true),
        Description("Recover/recheck a saved tool's retained original requestId using its exact reviewed digest and original inputs. Does not execute the saved action. Uses the definition's resultChecks; missing retained preparation or intervening native changes cannot pass.")]
    public Task<CallToolResult> VerifyTool(string requestId, string toolId, string digest, JsonElement inputs,
        bool trusted = false, int budgetMilliseconds = 10000, CancellationToken cancellationToken = default) => Safe(async () =>
    {
        var artifact = new SavedToolStore().Get(toolId);
        var code = SavedTools.Invocation(artifact, digest, inputs, trusted, budgetMilliseconds);
        var plan = artifact.Definition.ResultChecks ?? throw new ArgumentException("Saved tool has no resultChecks plan.");
        var result = await connection.VerifyChecks(Id(requestId), plan, inputs, ResultChecks.Hash(code.Source), trusted, budgetMilliseconds, cancellationToken);
        return Reply(result, !result.Succeeded || result.Verification?.Status != "passed");
    });
}
