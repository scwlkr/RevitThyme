using System.Collections.Immutable;

namespace RevitThyme.Core;

public enum TimberNativeKitState { Committed, RolledBack, Rejected, Unconfirmed }
public sealed record TimberNativeElement(long Id, string UniqueId, string Role, string? PanelId = null);
public sealed record TimberNativeFile(string RelativePath, string Sha256, long Bytes, string Role);
// The coordinator owns the directory and encoded plan. Clients supply only run IDs.
public sealed record TimberNativeKitRequest(Guid RunId, Target Target, string Directory, ImmutableArray<byte> PlanJson,
    string? InputSealSha256 = null);
public sealed record TimberNativeKitPlan(int Schema, Guid RunId, Guid PreviewRunId, Snapshot SourceContext,
    string SourceFingerprint, TimberFoldProfile Profile, TimberScope Scope, ImmutableArray<byte> SourceSnapshotJson,
    TimberFittedDraft Fitted, TimberStockPlan Stock, TimberGuideStaging Guides, ImmutableArray<TimberCutFile> CutFiles);
public sealed record TimberNativeKitReceipt(TimberNativeKitState State, Guid RunId, Target AfterTarget,
    ImmutableArray<TimberNativeElement> CreatedElements, ImmutableArray<TimberNativeFile> Files,
    ImmutableArray<TimberDiagnostic> Diagnostics, bool SourcePreserved, bool NativeReadbackPassed,
    string? PreparedReceiptSha256 = null, double? NativeMilliseconds = null, string? InputSealSha256 = null);

public interface ITimberFinalizationQueue
{
    Task<TimberNativeKitReceipt> FinalizeTimberAsync(TimberNativeKitRequest request, CancellationToken cancellationToken = default);
}
public interface ITimberNativeKitModel : IModel
{
    TimberNativeKitReceipt FinalizeTimberKit(TimberNativeKitRequest request, CancellationToken cancellationToken);
}
