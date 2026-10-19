using RevitThyme.Core;

namespace RevitThyme.Unit;

internal static class TimberFoldFinalization
{
    internal static async Task Run(Action<string, bool> check)
    {
        var dispatcher = new OperationDispatcher();
        if ((object)dispatcher is not ITimberFinalizationQueue queue)
        { check("native finalization is available through the shared callback queue", false); return; }
        var model = new NativeBoundary();
        var request = new TimberNativeKitRequest(Guid.NewGuid(), model.Base.Target, "owned-run", [1, 2, 3]);
        var pending = queue.FinalizeTimberAsync(request);
        check("finalization waits for the native callback without touching the model",
            !pending.IsCompleted && model.Captures == 0 && model.Mutations == 0);
        dispatcher.ExecuteNext(model);
        var receipt = await pending;
        check("finalization returns the definitive committed native receipt",
            receipt.State == TimberNativeKitState.Committed && receipt.RunId == request.RunId &&
            receipt.CreatedElements is [{ Id: 101 }] && model.Mutations == 1);
        await QueuedCancellation(check);
        await TargetGuard(check);
        await NativeSupportGuard(check);
        await InFlightCancellation(check);
        await CallbackFailure(check);
        await QueueOrder(check);
        await StoppedAndScheduling(check);
        await OwnedBytes(check);
    }

    private static async Task QueuedCancellation(Action<string, bool> check)
    {
        var dispatcher = new OperationDispatcher();
        var model = new NativeBoundary();
        using var cancelled = new CancellationTokenSource();
        var pending = dispatcher.FinalizeTimberAsync(new(Guid.NewGuid(), model.Base.Target, "owned-run", []), cancelled.Token);
        cancelled.Cancel();
        dispatcher.ExecuteNext(model);
        var receipt = await pending;
        check("queued finalization cancellation rejects without touching native state",
            receipt.State == TimberNativeKitState.Rejected && receipt.SourcePreserved &&
            receipt.Diagnostics is [{ Code: "cancelled" }] && model.Captures == 0 && model.Mutations == 0);
    }

    private static async Task TargetGuard(Action<string, bool> check)
    {
        var dispatcher = new OperationDispatcher();
        var model = new NativeBoundary();
        var stale = model.Base.Target with { Revision = model.Base.Target.Revision - 1 };
        var pending = dispatcher.FinalizeTimberAsync(new(Guid.NewGuid(), stale, "owned-run", []));
        dispatcher.ExecuteNext(model);
        var receipt = await pending;
        check("finalization rejects a changed native target before mutation",
            receipt.State == TimberNativeKitState.Rejected && receipt.Diagnostics is [{ Code: "stale_target" }] &&
            receipt.AfterTarget == model.Base.Target && receipt.SourcePreserved && model.Mutations == 0);
    }

    private static async Task NativeSupportGuard(Action<string, bool> check)
    {
        var dispatcher = new OperationDispatcher();
        var model = new NativeBoundary(); model.Base.Writable = false;
        var blocked = dispatcher.FinalizeTimberAsync(new(Guid.NewGuid(), model.Base.Target, "owned-run", []));
        dispatcher.ExecuteNext(model);
        check("read-only native finalization is rejected before mutation", (await blocked) is
            { State: TimberNativeKitState.Rejected, Diagnostics: [{ Code: "write_blocked" }], SourcePreserved: true } && model.Mutations == 0);
        var unsupported = new FakeModel();
        var missing = dispatcher.FinalizeTimberAsync(new(Guid.NewGuid(), unsupported.Target, "owned-run", []));
        dispatcher.ExecuteNext(unsupported);
        check("models without native finalization reject without opening transactions", (await missing) is
            { State: TimberNativeKitState.Rejected, Diagnostics: [{ Code: "unsupported" }], SourcePreserved: true } && unsupported.Transactions == 0);
    }

    private static async Task InFlightCancellation(Action<string, bool> check)
    {
        var dispatcher = new OperationDispatcher();
        var model = new NativeBoundary();
        using var cancellation = new CancellationTokenSource();
        using var finish = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var request = new TimberNativeKitRequest(Guid.NewGuid(), model.Base.Target, "owned-run", []);
        var committed = new TimberNativeKitReceipt(TimberNativeKitState.Committed, request.RunId, model.Base.Target,
            [new(101, "native-element", "panel")], [new("kit.dwg", "native-sha", 123, "native-export")], [], true, true, "prepared-sha");
        model.Finalize = (_, _) => { entered.SetResult(); if (!finish.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException(); return committed; };
        var pending = dispatcher.FinalizeTimberAsync(request, cancellation.Token);
        var queued = dispatcher.FinalizeTimberAsync(request with { RunId = Guid.NewGuid() });
        var callback = Task.Run(() => dispatcher.ExecuteNext(model));
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            dispatcher.Schedule = () => throw new InvalidOperationException("Cannot schedule another callback");
            var unscheduled = await dispatcher.FinalizeTimberAsync(request with { RunId = Guid.NewGuid() });
            check("scheduling failure rejects only queued native finalizations", !pending.IsCompleted &&
                unscheduled.Diagnostics is [{ Code: "schedule_failed" }] && (await queued).Diagnostics is [{ Code: "schedule_failed" }]);
            dispatcher.Schedule = null;
            var stopped = dispatcher.FinalizeTimberAsync(request with { RunId = Guid.NewGuid() });
            cancellation.Cancel(); dispatcher.Stop();
            check("cancellation and host stop cannot abandon a running finalization receipt", !pending.IsCompleted &&
                (await stopped).Diagnostics is [{ Code: "host_stopped" }] && model.Mutations == 1);
        }
        finally { finish.Set(); }
        await callback.WaitAsync(TimeSpan.FromSeconds(5));
        var receipt = await pending;
        check("in-flight cancellation preserves the definitive committed native receipt", receipt.State == TimberNativeKitState.Committed &&
            receipt.RunId == request.RunId && receipt.AfterTarget == model.Base.Target && receipt.SourcePreserved && receipt.NativeReadbackPassed &&
            receipt.PreparedReceiptSha256 == "prepared-sha" && receipt.CreatedElements is [{ Id: 101, UniqueId: "native-element", Role: "panel" }] &&
            receipt.Files is [{ RelativePath: "kit.dwg", Sha256: "native-sha", Bytes: 123, Role: "native-export" }]);
    }

    private static async Task CallbackFailure(Action<string, bool> check)
    {
        foreach (Exception error in new Exception[] { new InvalidOperationException("Unknown native failure"), new OperationCanceledException() })
        {
            var dispatcher = new OperationDispatcher();
            var model = new NativeBoundary { Finalize = (_, _) => throw error };
            var pending = dispatcher.FinalizeTimberAsync(new(Guid.NewGuid(), model.Base.Target, "owned-run", []));
            dispatcher.ExecuteNext(model);
            check("an exception after native invocation is unconfirmed rather than a safe rejection", (await pending) is
                { State: TimberNativeKitState.Unconfirmed, SourcePreserved: false, NativeReadbackPassed: false } && !pending.IsCanceled);
        }
    }

    private static async Task QueueOrder(Action<string, bool> check)
    {
        var dispatcher = new OperationDispatcher();
        var model = new NativeBoundary();
        var captured = dispatcher.CaptureTimberAsync(new(Guid.NewGuid(), model.Base.Target, new()));
        var finalized = dispatcher.FinalizeTimberAsync(new(Guid.NewGuid(), model.Base.Target, "owned-run", []));
        var inspected = dispatcher.ExecuteAsync(new(Guid.NewGuid(), OperationKind.Inspect));
        dispatcher.ExecuteNext(model);
        check("native capture stays before queued finalization and operations", captured.IsCompletedSuccessfully && !finalized.IsCompleted && !inspected.IsCompleted);
        dispatcher.ExecuteNext(model);
        check("native finalization keeps FIFO order with existing work", (await finalized).State == TimberNativeKitState.Committed && !inspected.IsCompleted);
        dispatcher.ExecuteNext(model);
        check("ordinary operations continue after native finalization", (await inspected).Succeeded && (await captured).Context.Target == model.Base.Target);
    }

    private static async Task StoppedAndScheduling(Action<string, bool> check)
    {
        var model = new NativeBoundary();
        var request = new TimberNativeKitRequest(Guid.NewGuid(), model.Base.Target, "owned-run", []);
        var dispatcher = new OperationDispatcher(); dispatcher.Stop();
        check("stopped finalization rejects without touching the model", (await dispatcher.FinalizeTimberAsync(request)) is
            { State: TimberNativeKitState.Rejected, Diagnostics: [{ Code: "host_stopped" }] } && model.Captures == 0);
        dispatcher = new() { Schedule = () => throw new InvalidOperationException("Cannot schedule") };
        check("scheduling failure gives queued finalization a known no-mutation rejection", (await dispatcher.FinalizeTimberAsync(request)) is
            { State: TimberNativeKitState.Rejected, Diagnostics: [{ Code: "schedule_failed" }], SourcePreserved: true } && model.Mutations == 0);
    }

    private static async Task OwnedBytes(Action<string, bool> check)
    {
        var dispatcher = new OperationDispatcher();
        var model = new NativeBoundary();
        byte[] producerBytes = [1, 2, 3];
        var bytes = System.Runtime.InteropServices.ImmutableCollectionsMarshal.AsImmutableArray(producerBytes);
        byte observed = 0;
        model.Finalize = (request, _) => { observed = request.PlanJson[0]; return new(TimberNativeKitState.RolledBack,
            request.RunId, model.Base.Target, [], [], [], true, false); };
        var pending = dispatcher.FinalizeTimberAsync(new(Guid.NewGuid(), model.Base.Target, "owned-run", bytes));
        producerBytes[0] = 99;
        dispatcher.ExecuteNext(model);
        check("native finalization owns its encoded plan and preserves a known rollback receipt", observed == 1 &&
            (await pending).State == TimberNativeKitState.RolledBack);
    }

    private sealed class NativeBoundary : ITimberNativeKitModel, ITimberFoldModel
    {
        internal readonly FakeModel Base = new();
        internal int Captures, Mutations;
        internal Func<TimberNativeKitRequest, CancellationToken, TimberNativeKitReceipt>? Finalize;
        public Snapshot Capture() { Captures++; return Base.Capture(); }
        public string ReadViewName() => Base.ReadViewName();
        public IChange BeginChange() => Base.BeginChange();
        public TimberNativeKitReceipt FinalizeTimberKit(TimberNativeKitRequest request, CancellationToken cancellationToken)
        {
            Mutations++;
            if (Finalize is { } finalize) return finalize(request, cancellationToken);
            return new(TimberNativeKitState.Committed, request.RunId, Base.Target,
                [new(101, "native-element", "panel", "P001")], [], [], true, true);
        }
        public string TimberFoldEvidenceDirectory => throw new InvalidOperationException("Capture must not write inspection evidence.");
        public TimberSnapshot ExtractTimberFold(TimberFoldProfile profile, TimberScope? scope) =>
            new(Base.Target, "native-source", "phase", [], [], [], [], []);
    }
}
