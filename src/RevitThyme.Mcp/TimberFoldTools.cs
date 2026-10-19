using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RevitThyme.Core;

namespace RevitThyme.Mcp;

public sealed partial class RevitTools
{
    [McpServerTool(Name = "timberfold_inspect", ReadOnly = false, Destructive = false),
        Description("Inspect captured exterior-wall/planar-roof kit scope, source accounting and fabrication profile; retain private snapshot/diagnostic files. No model edits or cutting files. Physical inches in profile, Revit feet in extraction; incomplete geometry blocks preview. Null scope reports active phase/main-primary options.")]
    public Task<CallToolResult> TimberFoldInspect(string captureId, TimberFoldProfile? profile = null,
        TimberScope? scope = null, CancellationToken cancellationToken = default) =>
        Run(captureId, new(Guid.NewGuid(), OperationKind.InspectTimberFold, TimberFold: new(profile, scope)), cancellationToken);

    [McpServerTool(Name = "timberfold_preview", ReadOnly = false, Destructive = false, Idempotent = true),
        Description("Start a retained digital kit preview from a fresh captured target and caller requestId/run ID. Writes private nominal-inch DXF, calibration and traceability files; no model edits or save. Poll timberfold_status; retry only identical ID/settings. Native import and physical checks remain outstanding; manufacturing readiness stays false. Kerf compensation belongs to laser software.")]
    public Task<CallToolResult> TimberFoldPreview(string captureId, string requestId, TimberFoldProfile? profile = null,
        TimberScope? scope = null, CancellationToken cancellationToken = default) =>
        Safe(() => Run(captureId, new(Id(requestId), OperationKind.PreviewTimberFold, TimberFold: new(profile, scope)), cancellationToken));

    [McpServerTool(Name = "timberfold_generate", ReadOnly = false, Destructive = false, Idempotent = true),
        Description("Generate a native review kit from a retained verified previewRunId and its unchanged captured source target. Supply a new requestId for the generation run; settings come from the preview. Creates native review geometry, views/sheets and private guides/exports in one Undo group; never saves the source model. Returns a run handle; poll timberfold_status. Physical cutting and assembly remain outstanding. Recover uncertain completion using the same ID, never a new generation ID.")]
    public Task<CallToolResult> TimberFoldGenerate(string captureId, string requestId, string previewRunId,
        CancellationToken cancellationToken = default) => Safe(() =>
    {
        Guid id = Id(requestId), parent = Id(previewRunId);
        if (id == parent) throw new OperationException("invalid_id", "Use a new generation requestId distinct from previewRunId.");
        return Run(captureId, new(id, OperationKind.GenerateTimberFold, OperationId: parent), cancellationToken);
    });

    [McpServerTool(Name = "timberfold_status", ReadOnly = true, Idempotent = true),
        Description("Read a retained TimberFold run by runId in the explicitly bound session. No get_context, native extraction or UI queue required. Reports stage, diagnostics and private artifact directory; digital previews retain outstanding native import and physical checks.")]
    public Task<CallToolResult> TimberFoldStatus(string runId, CancellationToken cancellationToken = default) => Safe(async () =>
    {
        var result = await connection.TimberRun(Id(runId), false, cancellationToken);
        return Reply(result, !result.Succeeded);
    });

    [McpServerTool(Name = "timberfold_reconcile", ReadOnly = false, Destructive = false, Idempotent = true),
        Description("Reconcile an already committed generation against a fresh current capture of the same native process/session/document. Supply a new requestId for separate recovery evidence and the original generationRunId. Reads existing native elements, geometry, labels and immutable files; never creates a replacement kit or saves the model. Poll timberfold_status with the recovery ID. Missing/ambiguous commit evidence or absent/changed native documentation remain Unconfirmed. Original run files and timings remain unchanged; physical checks stay outstanding.")]
    public Task<CallToolResult> TimberFoldReconcile(string captureId, string requestId, string generationRunId,
        CancellationToken cancellationToken = default) => Safe(() =>
    {
        Guid id = Id(requestId), generation = Id(generationRunId);
        if (id == generation) throw new OperationException("invalid_id", "Use a new reconciliation requestId distinct from generationRunId.");
        return Run(captureId, new(id, OperationKind.ReconcileTimberFold, OperationId: generation), cancellationToken);
    });

    [McpServerTool(Name = "timberfold_verify", ReadOnly = true, Idempotent = true),
        Description("Independently recheck retained preview or generated-kit evidence and file hashes by runId in the bound session. No capture, native edits or save. Missing or changed evidence cannot remain a verified kit; physical cutting/assembly checks remain outstanding.")]
    public Task<CallToolResult> TimberFoldVerify(string runId, CancellationToken cancellationToken = default) => Safe(async () =>
    {
        var result = await connection.VerifyTimberRun(Id(runId), cancellationToken);
        return Reply(result, !result.Succeeded);
    });

    [McpServerTool(Name = "timberfold_cancel", ReadOnly = false, Destructive = false, Idempotent = true),
        Description("Request cooperative cancellation of a retained TimberFold run by runId in the bound session. No get_context or UI queue required. Preserves previous outputs and retained terminal results; poll timberfold_status for the final state.")]
    public Task<CallToolResult> TimberFoldCancel(string runId, CancellationToken cancellationToken = default) => Safe(async () =>
    {
        var result = await connection.TimberRun(Id(runId), true, cancellationToken);
        return Reply(result, !result.Succeeded);
    });
}
