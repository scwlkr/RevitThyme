using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using RevitThyme.Core;

namespace RevitThyme.Unit;

internal static class TimberFoldReconciliation
{
    internal static async Task Run(Action<string, bool> check)
    {
        string root = Path.Combine(Path.GetTempPath(), "RevitThyme-Reconciliation-" + Guid.NewGuid());
        var adapter = new Adapter(); Guid generation = Guid.NewGuid(); string directory;
        await using (var runs = new TimberRunCoordinator(root, adapter))
        {
            var preview = runs.Start(new(Guid.NewGuid(), adapter.Model.Target, new()));
            await Finish(runs, preview.RunId);
            runs.StartGeneration(new(generation, adapter.Model.Target, preview.RunId));
            var result = await Finish(runs, generation); directory = result.Directory;
            check("simulated publication failure retains a definitive native commit without a successful manifest",
                result is { State: TimberRunState.Unconfirmed, NativeCommitted: true } && adapter.Finalizations == 1);
        }
        var original = Files(directory);
        await using (var runs = new TimberRunCoordinator(root, adapter))
        {
            var request = new TimberReconciliationStart(Guid.NewGuid(), generation, adapter.Model.Target with { ViewId = "kit-sheet", Revision = 3 });
            TimberRunSummary? recovered = null;
            try { runs.StartReconciliation(request); recovered = await Finish(runs, request.Id); }
            catch (OperationException) { }
            check("explicit recovery in a newly active view republishes only new evidence and never repeats native generation",
                recovered is { State: TimberRunState.KitReady, NativeCommitted: true } && recovered.GenerationRunId == generation &&
                recovered.Target == request.CurrentTarget && recovered.Directory != directory &&
                adapter.Finalizations == 1 && adapter.Reconciliations == 1 && Files(directory).SequenceEqual(original));
            if (recovered is not null)
            {
                check("same recovery request replays its handle without another native read and original remains unconfirmed",
                    runs.StartReconciliation(request).State == TimberRunState.KitReady && adapter.Reconciliations == 1 &&
                    runs.Status(generation).State == TimberRunState.Unconfirmed && runs.Verify(request.Id).State == TimberRunState.KitReady);
                bool conflict = false;
                try { runs.StartReconciliation(request with { GenerationRunId = Guid.NewGuid() }); }
                catch (OperationException error) { conflict = error.Code == "id_conflict"; }
                check("conflicting recovery handle cannot target another generation", conflict && adapter.Reconciliations == 1);
                string receipt = Path.Combine(recovered.Directory, "native-reconciliation.json");
                File.AppendAllText(receipt, " ");
                check("historical recovery verification detects changed readback evidence without native reads",
                    runs.Verify(request.Id).State == TimberRunState.Unconfirmed && adapter.Reconciliations == 1);
            }
        }
        Directory.Delete(root, true);
        await WrongCommittedSeal(check);
        await UnsafeOriginals(check);
        await NativeRefusals(check);
        await ResponsiveCancellation(check);
        await CompletedGeneration(check);
        await FailedClaim(check);
    }
    private static async Task FailedClaim(Action<string, bool> check)
    {
        string root = Path.Combine(Path.GetTempPath(), "RevitThyme-RecoveryClaim-" + Guid.NewGuid());
        var adapter = new Adapter(); var runs = new TimberRunCoordinator(root, adapter);
        Directory.Delete(root); File.WriteAllText(root, "SIMULATED-STORAGE-UNAVAILABLE");
        var request = new TimberRunStart(Guid.NewGuid(), adapter.Model.Target, new());
        bool rejected = false, settled = false;
        try { runs.Start(request); } catch (Exception error) when (error is IOException or OperationException) { rejected = true; }
        try { settled = runs.Status(request.Id).Terminal; } catch (OperationException) { settled = true; }
        check("failed storage claim cannot leave a live accepted reservation or enter the native callback", rejected && settled && adapter.Captures == 0);
        File.Delete(root); Directory.CreateDirectory(root);
        runs.Start(request); var result = await Finish(runs, request.Id);
        await runs.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
        check("a failed claim settles its lifetime and permits safe same-ID reuse after storage is restored",
            result.State == TimberRunState.PreviewReady && adapter.Captures == 1);
        Directory.Delete(root, true);
    }
    private static async Task CompletedGeneration(Action<string, bool> check)
    {
        string root = Path.Combine(Path.GetTempPath(), "RevitThyme-RecoveryCompleted-" + Guid.NewGuid());
        var adapter = new Adapter { BlockPublication = false };
        await using (var runs = new TimberRunCoordinator(root, adapter))
        {
            var generation = await Generate(runs, adapter); var original = Files(generation.Directory);
            Guid id = Guid.NewGuid(); runs.StartReconciliation(new(id, generation.RunId, adapter.Model.Target with { ViewId = "new-active-sheet", Revision = 10 }));
            var result = await Finish(runs, id);
            check("explicit reconciliation checks a sealed completed generation in the current view without changing historical success",
                generation.State == TimberRunState.KitReady && result.State == TimberRunState.KitReady && adapter.Reconciliations == 1 &&
                runs.Verify(generation.RunId).State == TimberRunState.KitReady && original.SequenceEqual(Files(generation.Directory)));
            adapter.ReconciliationState = TimberNativeReconciliationState.Absent;
            Guid absent = Guid.NewGuid(); runs.StartReconciliation(new(absent, generation.RunId, adapter.Model.Target));
            var missing = await Finish(runs, absent);
            check("native absence after a completed generation leaves historical file verification intact while current reconciliation is unconfirmed",
                missing.State == TimberRunState.Unconfirmed && runs.Verify(generation.RunId).State == TimberRunState.KitReady &&
                adapter.Reconciliations == 2 && adapter.Finalizations == 1 && !File.Exists(Path.Combine(missing.Directory, "kit.json")));
            check("an absent kit exposes an actionable diagnostic without suggesting native recreation",
                missing.Diagnostics.Any(d => d.Code == "native_kit_absent"));
            using var timing = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(result.Directory, "run-timings.json")));
            check("recovery records its native read duration separately from original native creation timings",
                timing.RootElement.TryGetProperty("nativeReconciliationMilliseconds", out var duration) && duration.GetDouble() == 7);
        }
        Directory.Delete(root, true);
    }
    private static async Task UnsafeOriginals(Action<string, bool> check)
    {
        foreach (string fault in new[] { "missing-receipt", "ambiguous-receipt", "missing-seal", "changed-plan", "changed-output", "extra-output", "new-session" })
        {
            string root = Path.Combine(Path.GetTempPath(), "RevitThyme-RecoveryProof-" + Guid.NewGuid());
            var adapter = new Adapter();
            await using (var runs = new TimberRunCoordinator(root, adapter))
            {
                var generation = await Generate(runs, adapter); string directory = generation.Directory;
                string receiptPath = Path.Combine(directory, "native-receipt.json");
                switch (fault)
                {
                    case "missing-receipt": File.Delete(receiptPath); break;
                    case "ambiguous-receipt":
                        var receipt = JsonSerializer.Deserialize<TimberNativeKitReceipt>(File.ReadAllBytes(receiptPath), Protocol.Json)!;
                        File.WriteAllBytes(receiptPath, JsonSerializer.SerializeToUtf8Bytes(receipt with { State = TimberNativeKitState.Unconfirmed }, Protocol.Json)); break;
                    case "missing-seal": File.Delete(Path.Combine(directory, "generation-inputs.json")); break;
                    case "changed-plan": File.AppendAllText(Path.Combine(directory, "native-plan.json"), " "); break;
                    case "changed-output": File.AppendAllText(Path.Combine(directory, "native-cuts", "CUT-001.dwg"), "changed"); break;
                    case "extra-output": File.WriteAllText(Path.Combine(directory, "native-cuts", "EXTRA.dwg"), "extra"); break;
                }
                var before = Files(directory);
                var target = fault == "new-session" ? adapter.Model.Target with { SessionId = "other-session" } : adapter.Model.Target;
                Guid id = Guid.NewGuid(); runs.StartReconciliation(new(id, generation.RunId, target));
                var result = await Finish(runs, id);
                check("recovery cannot bless " + fault + " or repeat the original mutation",
                    result.State == TimberRunState.Unconfirmed && adapter.Reconciliations == 0 && adapter.Finalizations == 1 &&
                    !File.Exists(Path.Combine(result.Directory, "kit.json")) && before.SequenceEqual(Files(directory)));
            }
            Directory.Delete(root, true);
        }
    }
    private static async Task NativeRefusals(Action<string, bool> check)
    {
        foreach (string fault in new[] { "absent", "partial", "changed-during-read", "changed-recovery-input" })
        {
            string root = Path.Combine(Path.GetTempPath(), "RevitThyme-RecoveryNative-" + Guid.NewGuid());
            var adapter = new Adapter();
            await using (var runs = new TimberRunCoordinator(root, adapter))
            {
                var generation = await Generate(runs, adapter);
                adapter.ReconciliationState = fault == "absent" ? TimberNativeReconciliationState.Absent : TimberNativeReconciliationState.Verified;
                adapter.OmitObserved = fault == "partial";
                if (fault == "changed-during-read") adapter.DuringReconciliation = () => File.AppendAllText(
                    Path.Combine(generation.Directory, "cutting", "CUT-001.dxf"), " ");
                Guid id = Guid.NewGuid();
                if (fault == "changed-recovery-input") adapter.DuringReconciliation = () => File.AppendAllText(
                    Path.Combine(root, id.ToString("D"), "producer.json"), " ");
                runs.StartReconciliation(new(id, generation.RunId, adapter.Model.Target));
                var result = await Finish(runs, id);
                check("native " + fault + " remains unconfirmed without new native elements or success manifest",
                    result is { State: TimberRunState.Unconfirmed, NativeCommitted: true } && adapter.Reconciliations == 1 &&
                    adapter.Finalizations == 1 && !File.Exists(Path.Combine(result.Directory, "kit.json")));
            }
            Directory.Delete(root, true);
        }
    }
    private static async Task ResponsiveCancellation(Action<string, bool> check)
    {
        string root = Path.Combine(Path.GetTempPath(), "RevitThyme-RecoveryControl-" + Guid.NewGuid());
        var adapter = new Adapter();
        await using (var runs = new TimberRunCoordinator(root, adapter))
        {
            var generation = await Generate(runs, adapter); var original = Files(generation.Directory);
            adapter.ReconciliationGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Guid id = Guid.NewGuid(); var clock = System.Diagnostics.Stopwatch.StartNew();
            var request = new TimberReconciliationStart(id, generation.RunId, adapter.Model.Target);
            var accepted = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => runs.StartReconciliation(request))));
            bool shortAck = clock.ElapsedMilliseconds < 1000 && accepted.All(a => a.RunId == id && !a.Terminal);
            await adapter.ReconciliationEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            clock.Restart(); var live = runs.Status(id); runs.Cancel(id);
            var result = await Finish(runs, id);
            check("recovery start/status/cancel remain responsive while native read is held and cancellation never publishes",
                shortAck && !live.Terminal && clock.ElapsedMilliseconds < 500 && result.State == TimberRunState.Cancelled &&
                !File.Exists(Path.Combine(result.Directory, "kit.json")) && adapter.Finalizations == 1 && adapter.Reconciliations == 1 && original.SequenceEqual(Files(generation.Directory)));
            adapter.ReconciliationGate.TrySetResult();
        }
        Directory.Delete(root, true);
    }
    private static async Task<TimberRunSummary> Generate(TimberRunCoordinator runs, Adapter adapter)
    {
        var preview = runs.Start(new(Guid.NewGuid(), adapter.Model.Target, new())); await Finish(runs, preview.RunId);
        Guid id = Guid.NewGuid(); runs.StartGeneration(new(id, adapter.Model.Target, preview.RunId)); return await Finish(runs, id);
    }
    private static async Task WrongCommittedSeal(Action<string, bool> check)
    {
        string root = Path.Combine(Path.GetTempPath(), "RevitThyme-ReconciliationSeal-" + Guid.NewGuid());
        var adapter = new Adapter { BlockPublication = false, ReceiptSealOverride = new('0', 64) };
        await using (var runs = new TimberRunCoordinator(root, adapter))
        {
            var preview = runs.Start(new(Guid.NewGuid(), adapter.Model.Target, new())); await Finish(runs, preview.RunId);
            Guid id = Guid.NewGuid(); runs.StartGeneration(new(id, adapter.Model.Target, preview.RunId));
            var result = await Finish(runs, id);
            check("a committed receipt cannot publish against a different verified input seal",
                result is { State: TimberRunState.Unconfirmed, NativeCommitted: true } && !File.Exists(Path.Combine(result.Directory, "kit.json")));
        }
        Directory.Delete(root, true);
    }
    private static string[] Files(string directory) => Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
        .Order(StringComparer.Ordinal).Select(p => Path.GetRelativePath(directory, p) + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)))).ToArray();
    private static async Task<TimberRunSummary> Finish(TimberRunCoordinator runs, Guid id)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!runs.Status(id).Terminal && DateTime.UtcNow < deadline) await Task.Delay(5);
        return runs.Status(id);
    }
    private sealed class Adapter : ITimberCaptureQueue, ITimberFinalizationQueue, ITimberReconciliationQueue
    {
        internal FakeModel Model { get; } = new();
        internal int Captures, Finalizations, Reconciliations;
        internal bool BlockPublication = true;
        internal string? ReceiptSealOverride;
        internal TimberNativeReconciliationState ReconciliationState = TimberNativeReconciliationState.Verified;
        internal bool OmitObserved;
        internal Action? DuringReconciliation;
        internal TaskCompletionSource? ReconciliationGate;
        internal TaskCompletionSource ReconciliationEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<TimberCapturedSnapshot> CaptureTimberAsync(TimberCaptureRequest request, CancellationToken ct = default)
        {
            Captures++;
            var snapshot = new TimberSnapshot(Model.Target, "recovery-source", "phase", [],
                [new("wall", "unique-wall", TimberCategory.Wall, TimberDisposition.Included, "Exterior", ["face"], [], "wall-geometry")],
                [new("face", "wall", TimberFaceKind.Wall, new(0, 0, 0), new(1, 0, 0), new(0, 0, 1), new(0, -1, 0),
                    [[new(0, 0), new(12, 0), new(12, 10), new(0, 10)]])], [], []);
            return Task.FromResult(new TimberCapturedSnapshot(Model.Capture(), JsonSerializer.SerializeToUtf8Bytes(snapshot, Protocol.Json).ToImmutableArray()));
        }
        public Task<TimberNativeKitReceipt> FinalizeTimberAsync(TimberNativeKitRequest request, CancellationToken ct = default)
        {
            Finalizations++;
            var files = ImmutableArray.CreateBuilder<TimberNativeFile>();
            Directory.CreateDirectory(Path.Combine(request.Directory, "native-cuts"));
            foreach (string name in new[] { "CUT-001.dwg", "CALIBRATION.dwg" })
            {
                byte[] bytes = "SIMULATED-NATIVE-READBACK"u8.ToArray();
                File.WriteAllBytes(Path.Combine(request.Directory, "native-cuts", name), bytes);
                files.Add(new("native-cuts/" + name, Convert.ToHexStringLower(SHA256.HashData(bytes)), bytes.Length,
                    name == "CALIBRATION.dwg" ? "CalibrationDwg" : "CutDwg"));
            }
            byte[] prepared = JsonSerializer.SerializeToUtf8Bytes(new { schema = 2, request.RunId, inputSealSha256 = request.InputSealSha256 }, Protocol.Json);
            File.WriteAllBytes(Path.Combine(request.Directory, "native-prepared.json"), prepared);
            if (BlockPublication) Directory.CreateDirectory(Path.Combine(request.Directory, "kit.json")); // Inject publication failure after commit.
            return Task.FromResult(new TimberNativeKitReceipt(TimberNativeKitState.Committed, request.RunId, Model.Target,
                [new(123, "simulated-sheet", "NativeSheet")], files.ToImmutable(), [], true, true,
                Convert.ToHexStringLower(SHA256.HashData(prepared)), InputSealSha256: ReceiptSealOverride ?? request.InputSealSha256));
        }
        public async Task<TimberNativeReconciliationReceipt> ReconcileTimberAsync(TimberNativeReconciliationRequest request, CancellationToken ct = default)
        {
            Reconciliations++;
            ReconciliationEntered.TrySetResult();
            if (ReconciliationGate is not null) await ReconciliationGate.Task.WaitAsync(ct);
            DuringReconciliation?.Invoke();
            return new(ReconciliationState, request.RunId, request.GenerationRunId, request.CurrentTarget, request.CurrentTarget,
                OmitObserved ? [] : request.CommitReceipt.CreatedElements, [], true, true, NativeMilliseconds: 7);
        }
    }
}
