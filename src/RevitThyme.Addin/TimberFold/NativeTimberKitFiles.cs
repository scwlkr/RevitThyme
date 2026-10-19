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
    {
        ct.ThrowIfCancellationRequested();
        if (request.RunId == Guid.Empty || request.PlanJson.IsDefaultOrEmpty || request.PlanJson.Length > 134217728 ||
            !Path.IsPathFullyQualified(request.Directory) ||
            !string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.Directory)), RunDirectory(request.RunId), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Native finalization requires its exact host-owned run directory and bounded plan.");
        RequirePlain(request.Directory);
        CheckInputSeal(request);
        var plan = JsonSerializer.Deserialize<TimberNativeKitPlan>(request.PlanJson.AsSpan(), Protocol.Json)
            ?? throw new InvalidDataException("Native kit plan is missing.");
        if (plan.Schema != 1 || plan.RunId != request.RunId || plan.PreviewRunId == Guid.Empty || plan.SourceContext.Target != request.Target ||
            plan.SourceSnapshotJson.IsDefaultOrEmpty || plan.SourceFingerprint.Length != 64 ||
            plan.Profile.Validate().Length != 0 || plan.Fitted.State != TimberFitState.GeometryFitted || !plan.Fitted.Verification.Passed ||
            !plan.Stock.Planned || !plan.Guides.Staged || plan.CutFiles.IsDefaultOrEmpty ||
            plan.Fitted.Draft.Panels.IsDefaultOrEmpty || plan.Fitted.Draft.Panels.Length > 10000)
            throw new InvalidDataException("Native kit plan is incomplete, unverified or differs from its request.");
        var source = JsonSerializer.Deserialize<TimberSnapshot>(plan.SourceSnapshotJson.AsSpan(), Protocol.Json)
            ?? throw new InvalidDataException("Original source snapshot is missing.");
        if (source.DocumentFingerprint != plan.SourceFingerprint || source.Target != request.Target ||
            TimberInspection.GetDiagnostics(source, plan.Profile, plan.Scope).Any(d => d.Severity == TimberSeverity.Error))
            throw new InvalidDataException("Source evidence differs from the verified plan.");
        string cutting = Path.Combine(request.Directory, "cutting"), guides = Path.Combine(request.Directory, "guides");
        RequirePlain(cutting); RequirePlain(guides);
        if (!string.Equals(Path.GetFullPath(plan.Guides.Directory), guides, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Guide inputs must belong to this generation directory.");
        CheckFiles(cutting, plan.CutFiles, ct); CheckFiles(guides, plan.Guides.Files, ct);
        var verified = TimberCutFiles.Verify(cutting, plan.Fitted.Draft.Panels, plan.Stock, plan.Profile, ct);
        if (!verified.Staged || !verified.Files.SequenceEqual(plan.CutFiles))
            throw new InvalidDataException("Canonical cutting files failed independent verification.");
        if (plan.Guides.Drawings.IsDefaultOrEmpty || plan.Guides.Labels.IsDefaultOrEmpty || plan.Guides.Drawings.Length > 1000 || plan.Guides.Labels.Length > 100000 ||
            plan.Guides.Drawings.Select(d => d.File).Distinct(StringComparer.Ordinal).Count() != plan.Guides.Drawings.Length ||
            plan.Guides.Drawings.Any(d => !plan.Guides.Files.Any(f => f.Name == d.File) || !d.File.EndsWith(".dxf", StringComparison.Ordinal)) ||
            plan.Guides.Labels.Any(l => !plan.Guides.Drawings.Any(d => d.File == l.GuideFile) || string.IsNullOrWhiteSpace(l.Text) ||
                l.Text.Length > 16384 || !double.IsFinite(l.PhysicalPositionInches.X) || !double.IsFinite(l.PhysicalPositionInches.Y) ||
                !double.IsFinite(l.TextHeightInches) || l.TextHeightInches <= 0 || l.TextHeightInches > 2))
            throw new InvalidDataException("Native guide inventory or labels are invalid.");
        var panels = plan.Fitted.Draft.Panels;
        if (panels.Any(p => !plan.Guides.Labels.Any(l => l.PanelId == p.Id && l.SourceId == p.SourceId && l.FaceId == p.FaceId)) ||
            plan.Guides.Labels.Any(l => l.PanelId is not null && !panels.Any(p => p.Id == l.PanelId && p.SourceId == l.SourceId && p.FaceId == l.FaceId)))
            throw new InvalidDataException("Every native part label must map to the exact source panel and face.");
        return plan;
    }

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
    internal static string WriteNew(string path, object value)
    {
        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
        { JsonSerializer.Serialize(stream, value, Protocol.Json); stream.Flush(true); }
        return Hash(path);
    }
    private static void CheckFiles(string directory, ImmutableArray<TimberCutFile> files, CancellationToken ct)
    {
        if (files.IsDefaultOrEmpty || files.Select(f => f.Name).Distinct(StringComparer.Ordinal).Count() != files.Length ||
            !Directory.GetFileSystemEntries(directory).Select(Path.GetFileName).Order(StringComparer.Ordinal)
                .SequenceEqual(files.Select(f => f.Name).Order(StringComparer.Ordinal)))
            throw new InvalidDataException("Native input file inventory differs from its verified plan.");
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            if (file.Name != Path.GetFileName(file.Name) || file.Name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || file.Bytes <= 0 || file.Bytes > 134217728)
                throw new InvalidDataException("Invalid native input file metadata.");
            string path = Path.Combine(directory, file.Name);
            if ((System.IO.File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 || new FileInfo(path).Length != file.Bytes || Hash(path) != file.Sha256)
                throw new InvalidDataException("Native input changed: " + file.Name);
        }
    }
}
