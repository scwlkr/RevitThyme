using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;

namespace RevitThyme.Core;

internal static class TimberReconciliationFiles
{
    private sealed record Inputs(int Schema, Guid GenerationRunId, ImmutableArray<TimberCutFile> Files);
    private sealed record Kit(int Schema, string State, Guid RunId, Guid GenerationRunId, Target CurrentTarget,
        string SourceFingerprint, int PanelCount, int SheetCount, bool ManufacturingReady, bool PhysicalChecksOutstanding,
        ImmutableArray<TimberCutFile> Files);
    internal static ImmutableArray<TimberCutFile> Capture(TimberRunStore store, Guid id, Guid generation, ImmutableArray<TimberCutFile> files)
    {
        store.WriteNew(id, "recovery-inputs.json", JsonSerializer.SerializeToUtf8Bytes(new Inputs(1, generation, files), Protocol.Json));
        _ = TimberRunProvenance.Read(store.DirectoryFor(id));
        var owned = Inventory(store.DirectoryFor(id));
        if (!owned.Select(f => f.Name).SequenceEqual(new[] { "producer.json", "reconciliation-request.json", "recovery-inputs.json" }))
            throw new InvalidDataException("Unexpected reconciliation input inventory.");
        return owned;
    }

    internal static void RequireVerified(TimberNativeReconciliationRequest request, TimberNativeReconciliationReceipt receipt)
    {
        if (receipt.State == TimberNativeReconciliationState.Absent)
            throw new OperationException("native_kit_absent", "Committed native documentation is absent or has changed identities. Original evidence is retained; reconciliation does not recreate documentation.");
        if (receipt.State != TimberNativeReconciliationState.Verified || receipt.RunId != request.RunId ||
            receipt.GenerationRunId != request.GenerationRunId || receipt.BeforeTarget != request.CurrentTarget || receipt.AfterTarget != request.CurrentTarget ||
            !receipt.SourcePreserved || !receipt.NativeReadbackPassed || receipt.Diagnostics.IsDefault ||
            receipt.Diagnostics.Any(d => d.Severity == TimberSeverity.Error) || receipt.ObservedElements.IsDefault ||
            !receipt.ObservedElements.OrderBy(e => e.Id).SequenceEqual(request.CommitReceipt.CreatedElements.OrderBy(e => e.Id)) ||
            receipt.NativeMilliseconds is { } ms && (!double.IsFinite(ms) || ms < 0))
            throw new OperationException("native_reconciliation", "Current native documentation could not be confirmed against the committed plan.");
    }
    internal static byte[] Manifest(TimberRunStore store, TimberRunSummary summary, ImmutableArray<TimberCutFile> verified)
    {
        var request = Read<TimberReconciliationStart>(Path.Combine(summary.Directory, "reconciliation-request.json"), 16384);
        CheckInputs(store, summary.Directory, request.GenerationRunId);
        var receipt = Read<TimberNativeReconciliationReceipt>(Path.Combine(summary.Directory, "native-reconciliation.json"), 16777216);
        if (receipt.RunId != request.Id || receipt.GenerationRunId != request.GenerationRunId || receipt.State != TimberNativeReconciliationState.Verified ||
            receipt.BeforeTarget != request.CurrentTarget || receipt.AfterTarget != request.CurrentTarget || !receipt.SourcePreserved || !receipt.NativeReadbackPassed)
            throw new InvalidDataException("Native reconciliation receipt differs from its owned request.");
        var files = Inventory(summary.Directory);
        if (!files.SequenceEqual(verified))
            throw new InvalidDataException("Reconciliation evidence inventory changed.");
        return JsonSerializer.SerializeToUtf8Bytes(new Kit(1, "ReconciledDigitalKit", summary.RunId, request.GenerationRunId,
            request.CurrentTarget, summary.SourceFingerprint!, summary.PanelCount, summary.SheetCount, false, true, files), Protocol.Json);
    }
    internal static void Verify(TimberRunStore store, TimberReconciliationStart request, TimberRunSummary summary)
    {
        var kit = Read<Kit>(Path.Combine(summary.Directory, "kit.json"), 16777216);
        if (kit.Schema != 1 || kit.State != "ReconciledDigitalKit" || kit.RunId != request.Id || kit.GenerationRunId != request.GenerationRunId ||
            kit.CurrentTarget != request.CurrentTarget || kit.SourceFingerprint != summary.SourceFingerprint || kit.PanelCount != summary.PanelCount ||
            kit.SheetCount != summary.SheetCount || kit.ManufacturingReady || !kit.PhysicalChecksOutstanding || !summary.NativeCommitted ||
            kit.Files.IsDefault || !kit.Files.SequenceEqual(Inventory(summary.Directory)))
            throw new InvalidDataException("Reconciled kit identity or evidence changed.");
        _ = TimberRunProvenance.Read(summary.Directory);
        CheckInputs(store, summary.Directory, request.GenerationRunId);
    }
    private static void CheckInputs(TimberRunStore store, string directory, Guid generation)
    {
        var inputs = Read<Inputs>(Path.Combine(directory, "recovery-inputs.json"), 16777216);
        if (inputs.Schema != 1 || inputs.GenerationRunId != generation || inputs.Files.IsDefaultOrEmpty)
            throw new InvalidDataException("Original recovery input inventory is missing.");
        TimberGenerationFiles.RequireRecoveryInventory(store.DirectoryFor(generation), inputs.Files);
    }
    private static ImmutableArray<TimberCutFile> Inventory(string directory)
    {
        for (var parent = new DirectoryInfo(directory); parent is not null; parent = parent.Parent)
            if ((parent.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Reconciliation paths cannot be redirected.");
        var files = new List<TimberCutFile>();
        foreach (string path in Directory.EnumerateFileSystemEntries(directory))
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0) throw new InvalidDataException("Unexpected reconciliation directory or redirected file.");
            string name = Path.GetFileName(path);
            if (name is "kit.json" or "run.json" or "diagnostics.json" or "run-timings.json") continue;
            using var stream = File.OpenRead(path);
            if (stream.Length <= 0 || stream.Length > 16777216) throw new InvalidDataException("Reconciliation file exceeds its bound.");
            files.Add(new(name, Convert.ToHexStringLower(SHA256.HashData(stream)), stream.Length));
            if (files.Count > 8) throw new InvalidDataException("Reconciliation evidence exceeds its inventory bound.");
        }
        return files.OrderBy(f => f.Name, StringComparer.Ordinal).ToImmutableArray();
    }
    private static T Read<T>(string path, long maximum)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length <= 0 || stream.Length > maximum) throw new InvalidDataException("Reconciliation record exceeds its bound.");
        return JsonSerializer.Deserialize<T>(stream, Protocol.Json) ?? throw new InvalidDataException("Reconciliation record is missing.");
    }
}
