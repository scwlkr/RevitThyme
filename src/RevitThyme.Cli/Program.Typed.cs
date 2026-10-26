using System.Globalization;
using System.Text.Json;
using RevitThyme.Core;

namespace RevitThyme.Cli;

public static partial class Program
{
    private static async Task<int> RunTyped(string[] args, TextWriter output, TextWriter error,
        Func<SessionBinding, IOperationClient>? clientFactory)
    {
        if (args.Length >= 2 && args[0] is "view-range" or "room-dimensions") args = [args[0] + "-" + args[1], .. args[2..]];
        var options = Parse(args[1..]);
        string command = args[0];
        ValidateOptions(command, options);
        if (command == "sessions") return await ListSessions(options, output);
        var binding = SessionBinding.Load(Required(options, "--session"));
        var client = clientFactory?.Invoke(binding) ?? new PipeClient(binding);
        return command switch
        {
            "inspect" => await Execute(new(Guid.NewGuid(), OperationKind.Inspect), options, client, output, error),
            "list-documents" => await ListDocuments(options, binding, client, output, error),
            "inspect-elements" => await InspectElements(options, binding, client, output, error),
            "view-range-get" or "room-dimensions-preview" => await PreviewUtility(command, options, client, output, error),
            _ => await Execute(CapturedRequest(command, options, binding), options, client, output, error)
        };
    }

    private static void ValidateOptions(string command, Dictionary<string, string?> options)
    {
        string[] allowed = command switch
        {
            "sessions" => ["--directory", "--json"], "inspect" => ["--session", "--json"],
            "list-documents" => ["--session", "--capture", "--offset", "--limit", "--request-id", "--json"],
            "inspect-elements" => ["--session", "--element", "--capture", "--parameter-offset", "--json"],
            "rename-view" => ["--session", "--capture", "--name", "--json"],
            "view-range-get" => ["--session", "--json"],
            "view-range-set" => ["--session", "--capture", "--range", "--json"],
            "room-dimensions-preview" => ["--session", "--room", "--inset-feet", "--tag-clearance-feet", "--json"],
            "room-dimensions-create" => ["--session", "--capture", "--json"],
            _ => throw new ArgumentException("Unknown command. Run revitthyme --help.")
        };
        if (options.Keys.Except(allowed).Any()) throw new ArgumentException("Unknown option for " + command + ".");
    }

    private static async Task<int> ListSessions(Dictionary<string, string?> options, TextWriter output)
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

    private static async Task<int> ListDocuments(Dictionary<string, string?> options, SessionBinding binding,
        IOperationClient client, TextWriter output, TextWriter error)
    {
        var query = ReadDocumentQuery(options);
        Guid id = ReadRequestId(options);
        var context = await ReadContext(options, binding, client, query.Offset,
            "Use --capture with the previous document page to continue.");
        if (!context.Succeeded)
        {
            await output.WriteLineAsync(JsonSerializer.Serialize(context, Protocol.Json));
            await error.WriteLineAsync(context.Message); return 1;
        }
        query = ContinueDocuments(query, context.Documents);
        var request = new OperationRequest(id, OperationKind.ListDocuments, context.Snapshot!.Target, DocumentQuery: query);
        return await Execute(request, options, client, output, error);
    }

    private static DocumentQuery ReadDocumentQuery(Dictionary<string, string?> options)
    {
        if (!int.TryParse(options.GetValueOrDefault("--offset") ?? "0", NumberStyles.Integer, CultureInfo.InvariantCulture, out int offset)
            || offset is < 0 or > 4096
            || !int.TryParse(options.GetValueOrDefault("--limit") ?? "50", NumberStyles.Integer, CultureInfo.InvariantCulture, out int limit)
            || limit is < 1 or > 100)
            throw new ArgumentException("Use offset 0–4096 and limit 1–100.");
        return new(offset, limit);
    }

    private static Guid ReadRequestId(Dictionary<string, string?> options)
    {
        Guid id = Guid.NewGuid();
        if (options.TryGetValue("--request-id", out var suppliedId) && (!Guid.TryParse(suppliedId, out id) || id == Guid.Empty))
            throw new ArgumentException("request-id must be a non-empty GUID.");
        return id;
    }

    private static DocumentQuery ContinueDocuments(DocumentQuery query, DocumentPage? page)
    {
        if (query.Offset > 0 && (page is null || page.NextOffset != query.Offset))
            throw new ArgumentException("Continuation requires the previous document page and its exact nextOffset.");
        if (page is not null && (page.InventoryToken is not { Length: 64 } token || !token.All(Uri.IsHexDigit)))
            throw new ArgumentException("Document page capture must contain a valid inventoryToken.");
        return query with { InventoryToken = page?.InventoryToken };
    }

    private static async Task<int> InspectElements(Dictionary<string, string?> options, SessionBinding binding,
        IOperationClient client, TextWriter output, TextWriter error)
    {
        int offset = int.Parse(options.GetValueOrDefault("--parameter-offset") ?? "0", CultureInfo.InvariantCulture);
        var context = await ReadContext(options, binding, client, offset,
            "Use --capture with the previous result to continue parameter pages at the same revision.");
        if (!context.Succeeded) { await output.WriteLineAsync(JsonSerializer.Serialize(context, Protocol.Json)); return 1; }
        var request = new OperationRequest(Guid.NewGuid(), OperationKind.InspectElements, context.Snapshot!.Target,
            ElementIds: [Required(options, "--element")], ParameterOffset: offset);
        return await Execute(request, options, client, output, error);
    }

    private static async Task<OperationResult> ReadContext(Dictionary<string, string?> options, SessionBinding binding,
        IOperationClient client, int offset, string continuationMessage)
    {
        if (options.ContainsKey("--capture")) return ReadCapture(Required(options, "--capture"), binding);
        if (offset != 0) throw new ArgumentException(continuationMessage);
        return await client.ExecuteAsync(new(Guid.NewGuid(), OperationKind.Inspect));
    }

    private static async Task<int> PreviewUtility(string command, Dictionary<string, string?> options,
        IOperationClient client, TextWriter output, TextWriter error)
    {
        var context = await client.ExecuteAsync(new(Guid.NewGuid(), OperationKind.Inspect));
        if (!context.Succeeded) { await output.WriteLineAsync(JsonSerializer.Serialize(context, Protocol.Json)); return 1; }
        OperationRequest request = command == "view-range-get" ? new(Guid.NewGuid(), OperationKind.GetViewRange, context.Snapshot!.Target)
            : new(Guid.NewGuid(), OperationKind.PreviewRoomDimensions, context.Snapshot!.Target, ElementIds: [Required(options, "--room")],
                RoomOptions: new(double.Parse(options.GetValueOrDefault("--inset-feet") ?? "1.5", CultureInfo.InvariantCulture),
                    double.Parse(options.GetValueOrDefault("--tag-clearance-feet") ?? "1", CultureInfo.InvariantCulture)));
        return await Execute(request, options, client, output, error);
    }

    private static OperationRequest CapturedRequest(string command, Dictionary<string, string?> options, SessionBinding binding)
    {
        var capture = ReadCapture(Required(options, "--capture"), binding);
        var snapshot = capture.Snapshot!;
        return command switch
        {
            "rename-view" => new(Guid.NewGuid(), OperationKind.RenameView, snapshot.Target, snapshot.ViewName, Required(options, "--name")),
            "view-range-set" when capture.ViewRange is { } range => new(Guid.NewGuid(), OperationKind.SetViewRange, snapshot.Target,
                ExpectedRange: range.Settings, Range: ReadRange(Required(options, "--range"))),
            "room-dimensions-create" when capture.RoomDimensions is { } preview => new(Guid.NewGuid(), OperationKind.CreateRoomDimensions, snapshot.Target,
                ElementIds: [preview.RoomId], RoomOptions: preview.Options, RoomPreview: preview),
            _ => throw new ArgumentException("Capture must contain the native utility result for this command.")
        };
    }

    private static async Task<int> Execute(OperationRequest request, Dictionary<string, string?> options,
        IOperationClient client, TextWriter output, TextWriter error)
    {
        var result = await client.ExecuteAsync(request);
        return await WriteResult(result, options.ContainsKey("--json"), output, error);
    }

    private static async Task<int> WriteResult(OperationResult result, bool json, TextWriter output, TextWriter error)
    {
        if (json) await output.WriteLineAsync(JsonSerializer.Serialize(result, Protocol.Json));
        else
        {
            await output.WriteLineAsync($"{result.Code}: {result.Message}");
            if (result.Snapshot is { } s)
                await output.WriteLineAsync($"{s.DocumentTitle} / {s.ViewName} ({s.ViewType}, view {s.ViewElementId.ToString(CultureInfo.InvariantCulture)})");
            if (result.Details is { } details)
                await output.WriteLineAsync(JsonSerializer.Serialize(details, Protocol.Json));
            if (result.Documents is { } documents)
                await output.WriteLineAsync(JsonSerializer.Serialize(documents, Protocol.Json));
        }
        if (!result.Succeeded) { await error.WriteLineAsync(result.Message); return 1; }
        return 0;
    }
    private static OperationResult ReadCapture(string path, SessionBinding binding)
    {
        if (!Path.IsPathFullyQualified(path) || new FileInfo(path).Length > Protocol.MaxOperationBytes)
            throw new ArgumentException("Use an absolute inspection capture file (maximum 64 KiB).");
        var capture = JsonSerializer.Deserialize<OperationResult>(File.ReadAllText(path), Protocol.Json);
        if (capture?.Succeeded != true || capture.Snapshot is not { } snapshot)
            throw new ArgumentException("Capture must contain a successful inspection result.");
        if (snapshot.Target.ProcessId != binding.ProcessId || snapshot.Target.ProcessStart != binding.ProcessStart
            || snapshot.Target.SessionId != binding.SessionId)
            throw new ArgumentException("Capture belongs to a different Revit session.");
        return capture;
    }
    private static ViewRangeSettings ReadRange(string path)
    {
        if (!Path.IsPathFullyQualified(path) || new FileInfo(path).Length > Protocol.MaxOperationBytes) throw new ArgumentException("Use an absolute range settings file (maximum 64 KiB).");
        return JsonSerializer.Deserialize<ViewRangeSettings>(File.ReadAllText(path), Protocol.Json) ?? throw new ArgumentException("Range settings required.");
    }
}
