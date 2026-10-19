using System.Collections.Immutable;
using System.Text.Json;

namespace RevitThyme.Core;

internal static partial class TimberGenerationFiles
{
    private sealed record InputSeal(int Schema, Guid RunId, Guid PreviewRunId, ImmutableArray<TimberCutFile> Files);
    internal sealed record ReconciliationInputs(TimberRunSummary Summary, TimberNativeKitPlan Plan,
        ImmutableArray<byte> PlanJson, ImmutableArray<byte> PreparedJson, TimberNativeKitReceipt Receipt,
        ImmutableArray<TimberCutFile> Files);

    private static void RequireInputSealBinding(string directory, TimberNativeKitReceipt receipt)
    {
        var seal = Describe(directory, "generation-inputs.json");
        if (receipt.InputSealSha256 != seal.Sha256) throw new InvalidDataException("Committed receipt binds different verified generation inputs.");
        using var prepared = JsonDocument.Parse(PreparedBytes(directory));
        if (prepared.RootElement.GetProperty("schema").GetInt32() != 2 ||
            prepared.RootElement.GetProperty("runId").GetGuid() != receipt.RunId ||
            prepared.RootElement.GetProperty("inputSealSha256").GetString() != seal.Sha256)
            throw new InvalidDataException("Prepared native evidence does not bind the verified generation inputs.");
    }

    private static byte[] PreparedBytes(string directory)
    {
        using var stream = File.OpenRead(Path.Combine(directory, "native-prepared.json"));
        if (stream.Length <= 0 || stream.Length > 33554432) throw new InvalidDataException("Prepared evidence exceeds its bounded recovery limit.");
        byte[] bytes = new byte[(int)stream.Length]; stream.ReadExactly(bytes); return bytes;
    }

    internal static (ImmutableArray<TimberCutFile> Files, string Sha256) SealInputs(TimberRunStore store,
        TimberRunStart request, ImmutableArray<TimberCutFile> files)
    {
        store.WriteNew(request.Id, "generation-inputs.json", JsonSerializer.SerializeToUtf8Bytes(
            new InputSeal(1, request.Id, request.PreviewRunId!.Value, files), Protocol.Json));
        var seal = Describe(store.DirectoryFor(request.Id), "generation-inputs.json");
        return (files.Add(new(seal.Name, seal.Sha256, seal.Bytes)).OrderBy(f => f.Name, StringComparer.Ordinal).ToImmutableArray(), seal.Sha256);
    }

    internal static ReconciliationInputs ReadReconciliation(TimberRunStore store, Guid generation)
    {
        var request = store.ReadRequest(generation); var summary = store.Read(generation);
        if (request.PreviewRunId is null || summary.GenerationRunId is not null || summary.State is not (TimberRunState.Unconfirmed or TimberRunState.KitReady))
            throw new OperationException("reconciliation_required", "Reconciliation requires a retained completed or unconfirmed generation.");
        string directory = store.DirectoryFor(generation);
        var receipt = Read<TimberNativeKitReceipt>(Path.Combine(directory, "native-receipt.json"));
        // A prepared record or present IDs alone cannot prove native assimilation.
        ValidateReceipt(summary, receipt);
        var seal = Read<InputSeal>(Path.Combine(directory, "generation-inputs.json"));
        if (seal.Schema != 1 || seal.RunId != generation || seal.PreviewRunId != request.PreviewRunId ||
            seal.Files.IsDefaultOrEmpty || seal.Files.Any(f => f is null || f.Name == "generation-inputs.json") ||
            !seal.Files.Select(f => f.Name).SequenceEqual(seal.Files.Select(f => f.Name).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal)))
            throw new InvalidDataException("Verified generation input seal is missing or invalid.");
        var sealFile = Describe(directory, "generation-inputs.json");
        if (receipt.InputSealSha256 != sealFile.Sha256) throw new InvalidDataException("Committed receipt does not bind the verified generation inputs.");
        byte[] preparedBytes = PreparedBytes(directory);
        using var prepared = JsonDocument.Parse(preparedBytes);
        if (prepared.RootElement.GetProperty("schema").GetInt32() != 2 ||
            prepared.RootElement.GetProperty("runId").GetGuid() != generation ||
            prepared.RootElement.GetProperty("inputSealSha256").GetString() != sealFile.Sha256)
            throw new InvalidDataException("Prepared native evidence does not bind the verified generation inputs.");
        var inputs = seal.Files.Add(new(sealFile.Name, sealFile.Sha256, sealFile.Bytes)).OrderBy(f => f.Name, StringComparer.Ordinal).ToImmutableArray();
        // This checks the original exact pre-dispatch inventory, output hashes, prepared and committed receipts.
        _ = Manifest(summary, receipt, inputs);
        _ = TimberRunProvenance.Read(directory);
        using var planStream = File.OpenRead(Path.Combine(directory, "native-plan.json"));
        if (planStream.Length <= 0 || planStream.Length > 134217728) throw new InvalidDataException("Native reconciliation plan exceeds its bound.");
        byte[] planBytes = new byte[(int)planStream.Length]; planStream.ReadExactly(planBytes);
        var plan = JsonSerializer.Deserialize<TimberNativeKitPlan>(planBytes, Protocol.Json) ?? throw new InvalidDataException("Native plan is missing.");
        if (plan.Schema != 1 || plan.RunId != generation || plan.PreviewRunId != request.PreviewRunId ||
            plan.SourceContext.Target != request.Target || plan.SourceFingerprint != summary.SourceFingerprint ||
            plan.Profile != request.Profile || plan.Fitted.Draft.Panels.Length != summary.PanelCount || plan.Stock.SheetCount != summary.SheetCount)
            throw new InvalidDataException("Sealed native plan differs from its generation identity.");
        return new(summary with { NativeCommitted = true }, plan, planBytes.ToImmutableArray(), preparedBytes.ToImmutableArray(), receipt, RecoveryInventory(directory));
    }

    internal static ImmutableArray<TimberCutFile> RecoveryInventory(string directory) => Inventory(directory)
        .Concat(new[] { "run.json", "diagnostics.json", "run-timings.json", "kit.json" }.Where(name => File.Exists(Path.Combine(directory, name))))
        .Order(StringComparer.Ordinal).Select(name => { var f = Describe(directory, name); return new TimberCutFile(f.Name, f.Sha256, f.Bytes); }).ToImmutableArray();

    internal static void RequireRecoveryInventory(string directory, ImmutableArray<TimberCutFile> files)
    {
        if (!RecoveryInventory(directory).SequenceEqual(files)) throw new InvalidDataException("Original generation evidence changed during or after reconciliation.");
    }
}
