using System.Collections.Immutable;

namespace RevitThyme.Core;

public sealed record TimberReconciliationStart(Guid Id, Guid GenerationRunId, Target CurrentTarget);
public enum TimberNativeReconciliationState { Verified, Absent, Conflict, Unconfirmed }
public sealed record TimberNativeReconciliationRequest(Guid RunId, Guid GenerationRunId, Target CurrentTarget,
    string GenerationDirectory, string Directory, ImmutableArray<byte> PlanJson, ImmutableArray<byte> PreparedJson,
    TimberNativeKitReceipt CommitReceipt);
public sealed record TimberNativeReconciliationReceipt(TimberNativeReconciliationState State, Guid RunId,
    Guid GenerationRunId, Target BeforeTarget, Target AfterTarget, ImmutableArray<TimberNativeElement> ObservedElements,
    ImmutableArray<TimberDiagnostic> Diagnostics, bool SourcePreserved, bool NativeReadbackPassed,
    double? NativeMilliseconds = null);
public interface ITimberReconciliationQueue
{
    Task<TimberNativeReconciliationReceipt> ReconcileTimberAsync(TimberNativeReconciliationRequest request,
        CancellationToken cancellationToken = default);
}
public interface ITimberNativeReconciliationModel : IModel
{
    TimberNativeReconciliationReceipt ReconcileTimberKit(TimberNativeReconciliationRequest request, CancellationToken cancellationToken);
}
