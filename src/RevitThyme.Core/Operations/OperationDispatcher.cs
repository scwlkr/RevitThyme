namespace RevitThyme.Core;

// Queue contains data only. The host drains it from ExternalEvent.Execute on Revit's thread.
public sealed partial class OperationDispatcher : IOperationClient, ITimberCaptureQueue, ITimberFinalizationQueue, ITimberReconciliationQueue, IAsyncDisposable
{
    private readonly TimberRunCoordinator? timberRuns;
    public OperationDispatcher(string? timberRunDirectory = null)
    { if (timberRunDirectory is not null) timberRuns = new(timberRunDirectory, this); }
    public ValueTask DisposeAsync() { Stop(); return timberRuns?.DisposeAsync() ?? ValueTask.CompletedTask; }
    public Task<TimberCapturedSnapshot> CaptureTimberAsync(TimberCaptureRequest request, CancellationToken cancellationToken = default)
    {
        if (request.RunId == Guid.Empty) return Task.FromException<TimberCapturedSnapshot>(new OperationException("invalid_id", "A run ID is required."));
        // Capture scope by value before another caller can mutate its arrays.
        var owned = System.Text.Json.JsonSerializer.Deserialize<TimberCaptureRequest>(
            System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(request, Protocol.Json), Protocol.Json)!;
        var pending = new CapturePending(owned, new(TaskCreationOptions.RunContinuationsAsynchronously), cancellationToken);
        lock (gate)
        {
            if (stopped) return Task.FromException<TimberCapturedSnapshot>(new OperationException("host_stopped", "RevitThyme is stopped."));
            if (cancellationToken.IsCancellationRequested) return Task.FromCanceled<TimberCapturedSnapshot>(cancellationToken);
            if (queue.Count >= 32) return Task.FromException<TimberCapturedSnapshot>(new OperationException("queue_full", "Native capture queue is full."));
            queue.Enqueue(pending);
        }
        try { Schedule?.Invoke(); }
        catch (Exception) { RejectPending("schedule_failed", "Revit could not schedule extraction."); }
        return pending.Completion.Task;
    }
    private abstract record Work(CancellationToken Cancellation);
    private sealed record Pending(OperationRequest Request, TaskCompletionSource<OperationResult> Completion, CancellationToken Cancellation) : Work(Cancellation);
    private sealed record CapturePending(TimberCaptureRequest Request, TaskCompletionSource<TimberCapturedSnapshot> Completion, CancellationToken Cancellation) : Work(Cancellation);
    private readonly object gate = new();
    private readonly Queue<Work> queue = new();
    private readonly Dictionary<Guid, Pending> history = [];
    private readonly OperationExecutor executor = new();
    private bool stopped;
    public Action? Schedule { get; set; }
    public bool HasPending { get { lock (gate) return queue.Count > 0; } }
    public Task<OperationResult> ExecuteAsync(OperationRequest request, CancellationToken cancellationToken = default)
    {
        Pending pending;
        lock (gate)
        {
            if (stopped) return Task.FromResult(OperationResult.Reject(request, "host_stopped", "RevitThyme is stopped."));
            if (request.Id == Guid.Empty) return Task.FromResult(OperationResult.Reject(request, "invalid_id", "A non-empty request ID is required."));
            if (request.DocumentLifecycle is not null && !DocumentLifecycleExecutor.Handles(request.Operation))
                return Task.FromResult(OperationResult.Reject(request, "invalid_request", "Lifecycle arguments are accepted only by lifecycle operations."));
            if (request.ImageExport is not null && request.Operation != OperationKind.ExportViewImage)
                return Task.FromResult(OperationResult.Reject(request, "invalid_request", "Image export arguments are accepted only by ExportViewImage."));
            if (request.Operation == OperationKind.GetOperation)
            {
                if (request.OperationId is not { } id || request.Target is not null || request.Name is not null || request.ExpectedName is not null
                    || request.Query is not null || request.ElementIds is not null || request.Edits is not null || request.ParameterOffset != 0
                    || request.ExpectedRange is not null || request.Range is not null || request.RoomOptions is not null || request.RoomPreview is not null || request.Code is not null || request.TimberFold is not null || request.ImageExport is not null)
                    return Task.FromResult(OperationResult.Reject(request, "invalid_request", "Only operationId is accepted."));
                return Task.FromResult(history.TryGetValue(id, out var retained)
                    ? retained.Completion.Task.IsCompletedSuccessfully ? retained.Completion.Task.Result
                        : OperationResult.Reject(request, "pending", "Request is queued or running. Poll its ID; do not submit a new edit.")
                    : OperationResult.Reject(request, "not_found", "Request ID is not retained in this session."));
            }
            if (cancellationToken.IsCancellationRequested)
                return Task.FromResult(OperationResult.Reject(request, "cancelled", "Request cancelled before queueing."));
            if (history.TryGetValue(request.Id, out var previous))
                return previous.Request.EquivalentTo(request) ? previous.Completion.Task :
                    Task.FromResult(OperationResult.Reject(request, "id_conflict", "Request ID was already used with different arguments."));
            if (IsTimberRun(request.Operation)) return RetainTimberRun(request);
            // Retain outcomes for this entire session; never evict an edit and execute it twice.
            if (queue.Count >= 32 || history.Count >= 4096)
                return Task.FromResult(OperationResult.Reject(request, "queue_full", "Session request capacity reached."));
            pending = new(request, new(TaskCreationOptions.RunContinuationsAsynchronously), cancellationToken);
            history.Add(request.Id, pending); queue.Enqueue(pending);
        }
        try { Schedule?.Invoke(); }
        catch (Exception) { RejectPending("schedule_failed", "Revit could not schedule the request."); }
        return pending.Completion.Task;
    }
    public void ExecuteNext(IModel model)
    {
        Work? pending;
        lock (gate) { pending = queue.Count == 0 ? null : queue.Dequeue(); }
        if (pending is Pending operation) operation.Completion.TrySetResult(operation.Cancellation.IsCancellationRequested
            ? OperationResult.Reject(operation.Request, "cancelled", "Request cancelled before Revit execution.")
            : executor.Execute(model, operation.Request));
        if (pending is CapturePending capture)
        {
            try { capture.Completion.TrySetResult(TimberCaptureExecutor.Execute(model, capture.Request, capture.Cancellation)); }
            catch (OperationCanceledException) { capture.Completion.TrySetCanceled(capture.Cancellation); }
            catch (Exception error) { capture.Completion.TrySetException(error); }
        }
        if (pending is FinalizationPending finalization) ExecuteFinalization(model, finalization);
        if (pending is ReconciliationPending reconciliation) ExecuteReconciliation(model, reconciliation);
    }
    public void RejectPending(string code, string message)
    {
        lock (gate)
            while (queue.TryDequeue(out var pending))
                if (pending is Pending operation) operation.Completion.TrySetResult(OperationResult.Reject(operation.Request, code, message));
                else if (pending is CapturePending capture) capture.Completion.TrySetException(new OperationException(code, message));
                else if (pending is FinalizationPending finalization)
                    finalization.Completion.TrySetResult(FinalizationRejected(finalization.Request, code, message));
                else if (pending is ReconciliationPending reconciliation)
                    reconciliation.Completion.TrySetException(new OperationException(code, message));
    }
    public void Stop()
    {
        lock (gate) { stopped = true; timberRuns?.Stop(); RejectPending("host_stopped", "Revit closed before execution."); }
    }
}
