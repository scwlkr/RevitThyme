using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitThyme.Core;

namespace RevitThyme.Addin;

internal static partial class NativeTimberKit
{
    private sealed class RecoveryRuntime(UIApplication app, RevitModel model, Document source,
        NativeTimberKitPreservation? preservation, TransactionGroup? group) : INativeTimberKitRecoveryRuntime
    {
        public bool HasGroup => group is not null;
        public bool GroupCommitted => group?.GetStatus() == TransactionStatus.Committed;
        public bool GroupStarted => group?.GetStatus() == TransactionStatus.Started;
        public bool GroupRolledBack => group?.GetStatus() == TransactionStatus.RolledBack;
        public bool RollBack() => group?.RollBack() == TransactionStatus.RolledBack;
        public bool HasPreservation => preservation is not null;
        public void RequireUnchanged(bool committed, CancellationToken cancellationToken) => preservation?.RequireUnchanged(committed, cancellationToken);
        public bool SourceActive => source.Equals(app.ActiveUIDocument?.Document);
        public string Fingerprint(TimberNativeKitPlan plan, Target target, IReadOnlyCollection<TimberNativeElement> owned, CancellationToken cancellationToken) =>
            NativeTimberKitPreservation.OriginalFingerprint(source, plan, target, owned, cancellationToken);
        public Target CaptureTarget() => model.Capture().Target;
        public IEnumerable<long> BeforeIds => preservation is { } monitor ? monitor.Before.Select(e => e.Id) : [];
        public IEnumerable<long> CurrentIds => NativeTimberKitPreservation.Ids(source);
        public string UniqueId(long id) => source.GetElement(new ElementId(id)).UniqueId;
    }
}
