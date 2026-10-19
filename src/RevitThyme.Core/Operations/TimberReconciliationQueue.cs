using System.Collections.Immutable;

namespace RevitThyme.Core;

public sealed partial class OperationDispatcher
{
    private sealed record ReconciliationPending(TimberNativeReconciliationRequest Request,
        TaskCompletionSource<TimberNativeReconciliationReceipt> Completion, CancellationToken Cancellation) : Work(Cancellation);

    public Task<TimberNativeReconciliationReceipt> ReconcileTimberAsync(TimberNativeReconciliationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.RunId == Guid.Empty || request.GenerationRunId == Guid.Empty || request.CurrentTarget is null ||
            request.PlanJson.IsDefaultOrEmpty || request.PlanJson.Length > 134217728 ||
            request.PreparedJson.IsDefaultOrEmpty || request.PreparedJson.Length > 33554432 || request.CommitReceipt is null ||
            request.CommitReceipt.CreatedElements.IsDefault || request.CommitReceipt.Files.IsDefault || request.CommitReceipt.Diagnostics.IsDefault)
            return Task.FromException<TimberNativeReconciliationReceipt>(new OperationException("invalid_request", "Reconciliation requires owned bounded generation evidence and an exact current target."));
        request = request with
        {
            PlanJson = ImmutableArray.Create(request.PlanJson.AsSpan()), PreparedJson = ImmutableArray.Create(request.PreparedJson.AsSpan()),
            CommitReceipt = request.CommitReceipt with
            {
                CreatedElements = ImmutableArray.Create(request.CommitReceipt.CreatedElements.AsSpan()),
                Files = ImmutableArray.Create(request.CommitReceipt.Files.AsSpan()),
                Diagnostics = ImmutableArray.Create(request.CommitReceipt.Diagnostics.AsSpan())
            }
        };
        var pending = new ReconciliationPending(request, new(TaskCreationOptions.RunContinuationsAsynchronously), cancellationToken);
        lock (gate)
        {
            if (stopped) return Task.FromException<TimberNativeReconciliationReceipt>(new OperationException("host_stopped", "RevitThyme is stopped."));
            if (cancellationToken.IsCancellationRequested) return Task.FromCanceled<TimberNativeReconciliationReceipt>(cancellationToken);
            if (queue.Count >= 32) return Task.FromException<TimberNativeReconciliationReceipt>(new OperationException("queue_full", "Native callback queue is full."));
            queue.Enqueue(pending);
        }
        try { Schedule?.Invoke(); }
        catch (Exception) { RejectPending("schedule_failed", "Revit could not schedule native reconciliation."); }
        return pending.Completion.Task;
    }

    private static void ExecuteReconciliation(IModel model, ReconciliationPending pending)
    {
        try
        {
            pending.Cancellation.ThrowIfCancellationRequested();
            var before = model.Capture();
            if (before.Target != pending.Request.CurrentTarget)
                throw new OperationException("stale_target", "Document, view or revision changed before native reconciliation.");
            if (model is not ITimberNativeReconciliationModel native)
                throw new OperationException("unsupported", "Native kit reconciliation is unavailable.");
            pending.Cancellation.ThrowIfCancellationRequested();
            var receipt = native.ReconcileTimberKit(pending.Request, pending.Cancellation);
            var after = model.Capture();
            if (after.Target != before.Target || after.IsModified != before.IsModified)
                throw new OperationException("source_changed", "Native source/context changed during reconciliation.");
            pending.Completion.TrySetResult(receipt);
        }
        catch (OperationCanceledException) { pending.Completion.TrySetCanceled(pending.Cancellation); }
        catch (Exception error)
        {
            pending.Completion.TrySetResult(new(TimberNativeReconciliationState.Unconfirmed, pending.Request.RunId,
                pending.Request.GenerationRunId, pending.Request.CurrentTarget, pending.Request.CurrentTarget, [],
                [new(error is OperationException operation ? operation.Code : "native_unconfirmed", TimberSeverity.Error, error.Message)], false, false));
        }
    }
}
