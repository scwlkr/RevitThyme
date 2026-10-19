using System.Collections.Immutable;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitThyme.Core;

namespace RevitThyme.Addin;

// Observe an already committed kit. This path never opens a transaction, imports,
// exports, creates documents/elements, changes the active view or saves a model.
internal static class NativeTimberReconciliation
{
    internal static TimberNativeReconciliationReceipt Check(UIApplication app, RevitModel model,
        TimberNativeReconciliationRequest request, CancellationToken ct)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var before = model.Capture();
        var observed = ImmutableArray<TimberNativeElement>.Empty;
        var source = app.ActiveUIDocument?.Document;
        NativeTimberKitPreservation? preservation = null;
        try
        {
            ct.ThrowIfCancellationRequested();
            if (source is null || before.Target != request.CurrentTarget)
                throw new InvalidDataException("Current document/view/revision differs from the reconciliation target.");
            if (request.RunId == Guid.Empty || request.GenerationRunId == Guid.Empty ||
                request.CommitReceipt.State != TimberNativeKitState.Committed || request.CommitReceipt.RunId != request.GenerationRunId ||
                request.CommitReceipt.InputSealSha256 is not { Length: 64 } || request.PreparedJson.IsDefaultOrEmpty ||
                request.PreparedJson.Length > 33554432 ||
                Convert.ToHexStringLower(SHA256.HashData(request.PreparedJson.AsSpan())) != request.CommitReceipt.PreparedReceiptSha256)
                throw new InvalidDataException("Reconciliation requires definitive committed, sealed native evidence.");
            var plan = NativeTimberKitFiles.Read(new(request.GenerationRunId,
                JsonSerializer.Deserialize<TimberNativeKitPlan>(request.PlanJson.AsSpan(), Protocol.Json)!.SourceContext.Target,
                request.GenerationDirectory, request.PlanJson, request.CommitReceipt.InputSealSha256), ct);
            using var prepared = JsonDocument.Parse(request.PreparedJson.AsMemory());
            var proof = prepared.RootElement;
            if (proof.GetProperty("schema").GetInt32() != 2 || proof.GetProperty("runId").GetGuid() != request.GenerationRunId ||
                proof.GetProperty("inputSealSha256").GetString() != request.CommitReceipt.InputSealSha256 ||
                !string.Equals(proof.GetProperty("sourcePath").GetString(), source.PathName, StringComparison.OrdinalIgnoreCase) ||
                NativeTimberKitFiles.Hash(Path.Combine(request.GenerationDirectory, "native-prepared.json")) != request.CommitReceipt.PreparedReceiptSha256)
                throw new InvalidDataException("Prepared native proof differs from the exact source and committed input seal.");
            var original = plan.SourceContext.Target; var current = before.Target;
            if (original.ProcessId != current.ProcessId || original.ProcessStart != current.ProcessStart ||
                original.SessionId != current.SessionId || original.DocumentId != current.DocumentId)
                throw new InvalidDataException("Reconciliation cannot retarget another process, session or document.");
            preservation = new(app.Application, source, ct);
            var existing = request.CommitReceipt.CreatedElements.Where(e => source.GetElement(new ElementId(e.Id)) is { } native &&
                native.UniqueId == e.UniqueId).ToImmutableArray();
            observed = existing;
            if (existing.Length != request.CommitReceipt.CreatedElements.Length)
            {
                preservation.RequireUnchanged(false, ct);
                var afterAbsent = model.Capture();
                if (afterAbsent.Target != before.Target || afterAbsent.IsModified != before.IsModified)
                    throw new InvalidDataException("Native context changed while checking absent documentation.");
                return new(TimberNativeReconciliationState.Absent, request.RunId, request.GenerationRunId,
                    before.Target, afterAbsent.Target, observed,
                    [new("native_kit_absent", TimberSeverity.Error, "Some committed native elements are absent or have different identities. Reconciliation will not recreate them.")],
                    true, false, clock.Elapsed.TotalMilliseconds);
            }
            RequireCatalog(proof.GetProperty("beforeCatalog"), preservation.Before, "original", ct);
            RequireCatalog(proof.GetProperty("createdCatalog"), preservation.Before, "created", ct);
            var createdCatalog = proof.GetProperty("createdCatalog").EnumerateArray().Select(e => e.GetProperty("id").GetInt64()).Order().ToArray();
            if (!createdCatalog.SequenceEqual(existing.Select(e => e.Id).Order()))
                throw new InvalidDataException("Prepared content catalog does not account for every committed native element.");
            foreach (var element in existing.Where(e => e.Role is "CutView" or "CutImport" or "ReferenceView" or "ReferenceImport" or "PartLabel" or "GuideLabel" or "NativeSheet" or "Viewport" or "TextType"))
            {
                ct.ThrowIfCancellationRequested();
                if (!NativeTimberKitOwnership.Matches(source.GetElement(new ElementId(element.Id)), request.GenerationRunId))
                    throw new InvalidDataException("A committed documentation ownership tag is absent or changed.");
            }
            NativeTimberReconciliationDocuments.Check(source, plan, request.GenerationDirectory, proof, existing, ct);
            if (NativeTimberKitPreservation.OriginalFingerprint(source, plan, original, existing, ct) != plan.SourceFingerprint)
                throw new InvalidDataException("Current source geometry differs from the committed generation.");
            preservation.RequireUnchanged(false, ct);
            var after = model.Capture();
            if (!source.Equals(app.ActiveUIDocument?.Document) || after.Target != before.Target || after.IsModified != before.IsModified)
                throw new InvalidDataException("Native source/context changed during read-only reconciliation.");
            return new(TimberNativeReconciliationState.Verified, request.RunId, request.GenerationRunId,
                before.Target, after.Target, observed, [], true, true, clock.Elapsed.TotalMilliseconds);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            bool preserved = false; var after = before.Target;
            try
            {
                preservation?.RequireUnchanged(false, CancellationToken.None);
                var current = model.Capture(); after = current.Target;
                preserved = preservation is not null && source is not null && source.Equals(app.ActiveUIDocument?.Document) &&
                    current.Target == before.Target && current.IsModified == before.IsModified;
            }
            catch (Exception checkError) when (checkError is not OutOfMemoryException) { error = new AggregateException(error, checkError); }
            return new(preserved ? TimberNativeReconciliationState.Conflict : TimberNativeReconciliationState.Unconfirmed,
                request.RunId, request.GenerationRunId, before.Target, after, observed,
                [new(error is OperationCanceledException ? "cancelled" : "native_reconciliation", TimberSeverity.Error, error.Message)],
                preserved, false, clock.Elapsed.TotalMilliseconds);
        }
        finally { preservation?.Dispose(); }
    }

    private static void RequireCatalog(JsonElement expected, NativeTimberKitPreservation.ElementStamp[] actual, string name, CancellationToken ct)
    {
        var rows = expected.EnumerateArray().ToArray();
        if (rows.Length == 0 || rows.Length > 500000 || rows.Select(e => e.GetProperty("id").GetInt64()).Distinct().Count() != rows.Length)
            throw new InvalidDataException("Prepared " + name + " catalog is empty or ambiguous.");
        var current = actual.ToDictionary(e => e.Id);
        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();
            if (!current.TryGetValue(row.GetProperty("id").GetInt64(), out var value) || value.UniqueId != row.GetProperty("uniqueId").GetString() ||
                value.ContentHash != row.GetProperty("contentHash").GetString())
                throw new InvalidDataException("A committed " + name + " element identity or parameter/content hash changed.");
        }
    }
}
