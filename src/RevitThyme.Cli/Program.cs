using System.Globalization;
using System.Text.Json;
using RevitThyme.Core;

namespace RevitThyme.Cli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try { return await RunAsync(args, Console.Out, Console.Error); }
        catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException or JsonException or InvalidOperationException or System.ComponentModel.Win32Exception)
        { Console.Error.WriteLine(error.Message); return 2; }
    }
    public static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error,
        Func<SessionBinding, IOperationClient>? clientFactory = null)
    {
        if (args.Length == 0 || args is ["--help"] or ["help"])
        {
            await output.WriteLineAsync("""
                RevitThyme
                revitthyme sessions [--directory ABSOLUTE_PATH] [--json]
                revitthyme inspect --session ABSOLUTE_FILE [--json]
                revitthyme rename-view --session ABSOLUTE_FILE --capture ABSOLUTE_FILE --name NAME [--json]

                Write inspection JSON to a capture file, review it, then rename that exact view.
                Capture files contain document paths; keep them private. No model save or sync occurs.
                """); return 0;
        }
        if (args is ["--version"]) { await output.WriteLineAsync("0.5.0-mcp"); return 0; }
        var options = Parse(args[1..]);
        string command = args[0];
        string[] allowed = command switch
        {
            "sessions" => ["--directory", "--json"], "inspect" => ["--session", "--json"],
            "rename-view" => ["--session", "--capture", "--name", "--json"],
            _ => throw new ArgumentException("Unknown command. Run revitthyme --help.")
        };
        if (options.Keys.Except(allowed).Any()) throw new ArgumentException("Unknown option for " + command + ".");
        if (command == "sessions")
        {
            string folder = options.GetValueOrDefault("--directory") ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RevitThyme", "sessions");
            if (!Path.IsPathFullyQualified(folder)) throw new ArgumentException("Use an absolute session directory.");
            var sessions = new List<object>();
            foreach (string file in Directory.Exists(folder) ? Directory.GetFiles(folder, "*.json").Order() : Enumerable.Empty<string>())
            {
                try { sessions.Add(new { file, binding = SessionBinding.Load(file) }); }
                catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException or JsonException or System.ComponentModel.Win32Exception) { }
            }
            await output.WriteLineAsync(JsonSerializer.Serialize(sessions, Protocol.Json)); return 0;
        }
        var binding = SessionBinding.Load(Required(options, "--session"));
        OperationRequest request;
        if (command == "inspect") request = new(Guid.NewGuid(), OperationKind.Inspect);
        else
        {
            string capturePath = Required(options, "--capture");
            if (!Path.IsPathFullyQualified(capturePath) || new FileInfo(capturePath).Length > Protocol.MaxBytes)
                throw new ArgumentException("Use an absolute inspection capture file (maximum 64 KiB).");
            var capture = JsonSerializer.Deserialize<OperationResult>(File.ReadAllText(capturePath), Protocol.Json);
            if (capture?.Succeeded != true || capture.Snapshot is not { } snapshot)
                throw new ArgumentException("Capture must contain a successful inspection result.");
            if (snapshot.Target.ProcessId != binding.ProcessId || snapshot.Target.ProcessStart != binding.ProcessStart
                || snapshot.Target.SessionId != binding.SessionId)
                throw new ArgumentException("Capture belongs to a different Revit session.");
            request = new(Guid.NewGuid(), OperationKind.RenameView, snapshot.Target, snapshot.ViewName, Required(options, "--name"));
        }
        var client = clientFactory?.Invoke(binding) ?? new PipeClient(binding);
        var result = await client.ExecuteAsync(request);
        if (options.ContainsKey("--json")) await output.WriteLineAsync(JsonSerializer.Serialize(result, Protocol.Json));
        else
        {
            await output.WriteLineAsync($"{result.Code}: {result.Message}");
            if (result.Snapshot is { } s)
                await output.WriteLineAsync($"{s.DocumentTitle} / {s.ViewName} ({s.ViewType}, view {s.ViewElementId.ToString(CultureInfo.InvariantCulture)})");
        }
        if (!result.Succeeded) { await error.WriteLineAsync(result.Message); return 1; }
        return 0;
    }
    private static Dictionary<string, string?> Parse(string[] args)
    {
        var options = new Dictionary<string, string?>(StringComparer.Ordinal);
        for (int i = 0; i < args.Length; i++)
        {
            string key = args[i];
            if (!key.StartsWith("--", StringComparison.Ordinal) || options.ContainsKey(key))
                throw new ArgumentException("Invalid or repeated option: " + key);
            string? value = key == "--json" ? null : ++i < args.Length && !args[i].StartsWith("--", StringComparison.Ordinal)
                ? args[i] : throw new ArgumentException("Missing value for " + key);
            options.Add(key, value);
        }
        return options;
    }
    private static string Required(Dictionary<string, string?> options, string name) =>
        options.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value : throw new ArgumentException("Required: " + name);
}
