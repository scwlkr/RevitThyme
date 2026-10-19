using RevitThyme.Core;

namespace RevitThyme.Addin;

public sealed partial class RevitModel
{
    public TimberNativeReconciliationReceipt ReconcileTimberKit(TimberNativeReconciliationRequest request, CancellationToken cancellationToken)
        => NativeTimberReconciliation.Check(app, this, request, cancellationToken);

    public TimberNativeKitReceipt FinalizeTimberKit(TimberNativeKitRequest request, CancellationToken cancellationToken)
    {
        var context = Capture();
        if (context.Target != request.Target || !context.CanModify)
            return new(TimberNativeKitState.Rejected, request.RunId, context.Target, [], [],
                [new("stale_target", TimberSeverity.Error, "Refresh the exact source document/view before generation.")], true, false);
        return NativeTimberKit.Finalize(app, this, request, cancellationToken);
    }
}
