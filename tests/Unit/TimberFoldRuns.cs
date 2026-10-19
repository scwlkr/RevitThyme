using RevitThyme.Core;

namespace RevitThyme.Unit;

internal static class TimberFoldRuns
{
    public static async Task Run(Action<string, bool> check)
    {
        var dispatcher = new OperationDispatcher();
        var model = new NativeBoundary();
        var capture = dispatcher.CaptureTimberAsync(new(Guid.NewGuid(), model.Base.Target, new()));
        check("TimberFold capture waits for the native callback without touching the model", !capture.IsCompleted && model.Extractions == 0);
        if (capture.IsFaulted) return;
        model.ExtractionDelayMilliseconds = 25;
        dispatcher.ExecuteNext(model);
        var captured = await capture;
        check("native extraction timing includes work performed inside the callback", captured.NativeMilliseconds is >= 15);
        model.Source.Faces[0].RingsFeet[0][0] = new(999, 999);
        check("native callback hands owned immutable bytes to background work", model.Extractions == 1 &&
            captured.Context.Target == model.Base.Target && captured.Decode().Faces[0].RingsFeet[0][0] == new TimberPoint2(0, 0) && model.Base.Transactions == 0);
        await QueuedCancellation(check);
        await NativeCancellation(check);
        await CompletedPreview(check);
        await BoundedFailure(check);
        await SharedOperations(check);
    }

    private static async Task SharedOperations(Action<string, bool> check)
    {
        string root = Path.Combine(Path.GetTempPath(), "RevitThyme-TimberOps-" + Guid.NewGuid());
        await using (var dispatcher = new OperationDispatcher(root))
        {
            var model = new NativeBoundary();
            var request = new OperationRequest(Guid.NewGuid(), OperationKind.PreviewTimberFold, model.Base.Target, TimberFold: new());
            var task = dispatcher.ExecuteAsync(request);
            await task.WaitAsync(TimeSpan.FromSeconds(3));
            check("shared preview operation returns its handle without a native callback", task.IsCompletedSuccessfully &&
                task.Result.TimberRun is { State: TimberRunState.Accepted } && model.Extractions == 0);
            if (task.IsCompletedSuccessfully && task.Result.TimberRun is { } run)
            {
                var status = await dispatcher.ExecuteAsync(new(Guid.NewGuid(), OperationKind.GetTimberFoldRun, OperationId: run.RunId));
                var cancel = await dispatcher.ExecuteAsync(new(Guid.NewGuid(), OperationKind.CancelTimberFoldRun, OperationId: run.RunId));
                check("shared status and cancellation bypass the native callback queue", status.TimberRun?.RunId == run.RunId &&
                    cancel.TimberRun?.RunId == run.RunId && model.Extractions == 0);
            }
            dispatcher.Stop();
        }
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }

    private static async Task NativeCancellation(Action<string, bool> check)
    {
        await using var dispatcher = new OperationDispatcher(); var model = new NativeBoundary();
        using var cancelled = new CancellationTokenSource(); bool reached = false;
        model.DuringExtraction = token => { reached = token.CanBeCanceled; cancelled.Cancel(); token.ThrowIfCancellationRequested(); };
        var pending = dispatcher.CaptureTimberAsync(new(Guid.NewGuid(), model.Base.Target, new()), cancelled.Token);
        dispatcher.ExecuteNext(model); bool stopped = false;
        try { await pending; } catch (OperationCanceledException) { stopped = true; }
        check("capture carries cooperative cancellation into the native extraction boundary", reached && stopped && model.Extractions == 0 && model.Base.Transactions == 0);
    }

    private static async Task BoundedFailure(Action<string, bool> check)
    {
        string root = Path.Combine(Path.GetTempPath(), "RevitThyme-TimberRuns-" + Guid.NewGuid());
        var dispatcher = new OperationDispatcher(); var model = new NativeBoundary();
        model.Source = model.Source with { Diagnostics = Enumerable.Range(0, 200).Select(i =>
            new TimberDiagnostic("source_problem", TimberSeverity.Error, new string('x', 1000), "wall", "face")).ToArray() };
        await using (var runs = new TimberRunCoordinator(root, dispatcher))
        {
            var accepted = runs.Start(new(Guid.NewGuid(), model.Base.Target, new()));
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!runs.Status(accepted.RunId).Terminal && DateTime.UtcNow < deadline)
            { if (dispatcher.HasPending) dispatcher.ExecuteNext(model); await Task.Delay(5); }
            var result = runs.Status(accepted.RunId);
            check("blocked previews retain full diagnostics while returning a bounded protocol summary",
                result.State == TimberRunState.Failed && result.DiagnosticsTruncated && result.Diagnostics.Length <= 24 &&
                System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(result, Protocol.Json).Length < Protocol.MaxBytes &&
                File.Exists(Path.Combine(result.Directory, "diagnostics.json")) &&
                !File.Exists(Path.Combine(result.Directory, "preview.json")));
        }
        dispatcher.Stop();
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }

    private static async Task CompletedPreview(Action<string, bool> check)
    {
        string root = Path.Combine(Path.GetTempPath(), "RevitThyme-TimberRuns-" + Guid.NewGuid());
        var dispatcher = new OperationDispatcher(); var model = new NativeBoundary();
        var request = new TimberRunStart(Guid.NewGuid(), model.Base.Target, new());
        string? completedDirectory = null;
        await using (var runs = new TimberRunCoordinator(root, dispatcher))
        {
            var accepted = runs.Start(request);
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!runs.Status(accepted.RunId).Terminal && DateTime.UtcNow < deadline)
            { if (dispatcher.HasPending) dispatcher.ExecuteNext(model); await Task.Delay(5); }
            var result = runs.Status(accepted.RunId);
            if (result.State != TimberRunState.PreviewReady) Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(result, Protocol.Json));
            check("preview runs capture once then produce verified physical-inch cutting files without model edits",
                result.State == TimberRunState.PreviewReady && result.PanelCount == 1 && result.SheetCount == 1 &&
                result.SourceFingerprint == "native-source" && !result.ManufacturingReady && result.NativeImportOutstanding &&
                result.PhysicalChecksOutstanding && model.Extractions == 1 && model.Base.Transactions == 0 &&
                File.Exists(Path.Combine(result.Directory, "preview.json")) &&
                File.Exists(Path.Combine(result.Directory, "cutting", "CUT-001.dxf")));
            check("completed preview retries return the retained result without a second extraction",
                runs.Start(request).State == TimberRunState.PreviewReady && model.Extractions == 1);
            string producerPath = Path.Combine(result.Directory, "producer.json");
            bool producerVerified = false;
            if (File.Exists(producerPath))
            {
                using var producer = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(producerPath));
                var record = producer.RootElement;
                producerVerified = record.GetProperty("schema").GetInt32() == 1 && record.GetProperty("protocol").GetInt32() == Protocol.Version &&
                    !string.IsNullOrWhiteSpace(record.GetProperty("productVersion").GetString()) &&
                    record.GetProperty("coreSha256").GetString() == Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
                        File.ReadAllBytes(typeof(TimberRunCoordinator).Assembly.Location))) &&
                    DateTimeOffset.TryParse(record.GetProperty("createdUtc").GetString(), out _);
            }
            check("retained preview identifies its exact producer binary version protocol and creation time", producerVerified);
            string measurementsPath = Path.Combine(result.Directory, "run-timings.json");
            bool stagesVerified = false;
            if (File.Exists(measurementsPath))
            {
                using var measurements = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(measurementsPath));
                var data = measurements.RootElement;
                var stages = data.GetProperty("stages").EnumerateArray().ToArray();
                stagesVerified = data.GetProperty("state").GetString() == "PreviewReady" &&
                    new[] { "Capturing", "Fitting", "Layout", "Cutting" }.All(name => stages.Any(s => s.GetProperty("stage").GetString() == name)) &&
                    stages.All(s => double.IsFinite(s.GetProperty("milliseconds").GetDouble()) && s.GetProperty("milliseconds").GetDouble() >= 0) &&
                    stages.Sum(s => s.GetProperty("milliseconds").GetDouble()) <= data.GetProperty("elapsedMilliseconds").GetDouble() + .000001 &&
                    data.GetProperty("nativeExtractionMilliseconds").GetDouble() >= 0;
            }
            check("retained run timings cover every completed stage and distinguish actual extraction from queue-inclusive durations", stagesVerified);
            if (producerVerified)
            {
                byte[] producerBytes = File.ReadAllBytes(producerPath);
                string previewPath = Path.Combine(result.Directory, "preview.json");
                byte[] previewBytes = File.ReadAllBytes(previewPath);
                try
                {
                    File.AppendAllText(producerPath, " ");
                    check("explicit verification rejects changed producer evidence", runs.Verify(request.Id).State == TimberRunState.Unconfirmed);
                    File.WriteAllBytes(producerPath, producerBytes);
                    var legacy = System.Text.Json.Nodes.JsonNode.Parse(previewBytes)!;
                    legacy["schema"] = 1;
                    var evidence = (System.Text.Json.Nodes.JsonArray)legacy["evidence"]!;
                    evidence.Remove(evidence.Single(e => e!["name"]!.GetValue<string>() == "producer.json"));
                    File.WriteAllText(previewPath, legacy.ToJsonString());
                    File.Delete(producerPath);
                    await using var legacyHost = new TimberRunCoordinator(root, dispatcher);
                    check("legacy completed previews remain verifiable without producer metadata or native execution",
                        legacyHost.Verify(request.Id).State == TimberRunState.PreviewReady && model.Extractions == 1);
                }
                finally { File.WriteAllBytes(producerPath, producerBytes); File.WriteAllBytes(previewPath, previewBytes); }
            }
            bool conflict = false;
            try { runs.Start(request with { Profile = request.Profile with { ThicknessInches = .25 } }); }
            catch (OperationException error) { conflict = error.Code == "id_conflict"; }
            check("a run ID cannot be reused with changed fabrication settings", conflict);
            completedDirectory = result.Directory;
        }
        await using (var recovered = new TimberRunCoordinator(root, dispatcher))
            check("a new host can inspect a completed preview without native extraction",
                recovered.Status(request.Id).State == TimberRunState.PreviewReady && model.Extractions == 1);
        if (completedDirectory is not null && File.Exists(Path.Combine(completedDirectory, "cutting", "CUT-001.dxf")))
        {
            string manifestPath = Path.Combine(completedDirectory, "preview.json");
            string original = File.ReadAllText(manifestPath);
            var corrupt = System.Text.Json.Nodes.JsonNode.Parse(original)!;
            corrupt["cuttingFiles"]![0] = null;
            File.WriteAllText(manifestPath, corrupt.ToJsonString());
            bool unconfirmed = false;
            await using (var recovered = new TimberRunCoordinator(root, dispatcher))
            {
                try { unconfirmed = recovered.Status(request.Id).State == TimberRunState.Unconfirmed; }
                catch (Exception) { }
            }
            check("null file entries in retained JSON produce an unconfirmed run instead of escaping recovery", unconfirmed);
            File.WriteAllText(manifestPath, original);
            File.AppendAllText(Path.Combine(completedDirectory, "cutting", "CUT-001.dxf"), "corrupt");
        }
        await using (var recovered = new TimberRunCoordinator(root, dispatcher))
            check("recovery never reports a modified cutting file as a verified preview",
                recovered.Status(request.Id).State == TimberRunState.Unconfirmed);
        dispatcher.Stop();
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }

    private static async Task QueuedCancellation(Action<string, bool> check)
    {
        string root = Path.Combine(Path.GetTempPath(), "RevitThyme-TimberRuns-" + Guid.NewGuid());
        var dispatcher = new OperationDispatcher();
        var model = new NativeBoundary();
        await using (var runs = new TimberRunCoordinator(root, dispatcher))
        {
            var id = Guid.NewGuid();
            var accepted = runs.Start(new(id, model.Base.Target, new()));
            check("preview run accepts a retained handle before native extraction", accepted.State == TimberRunState.Accepted &&
                model.Extractions == 0 && File.Exists(Path.Combine(accepted.Directory, "request.json")));
            if (accepted.State == TimberRunState.Accepted)
            {
                runs.Cancel(id);
                var deadline = DateTime.UtcNow.AddSeconds(5);
                while (!runs.Status(id).Terminal && DateTime.UtcNow < deadline) await Task.Delay(5);
                check("queued preview cancellation completes without draining the native callback", runs.Status(id).State == TimberRunState.Cancelled &&
                    model.Extractions == 0 && !File.Exists(Path.Combine(accepted.Directory, "preview.json")));
            }
        }
        dispatcher.Stop();
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }

    private sealed class NativeBoundary : ITimberFoldModel
    {
        internal FakeModel Base { get; } = new();
        internal int Extractions;
        internal int ExtractionDelayMilliseconds;
        internal Action<CancellationToken>? DuringExtraction;
        internal TimberSnapshot Source;
        internal NativeBoundary() => Source = new(Base.Target, "native-source", "phase", [],
            [new("wall", "unique-wall", TimberCategory.Wall, TimberDisposition.Included, "Exterior", ["face"], [], "wall-geometry")],
            [new("face", "wall", TimberFaceKind.Wall, new(0, 0, 0), new(1, 0, 0), new(0, 0, 1), new(0, -1, 0),
                [[new(0, 0), new(12, 0), new(12, 10), new(0, 10)]])], [], []);
        public Snapshot Capture() => Base.Capture();
        public string ReadViewName() => Base.ReadViewName();
        public IChange BeginChange() => Base.BeginChange();
        public string TimberFoldEvidenceDirectory => throw new InvalidOperationException("Capture must not use inspection storage.");
        public TimberSnapshot ExtractTimberFold(TimberFoldProfile profile, TimberScope? scope)
        { Extractions++; if (ExtractionDelayMilliseconds > 0) Thread.Sleep(ExtractionDelayMilliseconds); return Source; }
        public TimberSnapshot ExtractTimberFold(TimberFoldProfile profile, TimberScope? scope, CancellationToken cancellationToken)
        { DuringExtraction?.Invoke(cancellationToken); return ExtractTimberFold(profile, scope); }
    }
}
