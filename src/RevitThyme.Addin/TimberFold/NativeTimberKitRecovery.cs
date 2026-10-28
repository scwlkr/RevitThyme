using System.Collections.Immutable;
using System.Diagnostics;
using RevitThyme.Core;

namespace RevitThyme.Addin;

internal sealed record NativeTimberKitProgress(TimberNativeKitRequest Request, TimberNativeKitPlan? Plan,
    Target After, ImmutableArray<TimberNativeElement> Created, ImmutableArray<TimberNativeFile> Files,
    string? PreparedHash, bool Committed, bool Readback, Stopwatch Clock);

internal interface INativeTimberKitRecoveryRuntime
{
    bool HasGroup { get; }
    bool GroupCommitted { get; }
    bool GroupStarted { get; }
    bool GroupRolledBack { get; }
    bool RollBack();
    bool HasPreservation { get; }
    void RequireUnchanged(bool committed, CancellationToken cancellationToken);
    bool SourceActive { get; }
    string Fingerprint(TimberNativeKitPlan plan, Target target, IReadOnlyCollection<TimberNativeElement> owned, CancellationToken cancellationToken);
    Target CaptureTarget();
    IEnumerable<long> BeforeIds { get; }
    IEnumerable<long> CurrentIds { get; }
    string UniqueId(long id);
}

internal static class NativeTimberKitRecovery
{
    internal static TimberNativeKitReceipt Recover(INativeTimberKitRecoveryRuntime runtime, NativeTimberKitProgress progress, Exception error) =>
        new Recovery(runtime, progress, error).Execute();

    private sealed class Recovery(INativeTimberKitRecoveryRuntime runtime, NativeTimberKitProgress progress, Exception failure)
    {
        private Target after = progress.After;
        private ImmutableArray<TimberNativeElement> created = progress.Created;
        private Exception error = failure;
        private bool rolledBack, preserved;

        internal TimberNativeKitReceipt Execute()
        {
            // A confirmed commit wins a late cancellation; do not retry or roll it back.
            if (progress.Committed || runtime.GroupCommitted) return CommittedReceipt();
            VerifyRollback();
            CaptureUnconfirmed();
            return FailedReceipt();
        }

        private void VerifyRollback()
        {
            try
            {
                rolledBack = runtime.GroupStarted ? runtime.RollBack() : runtime.GroupRolledBack;
                if (runtime.HasPreservation) runtime.RequireUnchanged(false, CancellationToken.None);
                VerifySource();
                after = runtime.CaptureTarget();
            }
            catch (Exception verifyError) when (verifyError is not OutOfMemoryException)
            { error = new AggregateException(error, verifyError); }
        }

        private void VerifySource()
        {
            preserved = runtime.SourceActive;
            if (progress.Plan is not null && runtime.HasPreservation &&
                runtime.Fingerprint(progress.Plan, progress.Request.Target, [], CancellationToken.None) != progress.Plan.SourceFingerprint) preserved = false;
        }

        private void CaptureUnconfirmed()
        {
            if (rolledBack || !runtime.HasPreservation) return;
            try { created = FindUnconfirmed(); }
            catch (Exception inventoryError) when (inventoryError is not OutOfMemoryException)
            { error = new AggregateException(error, inventoryError); }
        }

        private ImmutableArray<TimberNativeElement> FindUnconfirmed()
        {
            var beforeIds = runtime.BeforeIds.ToHashSet();
            var known = created.ToDictionary(e => e.Id);
            foreach (long id in runtime.CurrentIds.Where(id => !beforeIds.Contains(id)))
                known.TryAdd(id, new(id, runtime.UniqueId(id), "UnconfirmedResource"));
            return known.Values.OrderBy(e => e.Id).ToImmutableArray();
        }

        private TimberNativeKitState FailureState()
        {
            if (!runtime.HasGroup) return preserved ? TimberNativeKitState.Rejected : TimberNativeKitState.Unconfirmed;
            return rolledBack && preserved ? TimberNativeKitState.RolledBack : TimberNativeKitState.Unconfirmed;
        }

        private TimberNativeKitReceipt CommittedReceipt() =>
            new(TimberNativeKitState.Committed, progress.Request.RunId, after, created, progress.Files,
                [new("native_postcommit_context", TimberSeverity.Warning, "Native kit committed; refresh source context: " + error.Message)],
                true, progress.Readback, progress.PreparedHash, progress.Clock.Elapsed.TotalMilliseconds, progress.Request.InputSealSha256);

        private TimberNativeKitReceipt FailedReceipt() =>
            new(FailureState(), progress.Request.RunId, after, created, progress.Files,
                [new(error is OperationCanceledException ? "cancelled" : "native_kit", TimberSeverity.Error, error.Message)],
                preserved, false, progress.PreparedHash, progress.Clock.Elapsed.TotalMilliseconds, progress.Request.InputSealSha256);
    }
}
