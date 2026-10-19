using RevitThyme.Core;

namespace RevitThyme.Unit;

internal static class TimberFoldReconciliationQueue
{
    internal static async Task Run(Action<string, bool> check)
    {
        await using var dispatcher = new OperationDispatcher();
        if ((object)dispatcher is not ITimberReconciliationQueue queue)
        { check("shared queue supports read-only native kit reconciliation", false); return; }
        var model = new Boundary(); model.Base.Writable = false;
        var request = new TimberNativeReconciliationRequest(Guid.NewGuid(), Guid.NewGuid(), model.Base.Target,
            "generation", "recovery", [1], [2], new(TimberNativeKitState.Committed, Guid.NewGuid(), model.Base.Target,
                [new(101, "native", "CutView")], [], [], true, true));
        var pending = queue.ReconcileTimberAsync(request);
        check("native reconciliation stays queued until its exact callback", !pending.IsCompleted && model.Reads == 0);
        dispatcher.ExecuteNext(model);
        var receipt = await pending;
        check("native reconciliation can inspect a read-only model without transactions", receipt is
            { State: TimberNativeReconciliationState.Verified, SourcePreserved: true, NativeReadbackPassed: true } &&
            model.Reads == 1 && model.Base.Transactions == 0);
        var stale = queue.ReconcileTimberAsync(request with { CurrentTarget = request.CurrentTarget with { Revision = 0 } });
        dispatcher.ExecuteNext(model);
        check("native reconciliation rejects changed target before inspecting the kit", (await stale).State != TimberNativeReconciliationState.Verified && model.Reads == 1);
        using var cancellation = new CancellationTokenSource();
        var cancelled = queue.ReconcileTimberAsync(request, cancellation.Token); cancellation.Cancel();
        dispatcher.ExecuteNext(model);
        try { await cancelled; check("queued reconciliation cancellation prevents native reads", false); }
        catch (OperationCanceledException) { check("queued reconciliation cancellation prevents native reads", model.Reads == 1); }
        model.Check = (_, _) => throw new InvalidOperationException("read failed");
        var failed = queue.ReconcileTimberAsync(request); dispatcher.ExecuteNext(model);
        check("native reconciliation exceptions cannot become verified", (await failed).State == TimberNativeReconciliationState.Unconfirmed);
        await Ownership(check);
    }

    private static async Task Ownership(Action<string, bool> check)
    {
        await using var dispatcher = new OperationDispatcher(); var model = new Boundary();
        byte[] bytes = [3]; var shared = System.Runtime.InteropServices.ImmutableCollectionsMarshal.AsImmutableArray(bytes);
        var request = new TimberNativeReconciliationRequest(Guid.NewGuid(), Guid.NewGuid(), model.Base.Target,
            "generation", "recovery", shared, shared, new(TimberNativeKitState.Committed, Guid.NewGuid(), model.Base.Target, [], [], [], true, true));
        byte plan = 0, prepared = 0;
        model.Check = (owned, _) =>
        {
            plan = owned.PlanJson[0]; prepared = owned.PreparedJson[0];
            return new(TimberNativeReconciliationState.Absent, owned.RunId, owned.GenerationRunId, owned.CurrentTarget,
                owned.CurrentTarget, [], [], true, false);
        };
        var pending = dispatcher.ReconcileTimberAsync(request); bytes[0] = 99; dispatcher.ExecuteNext(model);
        check("reconciliation queue owns immutable plan and prepared bytes", (await pending).State == TimberNativeReconciliationState.Absent && plan == 3 && prepared == 3);
    }

    private sealed class Boundary : ITimberNativeReconciliationModel
    {
        internal readonly FakeModel Base = new();
        internal int Reads;
        internal Func<TimberNativeReconciliationRequest, CancellationToken, TimberNativeReconciliationReceipt>? Check;
        public Snapshot Capture() => Base.Capture();
        public string ReadViewName() => Base.ReadViewName();
        public IChange BeginChange() => Base.BeginChange();
        public TimberNativeReconciliationReceipt ReconcileTimberKit(TimberNativeReconciliationRequest request, CancellationToken ct)
        {
            Reads++;
            return Check?.Invoke(request, ct) ?? new(TimberNativeReconciliationState.Verified, request.RunId, request.GenerationRunId,
                request.CurrentTarget, request.CurrentTarget, request.CommitReceipt.CreatedElements, [], true, true);
        }
    }
}
