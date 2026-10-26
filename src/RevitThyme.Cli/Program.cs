using System.Text.Json;
using RevitThyme.Core;

namespace RevitThyme.Cli;

public static partial class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);
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
                RevitThyme (also available as rthyme)
                revitthyme --version | version [--json]
                revitthyme update check [--json] [--version VERSION]
                revitthyme update download --destination ABSOLUTE_FOLDER [--version VERSION]
                revitthyme update install [--version VERSION]
                revitthyme sessions [--directory ABSOLUTE_PATH] [--json]
                revitthyme inspect --session ABSOLUTE_FILE [--json]
                revitthyme list-documents --session ABSOLUTE_FILE [--capture PREVIOUS_RESULT --offset OFFSET] [--limit 50] [--request-id GUID] --json
                revitthyme inspect-elements --session ABSOLUTE_FILE --element UNIQUE_ID [--capture PREVIOUS_RESULT --parameter-offset OFFSET] [--json]
                revitthyme rename-view --session ABSOLUTE_FILE --capture ABSOLUTE_FILE --name NAME [--json]
                revitthyme view-range get --session ABSOLUTE_FILE --json
                revitthyme view-range set --session ABSOLUTE_FILE --capture RANGE_RESULT --range SETTINGS_FILE --json
                revitthyme room-dimensions preview --session ABSOLUTE_FILE --room UNIQUE_ID [--inset-feet 1.5] [--tag-clearance-feet 1] --json
                revitthyme room-dimensions create --session ABSOLUTE_FILE --capture PREVIEW_RESULT --json
                revitthyme run-query|run-modify --session ABSOLUTE_FILE --capture INSPECTION --source CS_BODY_FILE --trusted true --request-id GUID [--budget-ms 10000] --json
                revitthyme get-operation --session ABSOLUTE_FILE --request-id GUID --json
                revitthyme tool save --id GUID --definition ABSOLUTE_JSON --json
                revitthyme tool show --id GUID --json
                revitthyme tool run --id GUID --digest SHA256 --inputs ABSOLUTE_JSON --session ABSOLUTE_FILE --capture INSPECTION --request-id GUID --trusted true [--test NAME] [--budget-ms 10000] --json
                revitthyme tool verify --id GUID --digest SHA256 --inputs ABSOLUTE_JSON --session ABSOLUTE_FILE --request-id ORIGINAL_GUID --trusted true [--budget-ms 10000] --json
                revitthyme result-check prepare|verify --plan ABSOLUTE_JSON --inputs ABSOLUTE_JSON --source-sha256 SHA256 --request-id ORIGINAL_GUID --session ABSOLUTE_FILE --trusted true [--capture INSPECTION (prepare only)] [--budget-ms 10000] --json
                revitthyme timberfold inspect --session ABSOLUTE_FILE [--profile JSON] [--scope JSON] [--json]
                revitthyme timberfold preview --run-id GUID --session ABSOLUTE_FILE [--profile JSON] [--scope JSON] [--json]
                revitthyme timberfold generate --preview-run-id GUID --run-id GUID --session ABSOLUTE_FILE [--json]
                revitthyme timberfold reconcile --generation-run-id GUID --run-id NEW_GUID --session ABSOLUTE_FILE [--json]
                revitthyme timberfold verify --run-id GUID --session ABSOLUTE_FILE [--json]
                revitthyme timberfold status|cancel --run-id GUID --session ABSOLUTE_FILE [--json]

                Version reports distinguish the CLI/development build from installed RevitThyme.
                Update commands target the registered RevitThyme installation. Close Revit before installing.

                Write inspection JSON to a capture file, review it, then rename that exact view.
                Capture files contain document paths; keep them private. No model save or sync occurs.
                inspect-elements returns parameter IDs, raw values, isShared, sharedGuid, builtInParameter and native hasValue.
                Continue with --capture and nextParameterOffset until null; HasValue means assigned in Revit, even for zero/empty values.
                """); return 0;
        }
        if (args[0] is "--version" or "version" or "update") return await UpdateCli.Run(args, output);
        if (args[0] is "run-query" or "run-modify" or "get-operation") return await DynamicCli.Run(args, output, error, clientFactory);
        if (args[0] == "tool") return await SavedToolCli.Run(args, output, error, clientFactory);
        if (args[0] == "result-check") return await ResultCheckCli.Run(args, output, error, clientFactory);
        if (args[0] == "timberfold") return await TimberFoldCli.Run(args, output, error, clientFactory);
        return await RunTyped(args, output, error, clientFactory);
    }
    internal static Dictionary<string, string?> Parse(string[] args)
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
    internal static string Required(Dictionary<string, string?> options, string name) =>
        options.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value : throw new ArgumentException("Required: " + name);
}
