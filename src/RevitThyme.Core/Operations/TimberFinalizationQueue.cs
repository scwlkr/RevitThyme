using System.Collections.Immutable;

namespace RevitThyme.Core;

public sealed partial class OperationDispatcher
{
    private sealed record FinalizationPending(TimberNativeKitRequest Request,
        TaskCompletionSource<TimberNativeKitReceipt> Completion, CancellationToken Cancellation) : Work(Cancellation);

    public Task<TimberNativeKitReceipt> FinalizeTimberAsync(TimberNativeKitRequest request, CancellationToken cancellationToken = default)
    {
        if (request.RunId == Guid.Empty || request.Target is null || request.PlanJson.IsDefault)
            return Task.FromException<TimberNativeKitReceipt>(new OperationException("invalid_request", "Run ID, exact target and encoded plan are required."));
        // Own the encoded bytes even if a producer retained their backing array.
        request = request with { PlanJson = ImmutableArray.Create(request.PlanJson.AsSpan()) };
        var pending = new FinalizationPending(request, new(TaskCreationOptions.RunContinuationsAsynchronously), cancellationToken);
        lock (gate)
        {
            if (stopped) return Task.FromResult(FinalizationRejected(request, "host_stopped", "RevitThyme is stopped."));
            if (cancellationToken.IsCancellationRequested) return Task.FromResult(FinalizationRejected(request, "cancelled", "Finalization cancelled before queueing."));
            if (queue.Count >= 32) return Task.FromResult(FinalizationRejected(request, "queue_full", "Native callback queue is full."));
            queue.Enqueue(pending);
        }
        try { Schedule?.Invoke(); }
        catch (Exception) { RejectPending("schedule_failed", "Revit could not schedule native finalization."); }
        return pending.Completion.Task;
    }

    private static void ExecuteFinalization(IModel model, FinalizationPending pending)
    {
        if (pending.Cancellation.IsCancellationRequested)
        {
            pending.Completion.TrySetResult(FinalizationRejected(pending.Request, "cancelled", "Finalization cancelled before native execution."));
            return;
        }
        Target actualTarget = pending.Request.Target;
        // Do not race this receipt against token cancellation: the native callback may commit.
        try
        {
            var before = model.Capture(); actualTarget = before.Target;
            if (before.Target != pending.Request.Target)
            {
                pending.Completion.TrySetResult(FinalizationRejected(pending.Request, "stale_target",
                    "Document, view or revision changed before native finalization.", actualTarget));
                return;
            }
            if (!before.CanModify)
            {
                pending.Completion.TrySetResult(FinalizationRejected(pending.Request, "write_blocked",
                    before.WriteBlock ?? "Native model edits are unavailable.", actualTarget));
                return;
            }
            if (model is not ITimberNativeKitModel native)
            {
                pending.Completion.TrySetResult(FinalizationRejected(pending.Request, "unsupported",
                    "Native TimberFold finalization is unavailable.", actualTarget));
                return;
            }
            if (pending.Cancellation.IsCancellationRequested)
            {
                pending.Completion.TrySetResult(FinalizationRejected(pending.Request, "cancelled",
                    "Finalization cancelled before native mutation.", actualTarget));
                return;
            }
            pending.Completion.TrySetResult(native.FinalizeTimberKit(pending.Request, pending.Cancellation));
        }
        catch (Exception error)
        {
            pending.Completion.TrySetResult(new(TimberNativeKitState.Unconfirmed, pending.Request.RunId,
                actualTarget, [], [], [new("native_unconfirmed", TimberSeverity.Error, error.Message)], false, false));
        }
    }

    private static TimberNativeKitReceipt FinalizationRejected(TimberNativeKitRequest request, string code, string message,
        Target? actualTarget = null) => new(TimberNativeKitState.Rejected, request.RunId, actualTarget ?? request.Target,
            [], [], [new(code, TimberSeverity.Error, message)], true, false);
}
