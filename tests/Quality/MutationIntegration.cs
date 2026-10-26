using System.Text.Json;
using RevitThyme.Quality;

internal static class MutationIntegration
{
    public static async Task Run(Action<string, bool> check)
    {
        string root = Path.Combine(Path.GetTempPath(), "RevitThyme-Quality-Integration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "src"));
        string file = Path.Combine(root, "src", "Math.cs"), config = Path.Combine(root, "commands.json");
        File.WriteAllText(Path.Combine(root, ".gitignore"), "artifacts/\nbin/\nobj/\ntarget/\ncoverage/\nTestResults/\n");
        File.WriteAllText(Path.Combine(root, "Fixture.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable><DefaultItemExcludes>$(DefaultItemExcludes);artifacts/**</DefaultItemExcludes></PropertyGroup></Project>");
        File.WriteAllText(Path.Combine(root, "Program.cs"), "return MathFixture.Choose(1) == 2 && MathFixture.Choose(0) == 1 ? 0 : 1;");
        string original = "public static class MathFixture { public static int Choose(int n) => n + 1; }";
        File.WriteAllText(file, original, System.Text.Encoding.Unicode);
        byte[] originalBytes = File.ReadAllBytes(file);
        Reports.Write(config, new TestCommands(["dotnet", "build", "Fixture.csproj", "-c", "Release", "--nologo", "-p:UseSharedCompilation=false"],
            [["dotnet", "bin/Release/net10.0/Fixture.dll"]], 60));
        var initialized = await Commands.Run(["git", "init", "--quiet"], root, Path.Combine(root, "artifacts", "init.txt"), 30);
        if (!initialized.Passed) throw new InvalidDataException("Cannot initialize the private mutation test fixture.");
        string reportPath = Path.Combine(root, "artifacts", "mutations.json");
        var partial = await MutationRunner.Run(root, config, "src/Math.cs", reportPath, 1);
        check("bounded mutation run is explicitly incomplete", !partial.Complete && !partial.Passed && partial.Results is [{ State: MutationState.Killed }]);
        check("mutation runner preserves original source", File.ReadAllText(file) == original);
        string runDirectory = Path.GetDirectoryName(partial.Results[0].Logs)!;
        using (var owner = JsonDocument.Parse(File.ReadAllText(Path.Combine(runDirectory, "ownership.json"))))
        {
            string privateFile = Path.Combine(owner.RootElement.GetProperty("worker").GetString()!, "src", "Math.cs");
            check("worker restore preserves exact source encoding and bytes", File.ReadAllBytes(privateFile).SequenceEqual(originalBytes));
        }
        var complete = await MutationRunner.Run(root, config, "src/Math.cs", reportPath, 0);
        check("real compiled C# mutants are killed by executed tests", complete.Complete && complete.Passed && complete.Results.Length == 2
            && complete.Results.All(r => r.State == MutationState.Killed));
        var replay = await MutationRunner.Run(root, config, "src/Math.cs", reportPath, 0);
        check("complete unchanged mutation report replays without another worker", replay.Results.SequenceEqual(complete.Results));
        File.AppendAllText(file, "\n// changed source");
        bool stale = false;
        try { await MutationRunner.Run(root, config, "src/Math.cs", reportPath, 0); } catch (InvalidDataException) { stale = true; }
        check("changed source cannot reuse a green mutation report", stale);
        File.WriteAllText(file, original);
        File.WriteAllText(Path.Combine(root, "Program.cs"), "return MathFixture.Guard(1) == 2 && MathFixture.Guard(0) == 0 && MathFixture.Guard(\"x\") == 0 ? 0 : 1;");
        File.WriteAllText(file, "public static class MathFixture { public static int Guard(object value) { if(value is int n && n > 0) return n + 1; return 0; } }");
        var guarded = await MutationRunner.Run(root, config, "src/Math.cs", Path.Combine(root, "artifacts", "guarded.json"), 0);
        check("compile-invalid short-circuit mutation is separated from killed tests", guarded.Complete && guarded.Passed
            && guarded.Results.Any(r => r.State == MutationState.Invalid) && guarded.Results.Any(r => r.State == MutationState.Killed));
        if (Environment.GetEnvironmentVariable("REVITTHYME_QUALITY_COLLECTOR") is { Length: > 0 } collector)
            await CoverageIntegration.Run(root, config, collector, check);
        else Console.WriteLine("SKIP collector integration: run project.cmd quality prepare, then quality test.");
        Console.WriteLine("Mutation integration evidence: " + root);
    }
}
