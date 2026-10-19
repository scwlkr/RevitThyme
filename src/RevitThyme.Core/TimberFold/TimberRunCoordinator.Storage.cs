namespace RevitThyme.Core;

public sealed partial class TimberRunCoordinator
{
    private TimberRunSummary StartOwned(TimberRunStart request, TimberRunSummary summary, byte[] bytes,
        TimberReconciliationStart? reconciliation = null)
    {
        Entry? entry;
        lock (gate)
        {
            if (stopped) throw new OperationException("host_stopped", "RevitThyme is stopped.");
            entries.TryGetValue(request.Id, out entry);
        }
        if (entry is not null) return Initialize(entry, bytes);
        // Retained file IO never holds the coordinator-wide gate.
        if (store.Exists(request.Id))
        {
            if (reconciliation is null) store.RequireSameRequest(request.Id, bytes);
            else store.RequireSameReconciliation(request.Id, bytes);
            var retained = store.Read(request.Id);
            lock (gate) entries.TryGetValue(request.Id, out entry);
            return entry is null ? retained : Initialize(entry, bytes);
        }
        lock (gate)
        {
            if (stopped) throw new OperationException("host_stopped", "RevitThyme is stopped.");
            if (!entries.TryGetValue(request.Id, out entry))
            {
                if (entries.Values.Count(e => !e.Summary.Terminal) >= 4 || entries.Count >= 4096)
                    throw new OperationException("queue_full", "TimberFold run capacity reached.");
                entry = new(request, summary, bytes) { Reconciliation = reconciliation };
                entries.Add(request.Id, entry);
            }
        }
        return Initialize(entry, bytes);
    }

    private TimberRunSummary Initialize(Entry entry, byte[] bytes)
    {
        lock (entry.PublicationGate)
        {
            if (!entry.CanonicalRequest.SequenceEqual(bytes)) throw new OperationException("id_conflict", "Run ID was already used with different arguments.");
            if (entry.InitializationError is { } priorError) throw new OperationException("run_storage", "Run initialization failed: " + priorError.Message);
            if (entry.Initialized) { lock (gate) return entry.Summary; }
            try
            {
                string name = entry.Reconciliation is null ? "request.json" : "reconciliation-request.json";
                store.Claim(entry.Request.Id, bytes, entry.Summary, name);
                entry.Initialized = true;
                if (entry.Summary.Terminal) entry.Lifetime.TrySetResult();
                else _ = Task.Run(() => RunOwned(entry));
                lock (gate) return entry.Summary;
            }
            catch (Exception error)
            {
                entry.InitializationError = error;
                lock (gate)
                {
                    entries.Remove(entry.Request.Id);
                    entry.Summary = entry.Summary with { State = TimberRunState.Unconfirmed, Stage = "Run initialization failed" };
                }
                entry.Lifetime.TrySetResult();
                entry.Cancellation.Dispose();
                throw;
            }
        }
    }

    private async Task RunOwned(Entry entry)
    {
        try
        {
            if (entry.Reconciliation is not null) await ExecuteReconciliation(entry).ConfigureAwait(false);
            else if (entry.Request.PreviewRunId is not null) await ExecuteGeneration(entry).ConfigureAwait(false);
            else await Execute(entry).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            lock (gate) entry.Summary = Bounded(entry.Summary with { State = TimberRunState.Unconfirmed,
                Stage = "Run stopped without retained completion", Diagnostics = [new("run_storage", TimberSeverity.Error, error.Message)] });
        }
        finally { entry.Lifetime.TrySetResult(); }
    }
}
