using System.Text.Json;

namespace RevitThyme.Core;

public sealed partial class OperationDispatcher
{
    private static bool IsTimberRun(OperationKind operation) => operation is OperationKind.PreviewTimberFold
        or OperationKind.GetTimberFoldRun or OperationKind.CancelTimberFoldRun or OperationKind.GenerateTimberFold or OperationKind.VerifyTimberFoldRun
        or OperationKind.ReconcileTimberFold;

    private OperationResult ExecuteTimberRun(OperationRequest request)
    {
        try
        {
            if (timberRuns is null) return OperationResult.Reject(request, "unsupported", "TimberFold run storage is unavailable in this host.");
            bool preview = request.Operation == OperationKind.PreviewTimberFold;
            bool generation = request.Operation == OperationKind.GenerateTimberFold;
            bool reconciliation = request.Operation == OperationKind.ReconcileTimberFold;
            if (request.ExpectedName is not null || request.Name is not null || request.Query is not null || request.ElementIds is not null ||
                request.ParameterOffset != 0 || request.Edits is not null || request.ExpectedRange is not null || request.Range is not null ||
                request.RoomOptions is not null || request.RoomPreview is not null || request.Code is not null ||
                (preview ? request.OperationId is not null : generation || reconciliation ? request.TimberFold is not null : request.Target is not null || request.TimberFold is not null))
                return OperationResult.Reject(request, "invalid_request", "Preview accepts target/profile/scope; generation/reconciliation accept target and parent operationId; run controls accept only operationId.");
            TimberRunSummary summary;
            if (preview)
            {
                if (request.Target is null) return OperationResult.Reject(request, "target_required", "Capture the source document and view first.");
                summary = timberRuns.Start(new(request.Id, request.Target, request.TimberFold?.Profile ?? new(), request.TimberFold?.Scope));
            }
            else if (generation || reconciliation)
            {
                if (request.Target is null) return OperationResult.Reject(request, "target_required", "Capture the exact source target first.");
                if (request.OperationId is not { } parent || parent == Guid.Empty)
                    return OperationResult.Reject(request, "invalid_id", "Generation/reconciliation require a non-empty parent operationId.");
                summary = reconciliation ? timberRuns.StartReconciliation(new(request.Id, parent, request.Target)) :
                    timberRuns.StartGeneration(new(request.Id, request.Target, parent));
            }
            else
            {
                if (request.OperationId is not { } id || id == Guid.Empty)
                    return OperationResult.Reject(request, "invalid_id", "A non-empty run operationId is required.");
                summary = request.Operation switch
                {
                    OperationKind.CancelTimberFoldRun => timberRuns.Cancel(id),
                    OperationKind.VerifyTimberFoldRun => timberRuns.Verify(id),
                    _ => timberRuns.Status(id)
                };
            }
            var outcome = summary.State == TimberRunState.Unconfirmed ? Outcome.Unconfirmed :
                summary.State == TimberRunState.Failed ? Outcome.Rejected : Outcome.Inspected;
            return new(request.Id, outcome, summary.State.ToString(), summary.Stage, TimberRun: summary);
        }
        catch (Exception error)
        {
            string message = error.Message.Length > 240 ? error.Message[..240] : error.Message;
            return OperationResult.Reject(request, error is OperationException operation ? operation.Code : "run_unconfirmed", message);
        }
    }

    private Task<OperationResult> RetainTimberRun(OperationRequest request)
    {
        // Called under the dispatch gate. Status does not consume retained write capacity.
        if (request.Operation is OperationKind.GetTimberFoldRun or OperationKind.VerifyTimberFoldRun) return Task.Run(() => ExecuteTimberRun(request));
        if (history.Count >= 4096) return Task.FromResult(OperationResult.Reject(request, "queue_full", "Session request capacity reached."));
        var owned = JsonSerializer.Deserialize<OperationRequest>(JsonSerializer.SerializeToUtf8Bytes(request, Protocol.Json), Protocol.Json)!;
        var completed = new Pending(owned, new(TaskCreationOptions.RunContinuationsAsynchronously), CancellationToken.None);
        history.Add(request.Id, completed);
        // Recovery hashes can be large. Never hold the native dispatch gate while
        // reading them, so unrelated cancellation and host shutdown remain responsive.
        _ = Task.Run(() => completed.Completion.SetResult(ExecuteTimberRun(owned)));
        return completed.Completion.Task;
    }
}
