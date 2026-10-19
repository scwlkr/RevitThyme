using RevitThyme.Core;
using System.Collections.Immutable;
using System.Text.Json;
using System.Security.Cryptography;

namespace RevitThyme.Unit;

internal static class TimberFoldGeneration
{
    public static async Task Run(Action<string, bool> check)
    {
        string root = Path.Combine(Path.GetTempPath(), "RevitThyme-TimberGeneration-" + Guid.NewGuid());
        await using var dispatcher = new OperationDispatcher();
        await using (var runs = new TimberRunCoordinator(root, dispatcher))
        {
            var request = new TimberGenerationStart(Guid.NewGuid(), new FakeModel().Target, Guid.NewGuid());
            bool rejected = false;
            try { runs.StartGeneration(request); }
            catch (OperationException error) { rejected = error.Code == "preview_required"; }
            check("generation rejects a missing verified preview before native queueing or creating output",
                rejected && !dispatcher.HasPending && !Directory.Exists(Path.Combine(root, request.Id.ToString("D"))));
        }
        if (Directory.Exists(root)) Directory.Delete(root, true);
        await RejectedNativeGeneration(check);
        await CommittedGeneration(check);
        await SharedGeneration(check);
        await CommittedCancellationAndRecovery(check);
        await ChangedStagingAfterCommit(check);
        await VerifyActiveRun(check);
        await UnexpectedNativeOutput(check);
    }

    private static async Task UnexpectedNativeOutput(Action<string, bool> check)
    {
        string root = Path.Combine(Path.GetTempPath(), "RevitThyme-TimberGenerationExtra-" + Guid.NewGuid());
        var adapter = new GenerationAdapter { Commit = true };
        await using (var runs = new TimberRunCoordinator(root, adapter))
        {
            var preview = runs.Start(new(Guid.NewGuid(), adapter.Model.Target, new()));
            await Finish(runs, preview.RunId);
            Guid id = Guid.NewGuid();
            adapter.BeforeReceipt = () => File.WriteAllText(Path.Combine(root, id.ToString("D"), "native-cuts", "UNEXPECTED.dwg"), "unexpected");
            runs.StartGeneration(new(id, adapter.Model.Target, preview.RunId));
            var result = await Finish(runs, id);
            check("unlisted native cutting output blocks publication while retaining the confirmed native commit",
                result is { State: TimberRunState.Unconfirmed, NativeCommitted: true } &&
                !File.Exists(Path.Combine(result.Directory, "kit.json")) && adapter.Finalizations == 1);
        }
        Directory.Delete(root, true);
    }

    private static async Task VerifyActiveRun(Action<string, bool> check)
    {
        string root = Path.Combine(Path.GetTempPath(), "RevitThyme-TimberVerifyActive-" + Guid.NewGuid());
        var adapter = new GenerationAdapter { CaptureGate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        await using (var runs = new TimberRunCoordinator(root, adapter))
        {
            var run = runs.Start(new(Guid.NewGuid(), adapter.Model.Target, new()));
            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (adapter.Captures == 0 && DateTime.UtcNow < deadline) await Task.Delay(5);
            check("verification during an owned active run preserves its live state instead of claiming recovery uncertainty",
                !runs.Verify(run.RunId).Terminal);
            adapter.CaptureGate.TrySetResult();
            await Finish(runs, run.RunId);
        }
        Directory.Delete(root, true);
    }

    private static async Task ChangedStagingAfterCommit(Action<string, bool> check)
    {
        string root = Path.Combine(Path.GetTempPath(), "RevitThyme-TimberGenerationTamper-" + Guid.NewGuid());
        var adapter = new GenerationAdapter { Commit = true };
        await using (var runs = new TimberRunCoordinator(root, adapter))
        {
            var preview = runs.Start(new(Guid.NewGuid(), adapter.Model.Target, new()));
            await Finish(runs, preview.RunId);
            Guid id = Guid.NewGuid();
            adapter.BeforeReceipt = () => File.AppendAllText(Path.Combine(root, id.ToString("D"), "cutting", "CUT-001.dxf"), "changed");
            runs.StartGeneration(new(id, adapter.Model.Target, preview.RunId));
            var result = await Finish(runs, id);
            check("post-commit staging changes stay unconfirmed and cannot be blessed into a success manifest",
                result is { State: TimberRunState.Unconfirmed, NativeCommitted: true } &&
                !File.Exists(Path.Combine(result.Directory, "kit.json")) && adapter.Finalizations == 1);
        }
        Directory.Delete(root, true);
    }

    private static async Task CommittedCancellationAndRecovery(Action<string, bool> check)
    {
        string root = Path.Combine(Path.GetTempPath(), "RevitThyme-TimberGenerationRecovery-" + Guid.NewGuid());
        var adapter = new GenerationAdapter { Commit = true };
        Guid id = Guid.NewGuid(); string directory;
        await using (var runs = new TimberRunCoordinator(root, adapter))
        {
            var preview = runs.Start(new(Guid.NewGuid(), adapter.Model.Target, new()));
            await Finish(runs, preview.RunId);
            adapter.BeforeReceipt = () => runs.Cancel(id);
            runs.StartGeneration(new(id, adapter.Model.Target, preview.RunId));
            var result = await Finish(runs, id); directory = result.Directory;
            check("cancellation racing a confirmed native commit preserves the committed kit result",
                result.State == TimberRunState.KitReady && result.NativeCommitted && adapter.Finalizations == 1);
        }
        await using (var recovered = new TimberRunCoordinator(root, adapter))
            check("a recovered kit revalidates all retained files without repeating native generation",
                recovered.Status(id).State == TimberRunState.KitReady && adapter.Finalizations == 1);
        File.AppendAllText(Path.Combine(directory, "native-cuts", "CUT-001.dwg"), "changed");
        await using (var recovered = new TimberRunCoordinator(root, adapter))
            check("verification rejects a changed native cutting file and retains the known committed state",
                recovered.Verify(id) is { State: TimberRunState.Unconfirmed, NativeCommitted: true } && adapter.Finalizations == 1);
        Directory.Delete(root, true);
    }

    private static async Task SharedGeneration(Action<string, bool> check)
    {
        string root = Path.Combine(Path.GetTempPath(), "RevitThyme-TimberGenerationOps-" + Guid.NewGuid());
        await using (var dispatcher = new OperationDispatcher(root))
        {
            OperationResult? result = null;
            try { result = await dispatcher.ExecuteAsync(new(Guid.NewGuid(), OperationKind.GenerateTimberFold,
                new FakeModel().Target, OperationId: Guid.NewGuid())).WaitAsync(TimeSpan.FromMilliseconds(500)); }
            catch (TimeoutException) { }
            check("shared generation rejects an absent preview without entering the native callback queue",
                result?.Code == "preview_required" && !dispatcher.HasPending);
        }
        Directory.Delete(root, true);
    }

    private static async Task CommittedGeneration(Action<string, bool> check)
    {
        string root = Path.Combine(Path.GetTempPath(), "RevitThyme-TimberGeneration-" + Guid.NewGuid());
        var adapter = new GenerationAdapter { Commit = true };
        await using (var runs = new TimberRunCoordinator(root, adapter))
        {
            var preview = runs.Start(new(Guid.NewGuid(), adapter.Model.Target, new()));
            await Finish(runs, preview.RunId);
            var request = new TimberGenerationStart(Guid.NewGuid(), adapter.Model.Target, preview.RunId);
            runs.StartGeneration(request);
            var result = await Finish(runs, request.Id);
            check("a confirmed native commit publishes a complete digital kit and retries never repeat the mutation",
                result.State.ToString() == "KitReady" && result.NativeCommitted && !result.NativeImportOutstanding &&
                result.PhysicalChecksOutstanding && !result.ManufacturingReady && File.Exists(Path.Combine(result.Directory, "kit.json")) &&
                runs.StartGeneration(request).State == result.State && adapter.Finalizations == 1);
        }
        Directory.Delete(root, true);
    }

    private static async Task RejectedNativeGeneration(Action<string, bool> check)
    {
        string root = Path.Combine(Path.GetTempPath(), "RevitThyme-TimberGeneration-" + Guid.NewGuid());
        var adapter = new GenerationAdapter();
        await using (var runs = new TimberRunCoordinator(root, adapter))
        {
            var preview = runs.Start(new(Guid.NewGuid(), adapter.Model.Target, new()));
            await Finish(runs, preview.RunId);
            var id = Guid.NewGuid();
            TimberRunSummary? result = null;
            try
            {
                runs.StartGeneration(new(id, adapter.Model.Target, preview.RunId));
                result = await Finish(runs, id);
            }
            catch (OperationException) { }
            check("generation recomputes a verified preview, stages guides and retains native rejection without a success manifest",
                result?.State == TimberRunState.Failed && adapter.Finalizations == 1 && adapter.Captures == 2 &&
                File.Exists(Path.Combine(result.Directory, "guides", "GUIDE.svg")) &&
                File.Exists(Path.Combine(result.Directory, "native-receipt.json")) &&
                !File.Exists(Path.Combine(result.Directory, "kit.json")));
        }
        Directory.Delete(root, true);
    }

    private static async Task<TimberRunSummary> Finish(TimberRunCoordinator runs, Guid id)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!runs.Status(id).Terminal && DateTime.UtcNow < deadline) await Task.Delay(5);
        return runs.Status(id);
    }

    private sealed class GenerationAdapter : ITimberCaptureQueue, ITimberFinalizationQueue
    {
        internal FakeModel Model { get; } = new();
        internal int Captures, Finalizations;
        internal bool Commit;
        internal Action? BeforeReceipt;
        internal TaskCompletionSource? CaptureGate;
        public async Task<TimberCapturedSnapshot> CaptureTimberAsync(TimberCaptureRequest request, CancellationToken ct = default)
        {
            Captures++;
            if (CaptureGate is not null) await CaptureGate.Task.WaitAsync(ct);
            var source = new TimberSnapshot(Model.Target, "generation-source", "phase", [],
                [new("wall", "unique-wall", TimberCategory.Wall, TimberDisposition.Included, "Exterior", ["face"], [], "wall-geometry")],
                [new("face", "wall", TimberFaceKind.Wall, new(0, 0, 0), new(1, 0, 0), new(0, 0, 1), new(0, -1, 0),
                    [[new(0, 0), new(12, 0), new(12, 10), new(0, 10)]])], [], []);
            return new TimberCapturedSnapshot(Model.Capture(), JsonSerializer.SerializeToUtf8Bytes(source, Protocol.Json).ToImmutableArray());
        }
        public Task<TimberNativeKitReceipt> FinalizeTimberAsync(TimberNativeKitRequest request, CancellationToken ct = default)
        {
            Finalizations++;
            if (Commit)
            {
                // This adapter models the definitive receipt seam; these are deliberately
                // simulated bytes, never evidence that a native DWG consumer passed.
                string native = Path.Combine(request.Directory, "native-cuts"); Directory.CreateDirectory(native);
                var files = ImmutableArray.CreateBuilder<TimberNativeFile>();
                foreach (string name in new[] { "CUT-001.dwg", "CALIBRATION.dwg" })
                {
                    byte[] bytes = System.Text.Encoding.ASCII.GetBytes("SIMULATED-NATIVE-RECEIPT-" + name);
                    File.WriteAllBytes(Path.Combine(native, name), bytes);
                    files.Add(new("native-cuts/" + name, Convert.ToHexStringLower(SHA256.HashData(bytes)), bytes.Length,
                        name == "CALIBRATION.dwg" ? "CalibrationDwg" : "CutDwg"));
                }
                byte[] prepared = "{\"simulated\":true}"u8.ToArray();
                File.WriteAllBytes(Path.Combine(request.Directory, "native-prepared.json"), prepared);
                BeforeReceipt?.Invoke();
                return Task.FromResult(new TimberNativeKitReceipt(TimberNativeKitState.Committed, request.RunId, Model.Target,
                    [new(123, "simulated-sheet", "ReviewSheet")], files.ToImmutable(), [], true, true,
                    Convert.ToHexStringLower(SHA256.HashData(prepared))));
            }
            return Task.FromResult(new TimberNativeKitReceipt(TimberNativeKitState.Rejected, request.RunId, Model.Target,
                [], [], [new("native_test_rejection", TimberSeverity.Error, "Native adapter rejected before mutation.")], true, false));
        }
    }
}
