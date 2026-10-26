using System.Globalization;
using System.Text.Json;

namespace RevitThyme.Quality;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length == 0 || args is ["--help"])
            {
                Console.WriteLine("C# quality companions: dryer | mutator | collect | coverage-status. Use --root PATH; reports go to artifacts/quality.");
                Console.WriteLine("dryer [--source src] [--threshold 0.8] [--min-lines 4] [--min-nodes 20] [--report PATH]");
                Console.WriteLine("mutator --file RELATIVE_CS [--scan] [--config tools/Quality/QualityCommands.json] [--max 0] [--report PATH]");
                Console.WriteLine("collect --collector ABSOLUTE_EXE [--config PATH] [--report PATH]; coverage-status validates retained source and report identities.");
                return 0;
            }
            var options = Options.Parse(args[1..]);
            return args[0] switch
            {
                "dryer" => Dry(options),
                "mutator" => await Mutate(options),
                "collect" => await Collect(options),
                "coverage-status" => CoverageStatus(options),
                _ => throw new ArgumentException("Unknown quality command. Use --help.")
            };
        }
        catch (Exception error) when (Expected(error))
        { Console.Error.WriteLine(error.Message); return 2; }
    }

    private static bool Expected(Exception error) => error is IOException or InvalidDataException or UnauthorizedAccessException || InvalidInput(error);
    private static bool InvalidInput(Exception error) => error is ArgumentException or InvalidOperationException or JsonException
        or System.ComponentModel.Win32Exception or FormatException or OverflowException or System.Xml.XmlException;

    private static int Dry(Options options)
    {
        options.Allow("--root", "--source", "--threshold", "--min-lines", "--min-nodes", "--report");
        var units = SourceAnalysis.ReadTree(options.Root, options.Get("--source", "src"));
        if (units.Count == 0) throw new InvalidDataException("No executable C# bodies found.");
        double threshold = double.Parse(options.Get("--threshold", "0.8"), CultureInfo.InvariantCulture);
        var pairs = Duplication.Find(units, threshold, options.Number("--min-lines", 4), options.Number("--min-nodes", 20));
        string output = options.ReportPath("artifacts/quality/dry.json");
        Reports.Write(output, new { units = units.Count, threshold, pairs });
        Console.WriteLine($"{units.Count} bodies, {pairs.Count} duplicate candidates. Report: {output}");
        return pairs.Count == 0 ? 0 : 3;
    }

    private static async Task<int> Mutate(Options options)
    {
        options.Allow("--root", "--file", "--scan", "--coverage", "--config", "--max", "--report");
        string file = options.Required("--file");
        if (options.Has("--coverage") && !options.Has("--scan")) throw new ArgumentException("Coverage annotations apply to scans; executed mutation runs never skip sites.");
        string folder = options.Has("--scan") ? "scans" : "mutations";
        string output = options.ReportPath("artifacts/quality/" + folder + "/" + System.IO.Path.GetFileNameWithoutExtension(file)
            + "-" + SourceAnalysis.Hash(file)[..12] + ".json");
        if (options.Has("--scan")) return Scan(options, file, output);
        var report = await MutationRunner.Run(options.Root, options.Path("--config", "tools/Quality/QualityCommands.json"),
            file, output, options.Number("--max", 0));
        Console.WriteLine($"{report.Results.Length}/{report.TotalSites} mutants processed. Complete: {report.Complete}. Passed: {report.Passed}. Report: {output}");
        foreach (var group in report.Results.GroupBy(r => r.State)) Console.WriteLine($"{group.Key}: {group.Count()}");
        return report.Passed ? 0 : report.Complete ? 3 : 4;
    }

    private static int Scan(Options options, string file, string output)
    {
        var sites = MutationSites.Find(File.ReadAllText(SourceSnapshot.Resolve(options.Root, file)), file);
        var coverage = options.Has("--coverage") ? Coverage.Load(options.Root, options.Path("--coverage", "")) : null;
        Reports.Write(output, new { sites = sites.Select(site => new { site, covered = coverage?.Hit(options.Root, site.Path, site.Line) }) });
        Console.WriteLine($"{sites.Count} mutation sites. Scan only; no tests run. Report: {output}");
        return sites.Count == 0 ? 4 : 0;
    }

    private static async Task<int> Collect(Options options)
    {
        options.Allow("--root", "--collector", "--config", "--report");
        string output = options.ReportPath("artifacts/quality/coverage.json");
        var receipt = await CoverageCollection.Collect(options.Root, options.Path("--config", "tools/Quality/QualityCommands.json"),
            options.Required("--collector"), output);
        Console.WriteLine($"Coverage verified for {receipt.Reports.Length} suites at source digest {receipt.SourceDigest}. Receipt: {output}");
        return 0;
    }

    private static int CoverageStatus(Options options)
    {
        options.Allow("--root", "--config", "--report");
        var receipt = CoverageCollection.Validate(options.Root, options.Path("--config", "tools/Quality/QualityCommands.json"),
            options.ReportPath("artifacts/quality/coverage.json"));
        Console.WriteLine($"Source and {receipt.Reports.Length} coverage reports match the retained receipt.");
        return 0;
    }
}

internal sealed class Options(Dictionary<string, string> values)
{
    public string Root => System.IO.Path.GetFullPath(Get("--root", Environment.CurrentDirectory));
    public bool Has(string key) => values.ContainsKey(key);
    public string Get(string key, string fallback) => values.GetValueOrDefault(key) ?? fallback;
    public string Required(string key) => values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
        ? value : throw new ArgumentException("Required: " + key);
    public int Number(string key, int fallback) => int.Parse(Get(key, fallback.ToString(CultureInfo.InvariantCulture)), CultureInfo.InvariantCulture);
    public string Path(string key, string fallback) => System.IO.Path.GetFullPath(System.IO.Path.Combine(Root, Get(key, fallback)));
    public string ReportPath(string fallback)
    {
        string output = Path("--report", fallback);
        string prefix = System.IO.Path.Combine(Root, "artifacts") + System.IO.Path.DirectorySeparatorChar;
        if (!output.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Quality reports must stay under this root's artifacts directory.");
        return output;
    }
    public void Allow(params string[] keys)
    { if (values.Keys.Except(keys).Any()) throw new ArgumentException("Unknown option for this quality command."); }
    public static Options Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < args.Length; i++)
        {
            string key = args[i];
            if (!key.StartsWith("--", StringComparison.Ordinal) || values.ContainsKey(key))
                throw new ArgumentException("Invalid or repeated option: " + key);
            if (key == "--scan") { values.Add(key, "true"); continue; }
            if (++i >= args.Length || args[i].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException("Missing value: " + key);
            values.Add(key, args[i]);
        }
        return new(values);
    }
}
