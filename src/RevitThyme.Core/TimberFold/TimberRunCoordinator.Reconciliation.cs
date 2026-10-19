using System.Text.Json;
using System.Security.Cryptography;
using System.Collections.Immutable;

namespace RevitThyme.Core;

public sealed partial class TimberRunCoordinator
{
    public TimberRunSummary StartReconciliation(TimberReconciliationStart request)
    {
        if (request.Id == Guid.Empty || request.GenerationRunId == Guid.Empty || request.Id == request.GenerationRunId || request.CurrentTarget is null)
            throw new OperationException("invalid_id", "Reconciliation needs a new non-empty handle, generation ID and current target.");
        if (native is not ITimberReconciliationQueue) throw new OperationException("reconciliation_unavailable", "Native reconciliation is unavailable.");
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(request, Protocol.Json);
        if (bytes.Length > 16384) throw new OperationException("request_limit", "Reconciliation request exceeds 16 KiB.");
        var summary = new TimberRunSummary(request.Id, request.CurrentTarget, TimberRunState.Accepted,
            "Reconciliation accepted; existing native documentation will only be read", store.DirectoryFor(request.Id), null, 0, 0, 0, [], GenerationRunId: request.GenerationRunId);
        return StartOwned(new(request.Id, request.CurrentTarget, new()), summary, bytes, request);
    }
    private async Task ExecuteReconciliation(Entry entry)
    {
        try
        {
            var request = entry.Reconciliation!; var ct = entry.Cancellation.Token;
            Set(entry, TimberRunState.Reconciling, "Verifying retained generation evidence");
            ct.ThrowIfCancellationRequested();
            lock (gate)
                if (entries.TryGetValue(request.GenerationRunId, out var active) && !active.Summary.Terminal)
                    throw new OperationException("reconciliation_required", "Wait for the original generation to finish before reconciling it.");
            var inputs = TimberGenerationFiles.ReadReconciliation(store, request.GenerationRunId);
            var original = inputs.Summary.Target; var current = request.CurrentTarget;
            if (original.ProcessId != current.ProcessId || original.ProcessStart != current.ProcessStart ||
                original.SessionId != current.SessionId || original.DocumentId != current.DocumentId)
                throw new OperationException("reconciliation_target", "Initial recovery requires the original native process/session/document; capture its current view separately.");
            lock (gate) entry.Summary = entry.Summary with { SourceFingerprint = inputs.Summary.SourceFingerprint,
                PanelCount = inputs.Summary.PanelCount, SheetCount = inputs.Summary.SheetCount, NativeCommitted = true, PreviewRunId = inputs.Summary.PreviewRunId };
            var verified = TimberReconciliationFiles.Capture(store, request.Id, request.GenerationRunId, inputs.Files);
            ct.ThrowIfCancellationRequested();
            var nativeRequest = new TimberNativeReconciliationRequest(request.Id, request.GenerationRunId, request.CurrentTarget,
                store.DirectoryFor(request.GenerationRunId), store.DirectoryFor(request.Id), inputs.PlanJson, inputs.PreparedJson, inputs.Receipt);
            var receipt = await ((ITimberReconciliationQueue)native).ReconcileTimberAsync(nativeRequest, ct).WaitAsync(ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            entry.NativeReconciliationMilliseconds = ValidMeasurement(receipt.NativeMilliseconds);
            byte[] receiptBytes = JsonSerializer.SerializeToUtf8Bytes(receipt, Protocol.Json);
            store.WriteNew(request.Id, "native-reconciliation.json", receiptBytes);
            verified = verified.Add(new("native-reconciliation.json", Convert.ToHexStringLower(SHA256.HashData(receiptBytes)), receiptBytes.Length))
                .OrderBy(f => f.Name, StringComparer.Ordinal).ToImmutableArray();
            TimberReconciliationFiles.RequireVerified(nativeRequest, receipt);
            TimberGenerationFiles.RequireRecoveryInventory(store.DirectoryFor(request.GenerationRunId), inputs.Files);
            ct.ThrowIfCancellationRequested();
            store.PublishNew(request.Id, "kit.json", TimberReconciliationFiles.Manifest(store, entry.Summary, verified));
            Set(entry, TimberRunState.KitReady, "Existing native kit reconciled; original generation retained; physical checks outstanding", receipt.Diagnostics);
        }
        catch (OperationCanceledException) { Set(entry, TimberRunState.Cancelled, "Read-only reconciliation cancelled; original generation unchanged"); }
        catch (Exception error)
        {
            Set(entry, TimberRunState.Unconfirmed, "Reconciliation remains unconfirmed; original generation unchanged",
                [new(error is OperationException operation ? operation.Code : "reconciliation_evidence", TimberSeverity.Error, error.Message)]);
        }
    }
}
