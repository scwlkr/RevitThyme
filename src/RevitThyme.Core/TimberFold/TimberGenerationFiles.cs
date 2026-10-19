using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;

namespace RevitThyme.Core;

internal static partial class TimberGenerationFiles
{
    private sealed record Evidence(string Name, string Sha256, long Bytes);
    private sealed record Kit(int Schema, string State, Guid RunId, Guid PreviewRunId, Target SourceTarget,
        Target AfterTarget, string SourceFingerprint, int PanelCount, int SheetCount,
        bool ManufacturingReady, bool PhysicalChecksOutstanding, bool NativeReadbackPassed,
        ImmutableArray<Evidence> Files);

    internal static ImmutableArray<TimberCutFile> CaptureInputs(string directory, TimberRunStart request,
        TimberNativeKitPlan plan, TimberCutStaging cutting)
    {
        var files = ImmutableArray.CreateBuilder<TimberCutFile>();
        _ = TimberRunProvenance.Read(directory);
        var producer = Describe(directory, "producer.json"); files.Add(new(producer.Name, producer.Sha256, producer.Bytes));
        AddBytes("request.json", JsonSerializer.SerializeToUtf8Bytes(request, Protocol.Json));
        AddBytes("snapshot.json", plan.SourceSnapshotJson.AsSpan().ToArray());
        AddBytes("fitted.json", JsonSerializer.SerializeToUtf8Bytes(plan.Fitted, Protocol.Json));
        AddBytes("layout.json", JsonSerializer.SerializeToUtf8Bytes(plan.Stock, Protocol.Json));
        AddBytes("cutting-result.json", JsonSerializer.SerializeToUtf8Bytes(cutting, Protocol.Json));
        AddBytes("native-plan.json", JsonSerializer.SerializeToUtf8Bytes(plan, Protocol.Json));
        var timing = Describe(directory, "timings.json"); files.Add(new(timing.Name, timing.Sha256, timing.Bytes));
        foreach (var file in plan.CutFiles) AddFile("cutting/" + file.Name, file);
        foreach (var file in plan.Guides.Files) AddFile("guides/" + file.Name, file);
        var result = files.OrderBy(f => f.Name, StringComparer.Ordinal).ToImmutableArray();
        if (!result.Select(f => f.Name).SequenceEqual(Inventory(directory)))
            throw new InvalidDataException("Generation input inventory differs from its verified plan.");
        return result;

        void AddBytes(string name, byte[] bytes) => AddFile(name, new(name, Convert.ToHexStringLower(SHA256.HashData(bytes)), bytes.Length));
        void AddFile(string name, TimberCutFile file)
        { Check(directory, name, file.Sha256, file.Bytes); files.Add(file with { Name = name }); }
    }

    internal static byte[] Manifest(TimberRunSummary summary, TimberNativeKitReceipt receipt, ImmutableArray<TimberCutFile> inputs)
    {
        ValidateReceipt(summary, receipt);
        foreach (var file in inputs) Check(summary.Directory, file.Name, file.Sha256, file.Bytes);
        var expected = inputs.Select(f => f.Name).Concat(receipt.Files.Select(f => f.RelativePath))
            .Concat(["native-prepared.json", "native-receipt.json"]).Order(StringComparer.Ordinal).ToArray();
        if (expected.Distinct(StringComparer.OrdinalIgnoreCase).Count() != expected.Length ||
            !expected.SequenceEqual(Inventory(summary.Directory)))
            throw new InvalidDataException("Native finalization changed the verified staging inventory.");
        var files = Inventory(summary.Directory).Select(name => Describe(summary.Directory, name)).ToImmutableArray();
        return JsonSerializer.SerializeToUtf8Bytes(new Kit(2, "DigitalKitVerified", summary.RunId,
            summary.PreviewRunId!.Value, summary.Target, receipt.AfterTarget, summary.SourceFingerprint!,
            summary.PanelCount, summary.SheetCount, false, true, true, files), Protocol.Json);
    }

    internal static void CheckRecovery(string directory, TimberRunStart request, TimberRunSummary summary)
    {
        var kit = Read<Kit>(Path.Combine(directory, "kit.json"));
        if (kit.Schema is not (1 or 2) || kit.State != "DigitalKitVerified" || kit.RunId != request.Id || kit.PreviewRunId != request.PreviewRunId ||
            kit.SourceTarget != request.Target || kit.SourceFingerprint != summary.SourceFingerprint ||
            kit.PanelCount != summary.PanelCount || kit.SheetCount != summary.SheetCount || kit.ManufacturingReady ||
            !kit.PhysicalChecksOutstanding || !kit.NativeReadbackPassed || !summary.NativeCommitted ||
            kit.Files.IsDefaultOrEmpty || kit.Files.Any(f => f is null) ||
            !kit.Files.Select(f => f.Name).SequenceEqual(Inventory(directory)))
            throw new InvalidDataException("Digital kit ownership, verification or inventory changed.");
        foreach (var file in kit.Files) Check(directory, file.Name, file.Sha256, file.Bytes);
        if (kit.Schema >= 2 && !kit.Files.Any(f => f.Name == "producer.json")) throw new InvalidDataException("Digital kit producer identity is missing.");
        if (kit.Schema >= 2) _ = TimberRunProvenance.Read(directory);
        var receipt = Read<TimberNativeKitReceipt>(Path.Combine(directory, "native-receipt.json"));
        if (receipt.AfterTarget != kit.AfterTarget) throw new InvalidDataException("Native receipt target differs from the kit.");
        ValidateReceipt(summary, receipt);
    }

    private static void ValidateReceipt(TimberRunSummary summary, TimberNativeKitReceipt receipt)
    {
        var before = summary.Target; var after = receipt.AfterTarget;
        if (receipt.State != TimberNativeKitState.Committed || receipt.RunId != summary.RunId ||
            !receipt.SourcePreserved || !receipt.NativeReadbackPassed || after is null ||
            receipt.NativeMilliseconds is { } nativeMilliseconds && (!double.IsFinite(nativeMilliseconds) || nativeMilliseconds < 0) ||
            after.ProcessId != before.ProcessId || after.ProcessStart != before.ProcessStart || after.SessionId != before.SessionId ||
            after.DocumentId != before.DocumentId || after.ViewId != before.ViewId || after.Revision < before.Revision ||
            receipt.Diagnostics.IsDefault || receipt.Diagnostics.Any(d => d.Severity == TimberSeverity.Error) ||
            receipt.CreatedElements.IsDefaultOrEmpty || receipt.CreatedElements.Any(e => e is null || e.Id <= 0 ||
                string.IsNullOrWhiteSpace(e.UniqueId) || string.IsNullOrWhiteSpace(e.Role)) ||
            receipt.CreatedElements.Select(e => e.Id).Distinct().Count() != receipt.CreatedElements.Length ||
            receipt.CreatedElements.Select(e => e.UniqueId).Distinct(StringComparer.Ordinal).Count() != receipt.CreatedElements.Length ||
            receipt.Files.IsDefaultOrEmpty || receipt.Files.Any(f => f is null) ||
            receipt.Files.Select(f => f.RelativePath).Distinct(StringComparer.OrdinalIgnoreCase).Count() != receipt.Files.Length)
            throw new InvalidDataException("Native receipt does not confirm a complete preserved digital kit.");
        var cuts = Enumerable.Range(1, summary.SheetCount).Select(n => $"native-cuts/CUT-{n:000}.dwg")
            .Append("native-cuts/CALIBRATION.dwg").Order(StringComparer.Ordinal);
        if (!receipt.Files.Where(f => f.Role is "CutDwg" or "CalibrationDwg").Select(f => f.RelativePath)
            .Order(StringComparer.Ordinal).SequenceEqual(cuts))
            throw new InvalidDataException("Native cutting inventory is incomplete.");
        foreach (var file in receipt.Files)
        {
            bool permitted = file.Role switch
            {
                "CutDwg" => file.RelativePath.StartsWith("native-cuts/CUT-", StringComparison.Ordinal) && file.RelativePath.EndsWith(".dwg", StringComparison.Ordinal),
                "CalibrationDwg" => file.RelativePath == "native-cuts/CALIBRATION.dwg",
                "ExportSupport" => file.RelativePath.StartsWith("native-export-support/", StringComparison.Ordinal),
                "NativeVerification" => file.RelativePath == "native-readback.json",
                "NativeReferenceInput" => file.RelativePath.StartsWith("native-reference-inputs/", StringComparison.Ordinal) &&
                    file.RelativePath.EndsWith(".dxf", StringComparison.Ordinal),
                _ => false
            };
            if (!permitted) throw new InvalidDataException("Native file has an unrecognized role or location.");
            Check(summary.Directory, file.RelativePath, file.Sha256, file.Bytes);
        }
        string prepared = Path.Combine(summary.Directory, "native-prepared.json");
        Check(summary.Directory, "native-prepared.json", receipt.PreparedReceiptSha256!, new FileInfo(prepared).Length);
        if (receipt.InputSealSha256 is not null) RequireInputSealBinding(summary.Directory, receipt);
    }

    private static T Read<T>(string path)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length > 16777216) throw new InvalidDataException("Kit record exceeds its size limit.");
        return JsonSerializer.Deserialize<T>(stream, Protocol.Json) ?? throw new InvalidDataException("Kit record is missing.");
    }

    private static IEnumerable<string> Inventory(string root)
    {
        var files = new List<string>(); var folders = new Stack<string>(); folders.Push(root);
        while (folders.TryPop(out string? folder))
        {
            if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Kit evidence cannot contain redirected directories.");
            foreach (string child in Directory.EnumerateFileSystemEntries(folder))
            {
                var attributes = File.GetAttributes(child);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Kit evidence cannot contain redirected files.");
                if ((attributes & FileAttributes.Directory) != 0) { folders.Push(child); continue; }
                string name = Path.GetRelativePath(root, child).Replace('\\', '/');
                if (name is not ("kit.json" or "run.json" or "diagnostics.json" or "run-timings.json")) files.Add(name);
                if (files.Count > 4096) throw new InvalidDataException("Kit inventory exceeds its size limit.");
            }
        }
        return files.Order(StringComparer.Ordinal);
    }

    private static Evidence Describe(string directory, string name)
    {
        using var stream = File.OpenRead(SafePath(directory, name));
        if (stream.Length <= 0 || stream.Length > 268435456) throw new InvalidDataException("Kit file exceeds its size limit: " + name);
        return new(name, Convert.ToHexStringLower(SHA256.HashData(stream)), stream.Length);
    }
    private static void Check(string directory, string name, string sha256, long bytes)
    {
        if (sha256 is not { Length: 64 } || !sha256.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'))
            throw new InvalidDataException("Kit file hash is invalid.");
        var actual = Describe(directory, name);
        if (bytes <= 0 || actual.Bytes != bytes || actual.Sha256 != sha256)
            throw new InvalidDataException("Kit evidence changed: " + name);
    }
    private static string SafePath(string directory, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Contains('\\') || name.Contains(':') || name.Split('/').Any(p => p is "" or "." or "..") || Path.IsPathRooted(name))
            throw new InvalidDataException("Kit file path is invalid.");
        string path = Path.GetFullPath(Path.Combine(directory, name));
        if (!path.StartsWith(Path.TrimEndingDirectorySeparator(directory) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Kit file escapes its owned directory.");
        return path;
    }
}
