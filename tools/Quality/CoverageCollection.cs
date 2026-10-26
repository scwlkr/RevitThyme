using System.Text.Json;

namespace RevitThyme.Quality;

public sealed record CoverageReceipt(string Root, string? Revision, string SourceDigest, string CommandsDigest,
    SnapshotFile[] Sources, SnapshotFile[] Reports);

public static class CoverageCollection
{
    public static async Task<CoverageReceipt> Collect(string root, string config, string collector, string output)
    {
        if (!Path.IsPathFullyQualified(collector) || !File.Exists(collector))
            throw new ArgumentException("Use the absolute path to the prepared dotnet-coverage executable.");
        var commands = TestCommands.Load(config);
        var tests = commands.AllTests();
        var sources = SourceSnapshot.Capture(root);
        string? revision = SourceSnapshot.Revision(root);
        string run = Guid.NewGuid().ToString("N");
        string logs = Path.Combine(root, "artifacts", "quality", "coverage", run);
        Directory.CreateDirectory(logs);
        using var ownership = new FileStream(Path.Combine(root, "artifacts", "quality", "coverage.lock"),
            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        ArchiveReports(root, run);
        if (!(await Commands.Run(commands.Build, root, Path.Combine(logs, "build.txt"), commands.TimeoutSeconds)).Passed)
            throw new InvalidDataException("Coverage build failed: " + logs);
        var reports = new List<SnapshotFile>();
        for (int i = 0; i < tests.Length; i++) reports.Add(await CollectReport(i));
        SourceSnapshot.Verify(root, sources);
        if (SourceSnapshot.Revision(root) != revision) throw new IOException("Git revision changed during coverage collection.");
        var receipt = new CoverageReceipt(Path.GetFullPath(root), revision, SourceSnapshot.Digest(sources),
            SourceSnapshot.HashFile(config), sources.ToArray(), reports.ToArray());
        Reports.Write(output, receipt);
        Validate(root, config, output);
        return receipt;

        async Task<SnapshotFile> CollectReport(int i)
        {
            string report = Path.Combine(root, "target", "coverage", "csharp", run, "suite-" + i, "coverage.cobertura.xml");
            string[] command = [collector, "collect", "-f", "cobertura", "-o", report, "--", .. tests[i]];
            var result = await Commands.Run(command, root, Path.Combine(logs, "suite-" + i + ".txt"), commands.TimeoutSeconds);
            if (!result.Passed || !File.Exists(report)) throw new InvalidDataException("Coverage tests failed or produced no report: " + logs);
            _ = Coverage.Load(root, report); // Reject empty or malformed evidence before issuing a receipt.
            return new(Path.GetRelativePath(root, report).Replace('\\', '/'), SourceSnapshot.HashFile(report));
        }
    }

    public static CoverageReceipt Validate(string root, string config, string path)
    {
        var receipt = JsonSerializer.Deserialize<CoverageReceipt>(File.ReadAllText(path), Reports.Json)
            ?? throw new InvalidDataException("Coverage receipt is missing.");
        if (!MatchesReceipt(root, config, receipt))
            throw new InvalidDataException("Coverage receipt does not match this source, revision and command configuration.");
        SourceSnapshot.Verify(root, receipt.Sources);
        var actual = ConventionalReports(root).Select(p => Path.GetRelativePath(root, p).Replace('\\', '/')).Order(StringComparer.Ordinal).ToArray();
        if (!actual.SequenceEqual(receipt.Reports.Select(r => r.Path).Order(StringComparer.Ordinal)))
            throw new InvalidDataException("Coverage report set changed; collect fresh evidence.");
        foreach (var report in receipt.Reports)
            if (SourceSnapshot.HashFile(SourceSnapshot.Resolve(root, report.Path)) != report.Sha256)
                throw new InvalidDataException("Coverage report bytes changed; collect fresh evidence.");
        return receipt;
    }

    private static bool MatchesReceipt(string root, string config, CoverageReceipt receipt) =>
        string.Equals(receipt.Root, Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase)
        && receipt.Revision == SourceSnapshot.Revision(root) && receipt.CommandsDigest == SourceSnapshot.HashFile(config)
        && receipt.SourceDigest == SourceSnapshot.Digest(receipt.Sources) && receipt.Reports.Length > 0;

    private static void ArchiveReports(string root, string run)
    {
        string[] previous = ConventionalReports(root).ToArray();
        string archive = Path.Combine(root, "artifacts", "quality", "coverage-archives", run);
        foreach (string file in previous)
        {
            string target = Path.Combine(archive, Path.GetRelativePath(root, file));
            AssertInside(root, file); AssertInside(root, target);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Move(file, target);
        }
        if (previous.Length > 0) Reports.Write(Path.Combine(archive, "archive.json"), new { files = previous });
    }

    private static IEnumerable<string> ConventionalReports(string root)
    {
        foreach (string relative in new[] { "target/coverage/csharp", "coverage", "TestResults" })
        {
            string directory = SourceSnapshot.Resolve(root, relative);
            if (!Directory.Exists(directory)) continue;
            AssertInside(root, directory);
            foreach (string file in FilesWithoutLinks(directory))
                if (relative == "target/coverage/csharp" ? Path.GetExtension(file).Equals(".xml", StringComparison.OrdinalIgnoreCase)
                    : Path.GetFileName(file).Equals("coverage.cobertura.xml", StringComparison.OrdinalIgnoreCase)) yield return file;
        }
    }

    private static IEnumerable<string> FilesWithoutLinks(string root)
    {
        var pending = new Stack<string>(); pending.Push(root);
        while (pending.TryPop(out var directory))
            foreach (string path in Directory.EnumerateFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Coverage report trees cannot contain links: " + path);
                if ((attributes & FileAttributes.Directory) != 0) pending.Push(path);
                else yield return path;
            }
    }

    private static void AssertInside(string root, string path)
    {
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        string full = Path.GetFullPath(path);
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Coverage output path escapes its workspace.");
        for (string? parent = Directory.Exists(full) ? full : Path.GetDirectoryName(full);
            parent is not null && !parent.Equals(root, StringComparison.OrdinalIgnoreCase); parent = Path.GetDirectoryName(parent))
            if (Directory.Exists(parent) && (File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Coverage output paths cannot traverse links.");
    }
}
