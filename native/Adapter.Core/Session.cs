namespace RevitThyme.Native;
public sealed class Session
{
    private sealed class Entry(Request request, string connection, DateTime created)
    {
        public Request Request = request;
        public string Connection = connection;
        public DateTime Created = created;
        public Reply Reply = Reply.Result(request, Status.Queued, "queued", "Waiting for Revit API context.");
    }
    private readonly object gate = new();
    private readonly Dictionary<string, Entry> entries = [];
    private readonly Queue<string> queue = new();
    private readonly HashSet<string> connections = [];
    private Facts? facts;
    public uint Revision { get; private set; } = 1;
    public void Connect(string connection) { lock (gate) connections.Add(connection); }
    public void Invalidate() { lock (gate) { Revision = checked(Revision + 1); facts = null; } }
    public void Disconnect(string connection)
    {
        lock (gate)
        {
            connections.Remove(connection); facts = null;
            foreach (var e in entries.Values.Where(e => e.Connection == connection && e.Reply.Status == Status.Queued))
                e.Reply = Reply.Result(e.Request, Status.Cancelled, "disconnected", "Client disconnected before execution.", true);
        }
    }
    public Reply Submit(Request r, string connection)
    {
        Wire.Validate(r);
        lock (gate)
        {
            if (!connections.Contains(connection)) return Reply.Result(r, Status.Rejected, "disconnected", "Connection expired.", true);
            if (r.Operation is Operation.Outcome or Operation.Cancel)
            {
                if (!entries.TryGetValue(r.OutcomeId, out var prior) || prior.Request.Target != r.Target)
                    return Reply.Result(r, Status.OutcomeUnconfirmed, "unknown_outcome", "No retained outcome for this target. Refresh; do not automatically retry.", true);
                if (prior.Reply.Status == Status.Queued && DateTime.UtcNow - prior.Created > TimeSpan.FromSeconds(30))
                    prior.Reply = Reply.Result(prior.Request, Status.Cancelled, "queue_expired", "Queued operation expired before execution.", true);
                if (r.Operation == Operation.Cancel && prior.Reply.Status == Status.Queued)
                    prior.Reply = Reply.Result(prior.Request, Status.Cancelled, "cancelled", "Removed before any API execution.", true);
                return prior.Reply;
            }
            if (entries.TryGetValue(r.RequestId, out var same))
                return same.Request == r ? same.Reply : Reply.Result(r, Status.Rejected, "request_id_conflict", "Request ID already has another payload.");
            if (r.Operation == Operation.Geometry)
            {
                var f = Resolve(r);
                return Reply.Result(r, Status.Captured, "geometry", "Cached native triangles in internal feet.") with
                { Triangles = f.Triangles.Skip(r.ChunkIndex * 128).Take(128).ToArray() };
            }
            foreach (var key in entries.Where(p => Terminal(p.Value.Reply.Status) && DateTime.UtcNow - p.Value.Created > TimeSpan.FromMinutes(10)).Select(p => p.Key).ToArray())
                entries.Remove(key);
            if (entries.Count >= 128 || queue.Count >= 16)
                return Reply.Result(r, Status.Rejected, "queue_full", "Bounded native queue/outcome store is full. Wait before a new request.");
            entries.Add(r.RequestId, new(r, connection, DateTime.UtcNow)); queue.Enqueue(r.RequestId);
            return entries[r.RequestId].Reply;
        }
    }
    private Facts Resolve(Request r)
    {
        if (facts is null || r.Target != facts.Capture.Target || r.SnapshotId != facts.Capture.SnapshotId
            || DateTime.UtcNow - facts.Created >= TimeSpan.FromMinutes(10)) throw new InvalidDataException("Snapshot expired. Refresh.");
        return facts;
    }
    public bool HasQueued { get { lock (gate) return queue.Count > 0; } }
    public void RejectQueued(string code)
    {
        lock (gate)
            foreach (var e in entries.Values.Where(e => e.Reply.Status == Status.Queued))
                e.Reply = Reply.Result(e.Request, Status.Rejected, code, "ExternalEvent could not be scheduled. Refresh.", true);
    }
    // Only Execute/Idling on the host thread may call this method. No lock is held during native work.
    public void ExecuteOne(IModel model)
    {
        Entry? e = null; Facts? captured = null;
        lock (gate)
        {
            while (queue.TryDequeue(out var id))
            {
                if (!entries.TryGetValue(id, out var next)) continue;
                if (next.Reply.Status != Status.Queued) continue;
                if (!connections.Contains(next.Connection) || DateTime.UtcNow - next.Created > TimeSpan.FromSeconds(30))
                { next.Reply = Reply.Result(next.Request, Status.Cancelled, "queue_expired", "Disconnected or timed-out queued request.", true); continue; }
                e = next; e.Reply = Reply.Result(e.Request, Status.Executing, "executing", "Native operation executing; cancellation cannot promise rollback.");
                captured = facts; break;
            }
        }
        if (e is null) return;
        Reply result;
        try
        {
            var r = e.Request;
            if (r.Operation == Operation.Capture)
            {
                var f = model.Capture();
                if (f.Triangles.Length > 50_000 || f.Capture.TriangleCount != f.Triangles.Length) throw new InvalidDataException("Capture limit.");
                lock (gate) { if (connections.Contains(e.Connection)) facts = f; }
                result = Reply.Result(r, Status.Captured, "captured", "Native snapshot captured.") with { Capture = f.Capture };
            }
            else
            {
                if (captured is null || captured.Capture.Target != r.Target || captured.Capture.SnapshotId != r.SnapshotId
                    || DateTime.UtcNow - captured.Created > TimeSpan.FromMinutes(10) || model.CurrentTarget() != r.Target)
                    throw new Rejection("stale_snapshot", "Target, session, document, view or revision changed. Refresh.");
                if (model.Read(captured) != captured.Original) throw new Rejection("stale_original", "Native range changed after capture. Refresh.");
                result = r.Operation == Operation.Apply ? Apply.Execute(model, captured, r)
                    : Reply.Result(r, Status.Validated, "validated", "Native validity checked; no transaction opened.", values: model.Validate(captured, r.Range!));
                if (r.Operation == Operation.Apply && result.Status != Status.Rejected) lock (gate) facts = null;
            }
        }
        catch (Rejection ex) { result = Reply.Result(e.Request, Status.Rejected, ex.Code, ex.Message, true); }
        catch { result = Reply.Result(e.Request, Status.Rejected, "native_rejected", "Capture, target or native validation failed. Refresh in a supported project plan.", true); }
        lock (gate) e.Reply = result;
    }
    private static bool Terminal(Status s) => s is not (Status.Queued or Status.Executing);
}
