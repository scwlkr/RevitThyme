namespace RevitThyme.Core;

// Queue contains data only. The host drains it from ExternalEvent.Execute on Revit's thread.
public sealed class OperationDispatcher : IOperationClient
{
    private sealed record Pending(OperationRequest Request, TaskCompletionSource<OperationResult> Completion);
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
            if (cancellationToken.IsCancellationRequested)
                return Task.FromResult(OperationResult.Reject(request, "cancelled", "Request cancelled before queueing."));
            if (history.TryGetValue(request.Id, out var previous))
                return previous.Request == request ? previous.Completion.Task :
                    Task.FromResult(OperationResult.Reject(request, "id_conflict", "Request ID was already used with different arguments."));
            // Retain outcomes for this entire session; never evict an edit and execute it twice.
            if (queue.Count >= 32 || history.Count >= 4096)
                return Task.FromResult(OperationResult.Reject(request, "queue_full", "Session request capacity reached."));
            pending = new(request, new(TaskCreationOptions.RunContinuationsAsynchronously));
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
        if (pending is not null) pending.Completion.TrySetResult(executor.Execute(model, pending.Request));
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
