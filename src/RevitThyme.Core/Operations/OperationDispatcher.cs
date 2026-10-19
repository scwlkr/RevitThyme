namespace RevitThyme.Core;

// Queue contains data only. The host drains it from ExternalEvent.Execute on Revit's thread.
public sealed class OperationDispatcher : IOperationClient
{
    private sealed record Pending(OperationRequest Request, TaskCompletionSource<OperationResult> Completion, CancellationToken Cancellation);
    private readonly object gate = new();
    private readonly Queue<Pending> queue = new();
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
            if (request.Operation == OperationKind.GetOperation)
            {
                if (request.OperationId is not { } id || request.Target is not null || request.Name is not null || request.ExpectedName is not null
                    || request.Query is not null || request.ElementIds is not null || request.Edits is not null || request.ParameterOffset != 0
                    || request.ExpectedRange is not null || request.Range is not null || request.RoomOptions is not null || request.RoomPreview is not null || request.Code is not null || request.TimberFold is not null)
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
        Pending? pending;
        lock (gate) { pending = queue.Count == 0 ? null : queue.Dequeue(); }
        if (pending is not null) pending.Completion.TrySetResult(pending.Cancellation.IsCancellationRequested
            ? OperationResult.Reject(pending.Request, "cancelled", "Request cancelled before Revit execution.")
            : executor.Execute(model, pending.Request));
    }
    public void RejectPending(string code, string message)
    {
        lock (gate)
            while (queue.TryDequeue(out var pending))
                pending.Completion.TrySetResult(OperationResult.Reject(pending.Request, code, message));
    }
    public void Stop()
    {
        lock (gate) { stopped = true; RejectPending("host_stopped", "Revit closed before execution."); }
    }
}
