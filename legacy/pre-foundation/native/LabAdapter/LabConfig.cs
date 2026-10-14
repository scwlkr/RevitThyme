using System.Text.Json;
using RevitThyme.Native;

namespace RevitThyme.Lab;
public sealed record LabConfig(string RunId, string FixturePath, string BindingPath, string RevitVersion, bool CreateFixture, bool AllowWrites)
{
    public static LabConfig Load(string path)
    {
        PlainPath(path);
        if (new FileInfo(path).Length > 4096) throw new InvalidDataException("Lab config exceeds limit.");
        var config = JsonSerializer.Deserialize<LabConfig>(File.ReadAllText(path), Wire.Json) ?? throw new InvalidDataException("Lab config required.");
        string root = Path.GetDirectoryName(Path.GetFullPath(path))!;
        if (!Guid.TryParseExact(config.RunId, "D", out _) || Path.GetFileName(root) != config.RunId
            || Path.GetFileName(path) != "config.json" || config.RevitVersion != "27.3.0.28"
            || !Same(config.FixturePath, Path.Combine(root, "RevitThyme-Test.rvt"))
            || !Same(config.BindingPath, Path.Combine(root, "binding.json")))
            throw new InvalidDataException("Use an explicit, prepared Revit 2027.3 lab run.");
        PlainPath(config.FixturePath); PlainPath(config.BindingPath);
        if (File.Exists(config.BindingPath)) throw new InvalidDataException("Run already bound. Prepare a fresh run; never rebind.");
        if (config.CreateFixture && File.Exists(config.FixturePath)) throw new InvalidDataException("Fixture exists; refusing replacement.");
        return config;
    }
    public static bool Same(string a, string b) => Path.IsPathFullyQualified(a) && Path.IsPathFullyQualified(b)
        && string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
    public static void PlainPath(string path)
    {
        if (!Path.IsPathFullyQualified(path) || path.StartsWith("\\\\", StringComparison.Ordinal)) throw new InvalidDataException("Local absolute lab path required.");
        for (string? p = Path.GetFullPath(path); p is not null; p = Path.GetDirectoryName(p))
            if ((File.Exists(p) || Directory.Exists(p)) && (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Redirected lab paths are refused.");
    }
}
// Invoked in the same API callback as model reads and writes. A reopened file is a new target.
public sealed class FixtureGuard(string path, bool allowWrites)
{
    private string? documentId;
    private bool closed;
    public void Check(string actualPath, string id, bool workshared)
    {
        LabConfig.PlainPath(path);
        if (!LabConfig.Same(path, actualPath) || workshared)
            throw new Rejection("wrong_fixture", "Only this run's non-workshared RevitThyme-Test.rvt is allowed.");
        if (closed || documentId is not null && documentId != id)
            throw new Rejection("reopened_fixture", "The approved document instance closed. Prepare a new lab run.");
        documentId ??= id;
    }
    public void Close() { if (documentId is not null) closed = true; }
    public void Write()
    {
        if (!allowWrites) throw new Rejection("lab_read_only", "This lab run has no approved model-write permission.");
    }
}
