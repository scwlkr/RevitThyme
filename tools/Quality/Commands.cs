using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RevitThyme.Quality;

public sealed record TestCommands(string[] Build, string[][] Tests, int TimeoutSeconds = 180, string[][]? ToolTests = null)
{
    public string[][] AllTests() => Tests.Concat(ToolTests ?? []).ToArray();
    public static TestCommands Load(string path)
    {
        var commands = JsonSerializer.Deserialize<TestCommands>(File.ReadAllText(path), Reports.Json)
            ?? throw new InvalidDataException("Test command configuration is required.");
        if (commands.Build is not { Length: > 0 } || commands.Tests is not { Length: > 0 }
            || commands.AllTests().Any(test => test is not { Length: > 0 }) || commands.TimeoutSeconds is < 1 or > 3600)
            throw new InvalidDataException("Supply build and test argument arrays with timeout 1–3600 seconds.");
        if (commands.AllTests().Prepend(commands.Build).Any(command => string.IsNullOrWhiteSpace(command[0]) || command.Any(arg => arg is null)))
            throw new InvalidDataException("Command executables must be nonempty and arguments cannot be null.");
        return commands;
    }
}

public sealed record CommandResult(int? ExitCode, bool TimedOut, double Seconds, bool CompilerFailure = false)
{
    public bool Passed => !TimedOut && ExitCode == 0;
}

public static class Commands
{
    public static async Task<CommandResult> Run(string[] command, string root, string log, int timeoutSeconds)
    {
        if (command.Length == 0) throw new ArgumentException("Executable is required.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(log))!);
        var start = new ProcessStartInfo(command[0])
        { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (string arg in command[1..]) start.ArgumentList.Add(arg);
        using var process = new Process { StartInfo = start };
        var clock = Stopwatch.StartNew();
        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        bool timedOut = false;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            timedOut = true;
            // Only this command's child tree is owned by this runner.
            process.Kill(entireProcessTree: true); await process.WaitForExitAsync();
        }
        string output = await stdout + Environment.NewLine + await stderr;
        await File.WriteAllTextAsync(log, output);
        bool compilerFailure = System.Text.RegularExpressions.Regex.IsMatch(output, @"\berror\s+CS\d{4}\b",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        return new(timedOut ? null : process.ExitCode, timedOut, clock.Elapsed.TotalSeconds, compilerFailure);
    }

    public static async Task<bool> Tests(TestCommands commands, string root, string directory)
    {
        for (int i = 0; i < commands.Tests.Length; i++)
            if (!(await Run(commands.Tests[i], root, Path.Combine(directory, $"test-{i}.txt"), commands.TimeoutSeconds)).Passed) return false;
        return true;
    }
}

public static class Reports
{
    public static readonly JsonSerializerOptions Json = CreateJson();
    private static JsonSerializerOptions CreateJson()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true,
            RespectRequiredConstructorParameters = true, WriteIndented = true };
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        return options;
    }
    public static void Write(string path, object report)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temporary = path + ".writing-" + Guid.NewGuid().ToString("N");
        try
        {
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(report, Json);
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(bytes); stream.Flush(flushToDisk: true); }
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
