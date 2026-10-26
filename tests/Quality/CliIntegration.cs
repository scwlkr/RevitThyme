using System.Text.Json;
using RevitThyme.Quality;
using QualityProgram = RevitThyme.Quality.Program;

internal static class CliIntegration
{
    private sealed record Reply(int Exit, string Output, string Error);
    private static async Task<Reply> Invoke(params string[] args)
    {
        var output = new StringWriter(); var error = new StringWriter();
        var priorOutput = Console.Out; var priorError = Console.Error;
        try
        {
            Console.SetOut(output); Console.SetError(error);
            return new(await QualityProgram.Main(args), output.ToString(), error.ToString());
        }
        finally { Console.SetOut(priorOutput); Console.SetError(priorError); }
    }

    public static async Task Run(Action<string, bool> check)
    {
        var help = await Invoke();
        check("quality CLI explains its commands without a repository", help.Exit == 0 && help.Output.Contains("dryer | mutator", StringComparison.Ordinal));
        check("quality CLI explicit help succeeds", (await Invoke("--help")).Exit == 0);
        string root = Path.Combine(Path.GetTempPath(), "RevitThyme-Quality-CLI-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "src")); Directory.CreateDirectory(Path.Combine(root, "single"));
        const string body = "int temp = n + 1;\nint other = temp * 2;\nreturn temp + other;";
        File.WriteAllText(Path.Combine(root, "src", "C.cs"), "class C {\nint First(int n) {\n" + body + "\n}\nint Second(int n) {\n" + body + "\n}\n}");
        File.WriteAllText(Path.Combine(root, "single", "C.cs"), "class C { int First(int n) => n + 1; }");
        File.WriteAllText(Path.Combine(root, ".gitignore"), "artifacts/\n");
        var initialized = await Commands.Run(["git", "init", "--quiet"], root, Path.Combine(root, "artifacts", "init.txt"), 30);
        check("CLI fixture owns a private Git inventory", initialized.Passed);
        foreach (string[] arguments in new string[][]
        {
            ["unknown"], ["dryer", "--unknown", "x"], ["dryer", "--source"], ["dryer", "--source", "src", "--source", "single"],
            ["dryer", "--threshold", "bad"], ["dryer", "--threshold", "0"], ["dryer", "--min-lines", "999999999999999999"],
            ["dryer", "--report", "source.json"], ["dryer", "--source", "missing"], ["mutator"],
            ["mutator", "--file", "../outside.cs", "--scan"], ["mutator", "--file", "src/C.cs", "--coverage", "missing.xml"]
        })
        {
            var reply = await Invoke([.. arguments, "--root", root]);
            check("quality CLI rejects invalid invocation: " + string.Join(' ', arguments), reply.Exit == 2 && reply.Error.Length > 0);
        }
        var dry = await Invoke("dryer", "--root", root);
        using (var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "artifacts", "quality", "dry.json"))))
            check("dryer CLI returns a review gate with actual duplicate pairs", dry.Exit == 3 && report.RootElement.GetProperty("pairs").GetArrayLength() == 1);
        check("dryer CLI passes a single-body source", (await Invoke("dryer", "--root", root, "--source", "single")).Exit == 0);
        string scan = Path.Combine(root, "artifacts", "scan.json");
        var scanned = await Invoke("mutator", "--root", root, "--file", "single/C.cs", "--scan", "--report", scan);
        using (var report = JsonDocument.Parse(File.ReadAllText(scan)))
            check("mutation CLI scan retains real sites without claiming executed tests", scanned.Exit == 0 && report.RootElement.GetProperty("sites").GetArrayLength() == 2);
        string coverage = Path.Combine(root, "coverage.xml");
        File.WriteAllText(coverage, "<coverage><packages><package><classes><class filename=\"single/C.cs\"><lines><line number=\"1\" hits=\"1\"/></lines></class></classes></package></packages></coverage>");
        var annotated = await Invoke("mutator", "--root", root, "--file", "single/C.cs", "--scan", "--coverage", coverage, "--report", scan);
        using (var report = JsonDocument.Parse(File.ReadAllText(scan)))
            check("mutation CLI coverage annotations retain observed hits", annotated.Exit == 0 && report.RootElement.GetProperty("sites").EnumerateArray().All(row => row.GetProperty("covered").GetBoolean()));
        File.WriteAllText(coverage, "<coverage>");
        check("invalid XML is a failed CLI boundary", (await Invoke("mutator", "--root", root, "--file", "single/C.cs", "--scan", "--coverage", coverage)).Exit == 2);
        File.WriteAllText(Path.Combine(root, "single", "C.cs"), "class C { void Empty() {} }");
        check("a mutation scan without sites remains explicitly unconfirmed", (await Invoke("mutator", "--root", root, "--file", "single/C.cs", "--scan")).Exit == 4);
        File.WriteAllText(Path.Combine(root, "single", "C.cs"), "class C {}");
        check("a source tree without executable bodies cannot pass dryer", (await Invoke("dryer", "--root", root, "--source", "single")).Exit == 2);
        await Configurations(root, check);
        Console.WriteLine("Quality CLI evidence: " + root);
    }

    private static async Task Configurations(string root, Action<string, bool> check)
    {
        string config = Path.Combine(root, "commands.json");
        string nullReceipt = JsonSerializer.Serialize(new { root, revision = (string?)null, sourceDigest = "", commandsDigest = "", sources = (object?)null, reports = Array.Empty<SnapshotFile>() });
        bool nullRejected = false;
        try { JsonSerializer.Deserialize<CoverageReceipt>(nullReceipt, Reports.Json); } catch (JsonException) { nullRejected = true; }
        check("nonnull receipt inputs cannot deserialize as explicit null", nullRejected);
        foreach (string text in new[] { "{}", "null", "{", "{\"build\":[],\"tests\":[[\"dotnet\"]]}",
            "{\"build\":[\"dotnet\"],\"tests\":[]}", "{\"build\":[\"dotnet\"],\"tests\":[[]]}",
            "{\"build\":[\"\"],\"tests\":[[\"dotnet\"]]}", "{\"build\":[\"dotnet\",null],\"tests\":[[\"dotnet\"]]}",
            "{\"build\":[\"dotnet\"],\"tests\":[[\"dotnet\"]],\"timeoutSeconds\":0}",
            "{\"build\":[\"dotnet\"],\"tests\":[[\"dotnet\"]],\"timeoutSeconds\":3601}",
            "{\"build\":[\"dotnet\"],\"tests\":[[\"dotnet\"]],\"toolTests\":[[]]}" })
        {
            File.WriteAllText(config, text);
            bool rejected = false;
            try { TestCommands.Load(config); } catch (Exception error) when (error is InvalidDataException or JsonException) { rejected = true; }
            check("invalid command manifests fail before process execution", rejected);
        }
        Reports.Write(config, new TestCommands(["dotnet"], [["dotnet"]], 3600, [["dotnet"]]));
        var commands = TestCommands.Load(config);
        check("valid tool suites and inclusive timeout limits survive parsing", commands.AllTests().Length == 2 && commands.TimeoutSeconds == 3600);
        File.WriteAllText(config, "{");
        check("malformed command JSON is a failed CLI boundary", (await Invoke("mutator", "--root", root, "--file", "src/C.cs", "--config", config)).Exit == 2);
        Reports.Write(config, new TestCommands(["revitthyme-quality-missing-" + Guid.NewGuid().ToString("N")], [["dotnet"]]));
        check("an unavailable executable is a failed CLI boundary", (await Invoke("mutator", "--root", root, "--file", "src/C.cs", "--config", config)).Exit == 2);
    }
}
