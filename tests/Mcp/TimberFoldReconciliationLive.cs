using System.Security.Cryptography;
using System.Text.Json;
using RevitThyme.Core;

namespace RevitThyme.McpTests;

internal static class TimberFoldReconciliationLive
{
    internal sealed record InventoryEntry(string Name, bool Directory, long Bytes, string? Sha256, long LastWriteUtcTicks);
    internal sealed record Evidence(Guid RecoveryRunId, TimberRunSummary Recovery, TimberRunSummary Verified,
        Snapshot Before, Snapshot After, TimberNativeReconciliationReceipt NativeReceipt,
        InventoryEntry[] GenerationBefore, InventoryEntry[] GenerationAfter);

    internal static async Task<Evidence> Run(TimberRunSummary generation, Guid recoveryRunId, Snapshot expectedContext,
        string original, string originalHash,
        Func<string, Dictionary<string, object?>?, CancellationToken, Task<JsonElement>> call, CancellationToken lifetime)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        budget.CancelAfter(TimeSpan.FromSeconds(60)); var ct = budget.Token;
        string generationDirectory = OwnedRun(generation.RunId, generation.Directory);
        var context = await call("get_context", null, ct);
        var before = Property(Property(context, "result"), "snapshot").Deserialize<Snapshot>(Core.Protocol.Json)!;
        Program.Check("native MCP reconciliation captures the exact current generated source context", before == expectedContext);
        var inventoryBefore = Inventory(generationDirectory, ct);
        var committed = JsonSerializer.Deserialize<TimberNativeKitReceipt>(File.ReadAllBytes(Path.Combine(generationDirectory, "native-receipt.json")), Core.Protocol.Json)!;
        Program.Check("native MCP recovery inventory includes original timings and all committed evidence", inventoryBefore.Any(e => e.Name == "run-timings.json" && !e.Directory) &&
            inventoryBefore.Any(e => e.Name == "native-receipt.json" && !e.Directory) && committed.RunId == generation.RunId &&
            committed.State == TimberNativeKitState.Committed && recoveryRunId != generation.RunId && recoveryRunId != Guid.Empty);
        var accepted = await call("timberfold_reconcile", new() { ["captureId"] = Property(context, "captureId").GetString(),
            ["requestId"] = recoveryRunId.ToString(), ["generationRunId"] = generation.RunId.ToString() }, ct);
        var recovery = Property(accepted, "timberRun").Deserialize<TimberRunSummary>(Core.Protocol.Json)!;
        while (!recovery.Terminal)
        {
            await Task.Delay(100, ct);
            recovery = Property(await call("timberfold_status", new() { ["runId"] = recoveryRunId.ToString() }, ct), "timberRun")
                .Deserialize<TimberRunSummary>(Core.Protocol.Json)!;
        }
        Program.Check("native MCP reconciliation settles a separate verified recovery preserving its generation and physical gap", recovery is
            { State: TimberRunState.KitReady, NativeCommitted: true, NativeImportOutstanding: false, PhysicalChecksOutstanding: true, ManufacturingReady: false } &&
            recovery.RunId == recoveryRunId && recovery.GenerationRunId == generation.RunId && recovery.Target == before.Target &&
            recovery.PanelCount == generation.PanelCount && recovery.SheetCount == generation.SheetCount && recovery.SourceFingerprint == generation.SourceFingerprint);
        string recoveryDirectory = OwnedRun(recoveryRunId, recovery.Directory);
        Program.Check("native MCP recovery uses a separate evidence directory", !string.Equals(recoveryDirectory, generationDirectory, StringComparison.OrdinalIgnoreCase));
        var receipt = JsonSerializer.Deserialize<TimberNativeReconciliationReceipt>(File.ReadAllBytes(Path.Combine(recoveryDirectory, "native-reconciliation.json")), Core.Protocol.Json)!;
        Program.Check("native MCP reconciliation receipt confirms read-only native targets readback and exact original element identities", receipt is
            { State: TimberNativeReconciliationState.Verified, SourcePreserved: true, NativeReadbackPassed: true } &&
            receipt.RunId == recoveryRunId && receipt.GenerationRunId == generation.RunId && receipt.BeforeTarget == before.Target && receipt.AfterTarget == before.Target &&
            !receipt.ObservedElements.IsDefaultOrEmpty && !receipt.Diagnostics.Any(d => d.Severity == TimberSeverity.Error) &&
            receipt.ObservedElements.OrderBy(e => e.Id).SequenceEqual(committed.CreatedElements.OrderBy(e => e.Id)));
        var verified = Property(await call("timberfold_verify", new() { ["runId"] = recoveryRunId.ToString() }, ct), "timberRun")
            .Deserialize<TimberRunSummary>(Core.Protocol.Json)!;
        Program.Check("native MCP explicitly verifies the complete retained recovery handle", verified is
            { State: TimberRunState.KitReady, NativeCommitted: true, NativeImportOutstanding: false, PhysicalChecksOutstanding: true, ManufacturingReady: false } &&
            verified.RunId == recoveryRunId && verified.GenerationRunId == generation.RunId && verified.Target == recovery.Target &&
            verified.ElapsedMilliseconds == recovery.ElapsedMilliseconds);
        var after = Property(Property(await call("get_context", null, ct), "result"), "snapshot").Deserialize<Snapshot>(Core.Protocol.Json)!;
        Program.Check("native MCP reconciliation and verification preserve the complete current target and source modified state", after == before);
        var inventoryAfter = Inventory(generationDirectory, ct);
        Program.Check("native MCP recovery leaves every original generation file directory timing and byte hash unchanged", inventoryBefore.SequenceEqual(inventoryAfter));
        Program.Check("native MCP read-only recovery leaves both saved RVT bytes unchanged", Hash(before.DocumentPath, ct) == originalHash.ToLowerInvariant() &&
            Hash(original, ct) == originalHash.ToLowerInvariant());
        return new(recoveryRunId, recovery, verified, before, after, receipt, inventoryBefore, inventoryAfter);
    }

    private static string OwnedRun(Guid id, string directory)
    {
        string expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RevitThyme", "TimberFold", "runs", id.ToString("D"));
        if (!Path.IsPathFullyQualified(directory) || !string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)), expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Native MCP evidence must use the exact host-owned run directory.");
        return expected;
    }
    private static InventoryEntry[] Inventory(string root, CancellationToken ct)
    {
        var entries = new List<InventoryEntry> { new(".", true, 0, null, File.GetLastWriteTimeUtc(root).Ticks) };
        var folders = new Stack<(string Path, int Depth)>(); folders.Push((root, 0));
        long total = 0;
        while (folders.Count > 0)
        {
            ct.ThrowIfCancellationRequested(); var (folder, depth) = folders.Pop();
            if (depth > 32 || (File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Generation inventory exceeds its depth or redirects outside its owned run.");
            foreach (string path in System.IO.Directory.EnumerateFileSystemEntries(folder))
            {
                ct.ThrowIfCancellationRequested(); var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Generation evidence contains a redirected entry.");
                string name = Path.GetRelativePath(root, path).Replace('\\', '/'); bool directory = (attributes & FileAttributes.Directory) != 0;
                long bytes = directory ? 0 : new FileInfo(path).Length;
                total = checked(total + bytes);
                if (entries.Count >= 4096 || total > 1073741824) throw new InvalidDataException("Generation inventory exceeds 4096 entries or 1 GiB.");
                entries.Add(new(name, directory, bytes, directory ? null : Hash(path, ct), File.GetLastWriteTimeUtc(path).Ticks));
                if (directory) folders.Push((path, depth + 1));
            }
        }
        return entries.OrderBy(e => e.Name, StringComparer.Ordinal).ToArray();
    }
    private static string Hash(string path, CancellationToken ct)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length > 268435456) throw new InvalidDataException("A recovery verification input exceeds 256 MiB.");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); byte[] buffer = new byte[65536]; int read; long consumed = 0;
        while ((read = stream.Read(buffer)) > 0)
        {
            ct.ThrowIfCancellationRequested(); consumed += read;
            if (consumed > 268435456) throw new InvalidDataException("A recovery input grew beyond 256 MiB.");
            hash.AppendData(buffer, 0, read);
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
    private static JsonElement Property(JsonElement value, string name) => value.EnumerateObject()
        .Single(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)).Value;
}
