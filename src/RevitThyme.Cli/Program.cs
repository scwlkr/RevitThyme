using System.Globalization;
using System.Text.Json;
using RevitThyme.Core;

namespace RevitThyme.Cli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try { return await RunAsync(args, Console.Out, Console.Error); }
        catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException or JsonException or InvalidOperationException or FormatException or OverflowException or System.ComponentModel.Win32Exception or HttpRequestException or OperationCanceledException or KeyNotFoundException)
        { Console.Error.WriteLine(error.Message); return 2; }
    }
    public static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error,
        Func<SessionBinding, IOperationClient>? clientFactory = null)
    {
        if (args.Length == 0 || args is ["--help"] or ["help"])
        {
            await output.WriteLineAsync("""
                RevitThyme
                revitthyme --version | version [--json]
                revitthyme update check [--json] [--version VERSION]
                revitthyme update download --destination ABSOLUTE_FOLDER [--version VERSION]
                revitthyme update install [--version VERSION]
                revitthyme sessions [--directory ABSOLUTE_PATH] [--json]
                revitthyme inspect --session ABSOLUTE_FILE [--json]
                revitthyme rename-view --session ABSOLUTE_FILE --capture ABSOLUTE_FILE --name NAME [--json]
                revitthyme view-range get --session ABSOLUTE_FILE --json
                revitthyme view-range set --session ABSOLUTE_FILE --capture RANGE_RESULT --range SETTINGS_FILE --json
                revitthyme room-dimensions preview --session ABSOLUTE_FILE --room UNIQUE_ID [--inset-feet 1.5] [--tag-clearance-feet 1] --json
                revitthyme room-dimensions create --session ABSOLUTE_FILE --capture PREVIEW_RESULT --json
                revitthyme run-query|run-modify --session ABSOLUTE_FILE --capture INSPECTION --source CS_BODY_FILE --trusted true --request-id GUID [--budget-ms 10000] --json
                revitthyme get-operation --session ABSOLUTE_FILE --request-id GUID --json

                Write inspection JSON to a capture file, review it, then rename that exact view.
                Capture files contain document paths; keep them private. No model save or sync occurs.
                """); return 0;
        }
        if (args is ["--version"]) { await output.WriteLineAsync(ProductVersion.Build); return 0; }
        if (args[0] is "version" or "update") return await UpdateCli.Run(args, output);
        if (args[0] is "run-query" or "run-modify" or "get-operation") return await DynamicCli.Run(args, output, error, clientFactory);
        if (args.Length >= 2 && args[0] is "view-range" or "room-dimensions") args = [args[0] + "-" + args[1], .. args[2..]];
        var options = Parse(args[1..]);
        string command = args[0];
        string[] allowed = command switch
        {
            "sessions" => ["--directory", "--json"], "inspect" => ["--session", "--json"],
            "rename-view" => ["--session", "--capture", "--name", "--json"],
            "view-range-get" => ["--session", "--json"],
            "view-range-set" => ["--session", "--capture", "--range", "--json"],
            "room-dimensions-preview" => ["--session", "--room", "--inset-feet", "--tag-clearance-feet", "--json"],
            "room-dimensions-create" => ["--session", "--capture", "--json"],
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
        var client = clientFactory?.Invoke(binding) ?? new PipeClient(binding);
        OperationRequest request;
        if (command == "inspect") request = new(Guid.NewGuid(), OperationKind.Inspect);
        else if (command is "view-range-get" or "room-dimensions-preview")
        {
            var context = await client.ExecuteAsync(new(Guid.NewGuid(), OperationKind.Inspect));
            if (!context.Succeeded) { await output.WriteLineAsync(JsonSerializer.Serialize(context, Protocol.Json)); return 1; }
            request = command == "view-range-get" ? new(Guid.NewGuid(), OperationKind.GetViewRange, context.Snapshot!.Target)
                : new(Guid.NewGuid(), OperationKind.PreviewRoomDimensions, context.Snapshot!.Target, ElementIds: [Required(options, "--room")],
                    RoomOptions: new(double.Parse(options.GetValueOrDefault("--inset-feet") ?? "1.5", CultureInfo.InvariantCulture),
                        double.Parse(options.GetValueOrDefault("--tag-clearance-feet") ?? "1", CultureInfo.InvariantCulture)));
        }
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
            request = command switch
            {
                "rename-view" => new(Guid.NewGuid(), OperationKind.RenameView, snapshot.Target, snapshot.ViewName, Required(options, "--name")),
                "view-range-set" when capture.ViewRange is { } range => new(Guid.NewGuid(), OperationKind.SetViewRange, snapshot.Target,
                    ExpectedRange: range.Settings, Range: ReadRange(Required(options, "--range"))),
                "room-dimensions-create" when capture.RoomDimensions is { } preview => new(Guid.NewGuid(), OperationKind.CreateRoomDimensions, snapshot.Target,
                    ElementIds: [preview.RoomId], RoomOptions: preview.Options, RoomPreview: preview),
                _ => throw new ArgumentException("Capture must contain the native utility result for this command.")
            };
        }
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
    private static ViewRangeSettings ReadRange(string path)
    {
        if (!Path.IsPathFullyQualified(path) || new FileInfo(path).Length > Protocol.MaxBytes) throw new ArgumentException("Use an absolute range settings file (maximum 64 KiB).");
        return JsonSerializer.Deserialize<ViewRangeSettings>(File.ReadAllText(path), Protocol.Json) ?? throw new ArgumentException("Range settings required.");
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
