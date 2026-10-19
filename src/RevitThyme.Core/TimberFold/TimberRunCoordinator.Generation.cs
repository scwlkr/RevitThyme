using System.Collections.Immutable;
using System.Text.Json;

namespace RevitThyme.Core;

public sealed partial class TimberRunCoordinator
{
    public TimberRunSummary Verify(Guid id)
    {
        lock (gate)
            if (entries.TryGetValue(id, out var entry) && !entry.Summary.Terminal) return entry.Summary;
        return store.Read(id);
    }

    public TimberRunSummary StartGeneration(TimberGenerationStart request)
    {
        if (request.Id == Guid.Empty || request.PreviewRunId == Guid.Empty)
            throw new OperationException("invalid_id", "Generation and preview IDs must be non-empty.");
        if (!store.Exists(request.PreviewRunId) || store.Read(request.PreviewRunId).State != TimberRunState.PreviewReady)
            throw new OperationException("preview_required", "Generation requires a retained verified preview.");
        var preview = store.ReadRequest(request.PreviewRunId);
        if (request.Target != preview.Target)
            throw new OperationException("stale_target", "Generation must use its preview's exact native target.");
        if (native is not ITimberFinalizationQueue)
            throw new OperationException("generation_unavailable", "Native kit finalization is unavailable.");
        return StartRun(new(request.Id, request.Target, preview.Profile, preview.Scope, request.PreviewRunId));
    }

    private async Task ExecuteGeneration(Entry entry)
    {
        bool submitted = false;
        try
        {
            var request = entry.Request;
            var ct = entry.Cancellation.Token;
            var parent = store.Read(request.PreviewRunId!.Value);
            if (parent.State != TimberRunState.PreviewReady)
                throw new OperationException("preview_required", "Preview evidence no longer verifies.");
            Set(entry, TimberRunState.Capturing, "Revalidating native source before generation");
            var captured = await native.CaptureTimberAsync(new(request.Id, request.Target, request.Profile, request.Scope), ct)
                .WaitAsync(ct).ConfigureAwait(false);
            entry.NativeExtractionMilliseconds = ValidMeasurement(captured.NativeMilliseconds);
            ct.ThrowIfCancellationRequested();
            if (captured.Context.Target != request.Target || captured.Decode().DocumentFingerprint != parent.SourceFingerprint)
                throw new OperationException("source_changed", "Native source differs from the verified preview.");
            store.WriteNew(request.Id, "snapshot.json", captured.SnapshotJson.AsSpan());
            var preview = TimberPreviewPipeline.Compute(request, captured, store,
                (state, stage) => Set(entry, state, stage), ct);
            lock (gate) entry.Summary = entry.Summary with { SourceFingerprint = preview.Fingerprint,
                PanelCount = preview.PanelCount, SheetCount = preview.SheetCount };
            if (preview.Inputs is not { } inputs)
            { Set(entry, TimberRunState.Failed, "Generation geometry blocked", preview.Diagnostics); return; }
            Set(entry, TimberRunState.Guides, "Preparing source-mapped stock and assembly guides");
            var guides = TimberGuides.Stage(Path.Combine(store.DirectoryFor(request.Id), "guides"),
                inputs.Fitted, inputs.Stock, request.Profile, ct);
            if (!guides.Staged)
            { Set(entry, TimberRunState.Failed, "Guide generation blocked", guides.Diagnostics); return; }
            var plan = new TimberNativeKitPlan(1, request.Id, request.PreviewRunId.Value, captured.Context,
                preview.Fingerprint, request.Profile, inputs.Scope, captured.SnapshotJson,
                inputs.Fitted, inputs.Stock, guides, inputs.Cutting.Files);
            store.WriteNew(request.Id, "native-plan.json", JsonSerializer.SerializeToUtf8Bytes(plan, Protocol.Json));
            var verifiedInputs = TimberGenerationFiles.CaptureInputs(store.DirectoryFor(request.Id), request, plan, inputs.Cutting);
            var sealedInputs = TimberGenerationFiles.SealInputs(store, request, verifiedInputs);
            ct.ThrowIfCancellationRequested();
            Set(entry, TimberRunState.Finalizing, "Creating and verifying native kit in one Undo group");
            submitted = true;
            // A mutation must return its definitive receipt even if cancellation arrives.
            // WaitAsync(ct) is safe for capture, but would abandon a possibly committed kit here.
            var receipt = await ((ITimberFinalizationQueue)native).FinalizeTimberAsync(new(request.Id, request.Target,
                store.DirectoryFor(request.Id), JsonSerializer.SerializeToUtf8Bytes(plan, Protocol.Json).ToImmutableArray(), sealedInputs.Sha256), ct).ConfigureAwait(false);
            lock (gate) entry.Summary = entry.Summary with { NativeCommitted = receipt.State == TimberNativeKitState.Committed };
            entry.NativeFinalizationMilliseconds = ValidMeasurement(receipt.NativeMilliseconds);
            store.WriteNew(request.Id, "native-receipt.json", JsonSerializer.SerializeToUtf8Bytes(receipt, Protocol.Json));
            if (receipt.RunId != request.Id || receipt.State == TimberNativeKitState.Unconfirmed || !receipt.SourcePreserved)
            { Set(entry, TimberRunState.Unconfirmed, "Native completion requires reconciliation", receipt.Diagnostics); return; }
            if (receipt.State is TimberNativeKitState.Rejected or TimberNativeKitState.RolledBack)
            {
                Set(entry, ct.IsCancellationRequested ? TimberRunState.Cancelled : TimberRunState.Failed,
                    "Native kit was not committed", receipt.Diagnostics); return;
            }
            // Cancellation after a confirmed commit cannot turn this into a cancelled run.
            store.PublishNew(request.Id, "kit.json", TimberGenerationFiles.Manifest(entry.Summary, receipt, sealedInputs.Files));
            Set(entry, TimberRunState.KitReady, "Digital kit verified; physical checks remain outstanding", receipt.Diagnostics);
        }
        catch (OperationCanceledException) when (!submitted)
        { Set(entry, TimberRunState.Cancelled, "Cancelled before native finalization"); }
        catch (Exception error)
        {
            Set(entry, submitted ? TimberRunState.Unconfirmed : TimberRunState.Failed,
                submitted ? "Native completion requires reconciliation" : "Generation failed",
                [new(error is OperationException operation ? operation.Code : "generation_failed", TimberSeverity.Error, error.Message)]);
        }
    }
}
