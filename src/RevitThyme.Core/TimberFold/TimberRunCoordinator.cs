using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json;

namespace RevitThyme.Core;

// One coordinator belongs to the host. Only the capture queue enters Revit's API context.
public sealed class TimberRunCoordinator : IAsyncDisposable
{
    private sealed class Entry(TimberRunStart request, TimberRunSummary summary)
    {
        internal readonly TimberRunStart Request = request;
        internal readonly CancellationTokenSource Cancellation = new();
        internal readonly Stopwatch Clock = Stopwatch.StartNew();
        internal TimberRunSummary Summary = summary;
        internal Task Work = Task.CompletedTask;
    }
    private readonly object gate = new();
    private readonly Dictionary<Guid, Entry> entries = [];
    private readonly TimberRunStore store;
    private readonly ITimberCaptureQueue native;
    private bool stopped;

    public TimberRunCoordinator(string root, ITimberCaptureQueue native)
    { store = new(root); this.native = native; }

    public TimberRunSummary Start(TimberRunStart request)
    {
        if (request.Id == Guid.Empty) throw new OperationException("invalid_id", "A non-empty run ID is required.");
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(request, Protocol.Json);
        if (bytes.Length > 16384) throw new OperationException("request_limit", "Run settings exceed 16 KiB.");
        request = JsonSerializer.Deserialize<TimberRunStart>(bytes, Protocol.Json)!;
        if (request.Profile is null || request.Target is null) throw new OperationException("invalid_request", "Target and fabrication profile are required.");
        lock (gate)
        {
            if (stopped) throw new OperationException("host_stopped", "RevitThyme is stopped.");
            if (entries.TryGetValue(request.Id, out var prior))
            {
                store.RequireSameRequest(request.Id, bytes);
                return prior.Summary;
            }
            if (!store.Exists(request.Id))
            {
                if (entries.Values.Count(e => !e.Summary.Terminal) >= 4 || entries.Count >= 4096)
                    throw new OperationException("queue_full", "TimberFold run capacity reached.");
                var diagnostics = request.Profile.Validate().ToImmutableArray();
                var summary = new TimberRunSummary(request.Id, request.Target,
                    diagnostics.Any(d => d.Severity == TimberSeverity.Error) ? TimberRunState.Failed : TimberRunState.Accepted,
                    "Accepted", store.DirectoryFor(request.Id), null, 0, 0, 0, diagnostics);
                store.Claim(request.Id, bytes, summary);
                var entry = new Entry(request, summary);
                entries.Add(request.Id, entry);
                if (!summary.Terminal) entry.Work = Task.Run(() => Execute(entry));
                return summary;
            }
        }
        store.RequireSameRequest(request.Id, bytes);
        return store.Read(request.Id);
    }

    public TimberRunSummary Status(Guid id)
    {
        lock (gate) if (entries.TryGetValue(id, out var entry)) return entry.Summary;
        return store.Read(id);
    }

    public TimberRunSummary Cancel(Guid id)
    {
        lock (gate)
        {
            if (entries.TryGetValue(id, out var entry))
            {
                if (!entry.Summary.Terminal) entry.Cancellation.Cancel();
                return entry.Summary;
            }
        }
        return store.Read(id, verifyPreview: false);
    }

    private async Task Execute(Entry entry)
    {
        try
        {
            Set(entry, TimberRunState.Capturing, "Capturing native source");
            var request = entry.Request;
            var captured = await native.CaptureTimberAsync(new(request.Id, request.Target, request.Profile, request.Scope),
                entry.Cancellation.Token).WaitAsync(entry.Cancellation.Token).ConfigureAwait(false);
            entry.Cancellation.Token.ThrowIfCancellationRequested();
            store.WriteNew(request.Id, "snapshot.json", captured.SnapshotJson.AsSpan());
            var preview = TimberPreviewPipeline.Compute(request, captured, store,
                (state, stage) => Set(entry, state, stage), entry.Cancellation.Token);
            lock (gate)
            {
                entry.Cancellation.Token.ThrowIfCancellationRequested();
                entry.Summary = entry.Summary with { SourceFingerprint = preview.Fingerprint,
                    PanelCount = preview.PanelCount, SheetCount = preview.SheetCount };
                if (preview.Manifest is not null) store.WriteNew(request.Id, "preview.json", preview.Manifest);
                Set(entry, preview.Manifest is null ? TimberRunState.Failed : TimberRunState.PreviewReady,
                    preview.Manifest is null ? "Preview blocked; inspect retained diagnostics" : "Preview ready; native import and physical checks remain",
                    preview.Diagnostics);
            }
        }
        catch (OperationCanceledException)
        { Set(entry, TimberRunState.Cancelled, "Cancelled"); }
        catch (Exception error)
        {
            Set(entry, TimberRunState.Failed, "Failed",
                [new(error is OperationException operation ? operation.Code : "run_failed", TimberSeverity.Error, error.Message)]);
        }
    }

    private void Set(Entry entry, TimberRunState state, string stage, ImmutableArray<TimberDiagnostic> diagnostics = default)
    {
        lock (gate)
        {
            if (entry.Summary.State == TimberRunState.Unconfirmed) return;
            var next = entry.Summary with { State = state, Stage = stage, ElapsedMilliseconds = entry.Clock.Elapsed.TotalMilliseconds,
                Diagnostics = diagnostics.IsDefault ? entry.Summary.Diagnostics : diagnostics };
            try
            {
                if (next.Terminal) store.WriteNew(next.RunId, "diagnostics.json", JsonSerializer.SerializeToUtf8Bytes(next.Diagnostics, Protocol.Json));
                next = Bounded(next);
                store.WriteStatus(next);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                next = next with { State = TimberRunState.Unconfirmed, Stage = "Run evidence could not be retained",
                    Diagnostics = [new("run_storage", TimberSeverity.Error, error.Message)] };
            }
            entry.Summary = next;
            if (next.State == TimberRunState.Unconfirmed) entry.Cancellation.Cancel();
        }
    }

    private static TimberRunSummary Bounded(TimberRunSummary summary)
    {
        var bounded = summary.Diagnostics.Take(24).Select(d => d with { Code = Clip(d.Code, 48)!, Message = Clip(d.Message, 128)!,
            SourceId = Clip(d.SourceId, 48), GeometryId = Clip(d.GeometryId, 48) }).ToImmutableArray();
        return summary with { Diagnostics = bounded, DiagnosticsTruncated = summary.Diagnostics.Length > 24 || !bounded.SequenceEqual(summary.Diagnostics),
            SourceFingerprint = Clip(summary.SourceFingerprint, 128) };
        static string? Clip(string? value, int maximum) => value?.Length > maximum ? value[..maximum] : value;
    }

    public void Stop()
    { lock (gate) { stopped = true; foreach (var entry in entries.Values.Where(e => !e.Summary.Terminal)) entry.Cancellation.Cancel(); } }

    public async ValueTask DisposeAsync()
    {
        Stop();
        Task[] work;
        lock (gate) work = entries.Values.Select(e => e.Work).ToArray();
        await Task.WhenAll(work).ConfigureAwait(false);
        lock (gate) foreach (var entry in entries.Values) entry.Cancellation.Dispose();
    }
}
