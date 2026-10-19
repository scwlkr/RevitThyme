using System.Collections.Immutable;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace RevitThyme.Core;

internal sealed record TimberPreviewResult(string Fingerprint, int PanelCount, int SheetCount,
    ImmutableArray<TimberDiagnostic> Diagnostics, byte[]? Manifest);

internal static class TimberPreviewPipeline
{
    private sealed record Evidence(string Name, string Sha256, long Bytes);
    private sealed record Timing(string Stage, double Milliseconds);
    private sealed record Preview(int Schema, string State, bool ManufacturingReady, bool NativeImportOutstanding,
        bool PhysicalChecksOutstanding, string SourceFingerprint, TimberFoldProfile Profile,
        int PanelCount, int SheetCount, ImmutableArray<TimberCutFile> CuttingFiles, Evidence[] Evidence);

    internal static void CheckRecovery(string directory, TimberRunStart request, TimberRunSummary summary)
    {
        string path = Path.Combine(directory, "preview.json");
        using var manifestStream = File.OpenRead(path);
        if (manifestStream.Length > 1048576) throw new InvalidDataException("Preview manifest exceeds its size limit.");
        var preview = JsonSerializer.Deserialize<Preview>(manifestStream, Protocol.Json)
            ?? throw new InvalidDataException("Preview manifest is missing.");
        if (preview.Schema != 1 || preview.State != "PreviewVerified" || preview.ManufacturingReady ||
            !preview.NativeImportOutstanding || !preview.PhysicalChecksOutstanding || preview.Profile != request.Profile ||
            preview.PanelCount != summary.PanelCount || preview.SheetCount != summary.SheetCount ||
            preview.SourceFingerprint != summary.SourceFingerprint || preview.Evidence is null ||
            preview.CuttingFiles.IsDefaultOrEmpty || preview.CuttingFiles.Length != preview.SheetCount + 2 ||
            preview.Evidence.Any(e => e is null || !ValidHash(e.Sha256) || e.Bytes <= 0) ||
            preview.CuttingFiles.Any(f => f is null || !ValidHash(f.Sha256) || f.Bytes <= 0) ||
            !preview.Evidence.Select(e => e.Name).Order().SequenceEqual(new[] { "cutting-result.json", "fitted.json", "layout.json", "snapshot.json" }))
            throw new InvalidDataException("Preview manifest differs from retained run settings or inventory.");
        foreach (var evidence in preview.Evidence) Check(directory, evidence.Name, evidence.Sha256, evidence.Bytes);
        string cutting = Path.Combine(directory, "cutting");
        var expected = Enumerable.Range(1, preview.SheetCount).Select(n => $"CUT-{n:000}.dxf")
            .Append("CALIBRATION.dxf").Append("cut-staging.json").Order(StringComparer.Ordinal).ToArray();
        if (!preview.CuttingFiles.Select(f => f.Name).Order(StringComparer.Ordinal).SequenceEqual(expected) ||
            !Directory.EnumerateFileSystemEntries(cutting).Select(Path.GetFileName).Order(StringComparer.Ordinal).SequenceEqual(expected))
            throw new InvalidDataException("Preview cutting inventory differs from its manifest.");
        foreach (var file in preview.CuttingFiles) Check(cutting, file.Name, file.Sha256, file.Bytes);

        static bool ValidHash(string? value) => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

        static void Check(string folder, string name, string hash, long bytes)
        {
            using var stream = File.OpenRead(Path.Combine(folder, name));
            if (stream.Length != bytes || stream.Length > 134217728 || Convert.ToHexStringLower(SHA256.HashData(stream)) != hash)
                throw new InvalidDataException("Preview evidence changed: " + name);
        }
    }

    internal static TimberPreviewResult Compute(TimberRunStart request, TimberCapturedSnapshot captured,
        TimberRunStore store, Action<TimberRunState, string> progress, CancellationToken ct)
    {
        var timings = new List<Timing>(); var clock = Stopwatch.StartNew();
        var source = captured.Decode();
        var scope = request.Scope ?? new TimberScope(source.PhaseId, source.DesignOptionIds);
        progress(TimberRunState.Fitting, "Fitting panel geometry and checking collisions");
        var fitted = TimberFabrication.Fit(source, request.Profile, scope, ct);
        Save("fitted.json", fitted); Mark("fitting");
        if (fitted.State != TimberFitState.GeometryFitted)
            return Result(fitted.Draft.Panels.Length, 0, fitted.Diagnostics);
        progress(TimberRunState.Layout, "Planning stock sheets");
        var stock = TimberStockLayout.Create(fitted.Draft.Panels, request.Profile, ct);
        Save("layout.json", stock); Mark("layout");
        if (!stock.Planned) return Result(fitted.Draft.Panels.Length, stock.SheetCount, stock.Diagnostics);
        progress(TimberRunState.Cutting, "Staging and independently reading cutting files");
        string directory = Path.Combine(store.DirectoryFor(request.Id), "cutting");
        var cut = TimberCutFiles.Stage(directory, fitted.Draft.Panels, stock, request.Profile, ct);
        if (cut.Staged) cut = TimberCutFiles.Verify(directory, fitted.Draft.Panels, stock, request.Profile, ct);
        Save("cutting-result.json", cut); Mark("cutting");
        Save("timings.json", timings);
        if (!cut.Staged) return Result(fitted.Draft.Panels.Length, stock.SheetCount, cut.Diagnostics);
        ct.ThrowIfCancellationRequested();
        var evidence = new[] { "snapshot.json", "fitted.json", "layout.json", "cutting-result.json" }.Select(name =>
        {
            using var stream = File.OpenRead(Path.Combine(store.DirectoryFor(request.Id), name));
            return new Evidence(name, Convert.ToHexStringLower(SHA256.HashData(stream)), stream.Length);
        }).ToArray();
        var manifest = new Preview(1, "PreviewVerified", false, true, true, source.DocumentFingerprint,
            request.Profile, fitted.Draft.Panels.Length, stock.SheetCount, cut.Files, evidence);
        return new(source.DocumentFingerprint, fitted.Draft.Panels.Length, stock.SheetCount, fitted.Diagnostics,
            JsonSerializer.SerializeToUtf8Bytes(manifest, Protocol.Json));

        TimberPreviewResult Result(int panels, int sheets, ImmutableArray<TimberDiagnostic> diagnostics) =>
            new(source.DocumentFingerprint, panels, sheets, diagnostics, null);
        void Save<T>(string name, T value)
        { ct.ThrowIfCancellationRequested(); store.WriteNew(request.Id, name, JsonSerializer.SerializeToUtf8Bytes(value, Protocol.Json)); }
        void Mark(string stage) { timings.Add(new(stage, clock.Elapsed.TotalMilliseconds)); clock.Restart(); }
    }
}
