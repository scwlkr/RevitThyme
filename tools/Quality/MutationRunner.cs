using System.Text.Json;

namespace RevitThyme.Quality;

public enum MutationState { Killed, Survived, Invalid, Unconfirmed }
public sealed record MutationOutcome(string SiteId, string UnitName, int Line, MutationState State, string Logs);
public sealed record MutationReport(string SourceDigest, string CommandsDigest, int TotalSites,
    MutationOutcome[] Results, bool Complete, string EngineId)
{
    public bool Passed => Complete && Results.Any(r => r.State == MutationState.Killed)
        && Results.All(r => r.State is MutationState.Killed or MutationState.Invalid);
}

public static class MutationRunner
{
    public static MutationState Classify(CommandResult build, IReadOnlyList<CommandResult> tests)
    {
        if (build.TimedOut) return MutationState.Unconfirmed;
        if (!build.Passed) return build.CompilerFailure ? MutationState.Invalid : MutationState.Unconfirmed;
        if (tests.Count == 0 || tests.Any(t => t.TimedOut)) return MutationState.Unconfirmed;
        return tests.All(t => t.Passed) ? MutationState.Survived : MutationState.Killed;
    }

    public static async Task<MutationReport> Run(string root, string commandsPath, string file, string output, int maximum)
    {
        if (maximum < 0) throw new ArgumentException("Mutation maximum must be nonnegative; zero runs all remaining sites.");
        string sourcePath = SourceSnapshot.Resolve(root, file);
        var sites = MutationSites.Find(File.ReadAllText(sourcePath), file);
        if (sites.Count == 0) throw new InvalidDataException("No supported mutation sites in this C# file.");
        var snapshot = SourceSnapshot.Capture(root);
        string digest = SourceSnapshot.Digest(snapshot), commandsDigest = SourceSnapshot.HashFile(commandsPath);
        var commands = TestCommands.Load(commandsPath);
        string engine = typeof(MutationRunner).Assembly.ManifestModule.ModuleVersionId + ":" + Environment.Version;
        output = Path.GetFullPath(output);
        if (snapshot.Any(s => string.Equals(SourceSnapshot.Resolve(root, s.Path), output, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("A report cannot replace source files.");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        using var ownership = new FileStream(output + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var results = Resume(output, digest, commandsDigest, engine, sites).ToList();
        if (results.Count == sites.Count) return new(digest, commandsDigest, sites.Count, results.ToArray(), true, engine);
        string run = Path.Combine(Path.GetDirectoryName(output)!, "workers", Guid.NewGuid().ToString("N"));
        string worker = Path.Combine(run, "source");
        SourceSnapshot.Copy(root, worker, snapshot);
        Reports.Write(Path.Combine(run, "ownership.json"), new { sourceRoot = root, worker, sourceDigest = digest });
        await Baseline(commands, worker, Path.Combine(run, "baseline"));
        int end = maximum == 0 ? sites.Count : Math.Min(sites.Count, results.Count + maximum);
        for (int i = results.Count; i < end; i++)
        {
            var site = sites[i];
            var outcome = await Execute(site, commands, worker, Path.Combine(run, "site-" + i));
            results.Add(outcome);
            SourceSnapshot.Verify(root, snapshot);
            var current = new MutationReport(digest, commandsDigest, sites.Count, results.ToArray(), results.Count == sites.Count, engine);
            Reports.Write(output, current);
            Console.Error.WriteLine($"{i + 1}/{sites.Count}: {outcome.State} {site.UnitName}:{site.Line}");
            if (outcome.State == MutationState.Unconfirmed) break;
        }
        SourceSnapshot.Verify(root, snapshot);
        return new(digest, commandsDigest, sites.Count, results.ToArray(), results.Count == sites.Count, engine);
    }

    private static MutationOutcome[] Resume(string output, string source, string commands, string engine, IReadOnlyList<MutationSite> sites)
    {
        if (!File.Exists(output)) return [];
        var previous = JsonSerializer.Deserialize<MutationReport>(File.ReadAllText(output), Reports.Json)
            ?? throw new InvalidDataException("Mutation report is empty.");
        if (previous.SourceDigest != source || previous.CommandsDigest != commands || previous.EngineId != engine
            || !MatchesCatalog(previous, sites))
            throw new InvalidDataException("Source, tests or mutation inventory changed; use a new report path.");
        if (previous.Results.Any(r => r.State == MutationState.Unconfirmed))
            throw new InvalidDataException("Resolve the timed-out run before resuming; its worker and logs were retained.");
        return previous.Results;
    }

    private static bool MatchesCatalog(MutationReport report, IReadOnlyList<MutationSite> sites)
    {
        if (report.TotalSites != sites.Count || report.Results is null || report.Results.Length > sites.Count) return false;
        return report.Results.All(r => r is not null && Enum.IsDefined(r.State))
            && report.Results.Select(r => r.SiteId).SequenceEqual(sites.Take(report.Results.Length).Select(s => s.Id));
    }

    private static async Task Baseline(TestCommands commands, string worker, string logs)
    {
        var build = await Commands.Run(commands.Build, worker, Path.Combine(logs, "build.txt"), commands.TimeoutSeconds);
        if (!build.Passed || !await Commands.Tests(commands, worker, logs))
            throw new InvalidDataException("Baseline build/tests failed in the private worker: " + logs);
    }

    private static async Task<MutationOutcome> Execute(MutationSite site, TestCommands commands, string worker, string logs)
    {
        string path = SourceSnapshot.Resolve(worker, site.Path);
        byte[] original = File.ReadAllBytes(path);
        using var reader = new StreamReader(new MemoryStream(original), detectEncodingFromByteOrderMarks: true);
        if (reader.ReadToEnd() != site.Source) throw new IOException("Worker source no longer matches the mutation baseline.");
        var encoding = reader.CurrentEncoding;
        string changed = site.Apply();
        await File.WriteAllTextAsync(path, changed, encoding);
        try
        {
            var build = await Commands.Run(commands.Build, worker, Path.Combine(logs, "build.txt"), commands.TimeoutSeconds);
            var tests = new List<CommandResult>();
            if (build.Passed)
                for (int i = 0; i < commands.Tests.Length; i++)
                {
                    var test = await Commands.Run(commands.Tests[i], worker, Path.Combine(logs, $"test-{i}.txt"), commands.TimeoutSeconds);
                    tests.Add(test);
                    if (!test.Passed) break;
                }
            return new(site.Id, site.UnitName, site.Line, Classify(build, tests), logs);
        }
        finally
        {
            if (File.ReadAllText(path) != changed) throw new IOException("Another writer changed the private worker; source was retained.");
            await File.WriteAllBytesAsync(path, original);
        }
    }
}
