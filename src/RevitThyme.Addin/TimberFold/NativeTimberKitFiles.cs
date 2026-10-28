using System.Collections.Immutable;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using RevitThyme.Core;

namespace RevitThyme.Addin;

internal static class NativeTimberKitFiles
{
    internal static string RunDirectory(Guid id) => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RevitThyme", "TimberFold", "runs", id.ToString("D"));

    internal static TimberNativeKitPlan Read(TimberNativeKitRequest request, CancellationToken ct)
        => Read(request, ct, RunDirectory);

    internal static TimberNativeKitPlan Read(TimberNativeKitRequest request, CancellationToken ct, Func<Guid, string> runDirectory)
    {
        ct.ThrowIfCancellationRequested();
        CheckRequest(request, runDirectory);
        RequirePlain(request.Directory);
        CheckInputSeal(request);
        var plan = JsonSerializer.Deserialize<TimberNativeKitPlan>(request.PlanJson.AsSpan(), Protocol.Json)
            ?? throw new InvalidDataException("Native kit plan is missing.");
        CheckPlan(request, plan);
        CheckSource(request, plan);
        CheckStaging(request.Directory, plan, ct);
        CheckGuides(plan.Guides);
        CheckPanelLabels(plan);
        return plan;
    }

    private static void CheckRequest(TimberNativeKitRequest request, Func<Guid, string> runDirectory)
    {
        if (request.RunId == Guid.Empty || request.PlanJson.IsDefaultOrEmpty || request.PlanJson.Length > 134217728 ||
            !Path.IsPathFullyQualified(request.Directory) ||
            !string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.Directory)), runDirectory(request.RunId), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Native finalization requires its exact host-owned run directory and bounded plan.");
    }

    private static void CheckPlan(TimberNativeKitRequest request, TimberNativeKitPlan plan)
    {
        if (!MatchesRequest(request, plan) || !HasVerifiedGeometry(plan) || !HasStagedParts(plan))
            throw new InvalidDataException("Native kit plan is incomplete, unverified or differs from its request.");
    }

    private static bool MatchesRequest(TimberNativeKitRequest request, TimberNativeKitPlan plan) =>
        plan.Schema == 1 && plan.RunId == request.RunId && plan.PreviewRunId != Guid.Empty && plan.SourceContext.Target == request.Target;

    private static bool HasVerifiedGeometry(TimberNativeKitPlan plan) =>
        !plan.SourceSnapshotJson.IsDefaultOrEmpty && plan.SourceFingerprint.Length == 64 &&
        plan.Profile.Validate().Length == 0 && plan.Fitted.State == TimberFitState.GeometryFitted && plan.Fitted.Verification.Passed;

    private static bool HasStagedParts(TimberNativeKitPlan plan) =>
        plan.Stock.Planned && plan.Guides.Staged && !plan.CutFiles.IsDefaultOrEmpty &&
        !plan.Fitted.Draft.Panels.IsDefaultOrEmpty && plan.Fitted.Draft.Panels.Length <= 10000;

    private static void CheckSource(TimberNativeKitRequest request, TimberNativeKitPlan plan)
    {
        var source = JsonSerializer.Deserialize<TimberSnapshot>(plan.SourceSnapshotJson.AsSpan(), Protocol.Json)
            ?? throw new InvalidDataException("Original source snapshot is missing.");
        if (source.DocumentFingerprint != plan.SourceFingerprint || source.Target != request.Target ||
            TimberInspection.GetDiagnostics(source, plan.Profile, plan.Scope).Any(d => d.Severity == TimberSeverity.Error))
            throw new InvalidDataException("Source evidence differs from the verified plan.");
    }

    private static void CheckStaging(string directory, TimberNativeKitPlan plan, CancellationToken ct)
    {
        string cutting = Path.Combine(directory, "cutting"), guides = Path.Combine(directory, "guides");
        RequirePlain(cutting); RequirePlain(guides);
        if (!string.Equals(Path.GetFullPath(plan.Guides.Directory), guides, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Guide inputs must belong to this generation directory.");
        CheckFiles(cutting, plan.CutFiles, ct); CheckFiles(guides, plan.Guides.Files, ct);
        var verified = TimberCutFiles.Verify(cutting, plan.Fitted.Draft.Panels, plan.Stock, plan.Profile, ct);
        if (!verified.Staged || !verified.Files.SequenceEqual(plan.CutFiles))
            throw new InvalidDataException("Canonical cutting files failed independent verification.");
    }

    private static void CheckGuides(TimberGuideStaging guides)
    {
        if (!HasGuideInventory(guides) || guides.Drawings.Any(d => !ValidDrawing(d, guides.Files)) ||
            guides.Labels.Any(l => !ValidLabel(l, guides.Drawings)))
            throw new InvalidDataException("Native guide inventory or labels are invalid.");
    }

    private static bool HasGuideInventory(TimberGuideStaging guides) =>
        !guides.Drawings.IsDefaultOrEmpty && !guides.Labels.IsDefaultOrEmpty && guides.Drawings.Length <= 1000 && guides.Labels.Length <= 100000 &&
        guides.Drawings.Select(d => d.File).Distinct(StringComparer.Ordinal).Count() == guides.Drawings.Length;

    private static bool ValidDrawing(TimberGuideDrawing drawing, ImmutableArray<TimberCutFile> files) =>
        files.Any(f => f.Name == drawing.File) && drawing.File.EndsWith(".dxf", StringComparison.Ordinal);

    private static bool ValidLabel(TimberGuideLabel label, ImmutableArray<TimberGuideDrawing> drawings) =>
        drawings.Any(d => d.File == label.GuideFile) && !string.IsNullOrWhiteSpace(label.Text) && label.Text.Length <= 16384 &&
        double.IsFinite(label.PhysicalPositionInches.X) && double.IsFinite(label.PhysicalPositionInches.Y) &&
        double.IsFinite(label.TextHeightInches) && label.TextHeightInches > 0 && label.TextHeightInches <= 2;

    private static void CheckPanelLabels(TimberNativeKitPlan plan)
    {
        var panels = plan.Fitted.Draft.Panels;
        if (panels.Any(p => !plan.Guides.Labels.Any(l => MatchesPanel(l, p))) ||
            plan.Guides.Labels.Any(l => l.PanelId is not null && !panels.Any(p => MatchesPanel(l, p))))
            throw new InvalidDataException("Every native part label must map to the exact source panel and face.");
    }

    private static bool MatchesPanel(TimberGuideLabel label, TimberPanel panel) =>
        panel.Id == label.PanelId && panel.SourceId == label.SourceId && panel.FaceId == label.FaceId;

    internal static void CheckInputSeal(TimberNativeKitRequest request)
    {
        string path = Path.Combine(request.Directory, "generation-inputs.json");
        if (request.InputSealSha256 is not { Length: 64 } ||
            (System.IO.File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 || Hash(path) != request.InputSealSha256)
            throw new InvalidDataException("Verified generation input seal changed before native execution or assimilation.");
    }

    internal static string FreshDirectory(string run, string name)
    {
        string path = Path.Combine(run, name);
        if (Directory.Exists(path) || System.IO.File.Exists(path)) throw new IOException("Native output already exists; recover the original generation instead of retrying.");
        string temporary = Path.Combine(run, ".native-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary); Directory.Move(temporary, path); return path;
    }
    internal static void RequirePlain(string path)
    {
        for (var part = new DirectoryInfo(path); part is not null; part = part.Parent)
            if ((part.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Native run paths cannot use reparse points.");
    }
    internal static string Hash(string path, CancellationToken cancellationToken = default)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[81920]; int read;
        while ((read = stream.Read(buffer)) > 0)
        { cancellationToken.ThrowIfCancellationRequested(); hash.AppendData(buffer, 0, read); }
        cancellationToken.ThrowIfCancellationRequested();
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
    internal static TimberNativeFile File(string run, string path, string role) =>
        new(Path.GetRelativePath(run, path).Replace('\\', '/'), Hash(path), new FileInfo(path).Length, role);
    internal static string WriteNew(string path, object value) =>
        WriteNew(path, value, p => new FileStream(p, FileMode.CreateNew, FileAccess.Write, FileShare.Read));

    internal static string WriteNew(string path, object value, Func<string, FileStream> open)
    {
        using (var stream = open(path))
        { JsonSerializer.Serialize(stream, value, Protocol.Json); stream.Flush(true); }
        return Hash(path);
    }
    private static void CheckFiles(string directory, ImmutableArray<TimberCutFile> files, CancellationToken ct)
    {
        // Plan validation guarantees nonempty arrays; exact inventory also rejects duplicate names.
        if (!Directory.GetFileSystemEntries(directory).Select(Path.GetFileName).Order(StringComparer.Ordinal)
                .SequenceEqual(files.Select(f => f.Name).Order(StringComparer.Ordinal)))
            throw new InvalidDataException("Native input file inventory differs from its verified plan.");
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            CheckFileMetadata(file);
            CheckFileContent(directory, file);
        }
    }

    private static void CheckFileMetadata(TimberCutFile file)
    {
        if (file.Bytes <= 0 || file.Bytes > 134217728)
            throw new InvalidDataException("Invalid native input file metadata.");
    }

    private static void CheckFileContent(string directory, TimberCutFile file)
    {
        string path = Path.Combine(directory, file.Name);
        if ((System.IO.File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 || new FileInfo(path).Length != file.Bytes || Hash(path) != file.Sha256)
            throw new InvalidDataException("Native input changed: " + file.Name);
    }
}
