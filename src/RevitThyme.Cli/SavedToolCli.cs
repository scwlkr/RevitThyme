using System.Globalization;
using System.Text.Json;
using RevitThyme.Core;

namespace RevitThyme.Cli;

internal static class SavedToolCli
{
    public static async Task<int> Run(string[] args, TextWriter output, TextWriter error, Func<SessionBinding, IOperationClient>? factory)
    {
        if (args.Length < 2 || args[1] is not ("save" or "show" or "run")) throw new ArgumentException("Use tool save, show or run. See --help.");
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        string[] allowed = args[1] switch
        {
            "save" => ["--id", "--definition", "--json"], "show" => ["--id", "--json"],
            _ => ["--id", "--digest", "--inputs", "--session", "--capture", "--request-id", "--trusted", "--test", "--budget-ms", "--json"]
        };
        for (int i = 2; i < args.Length; i++)
        {
            string key = args[i];
            if (!allowed.Contains(key) || options.ContainsKey(key)) throw new ArgumentException("Unknown/repeated option: " + key);
            options.Add(key, key == "--json" ? "true" : ++i < args.Length ? args[i] : throw new ArgumentException("Missing value: " + key));
        }
        string Required(string key) => options.GetValueOrDefault(key) ?? throw new ArgumentException("Required: " + key);
        T Read<T>(string path, int limit)
        {
            if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("Use absolute JSON paths.");
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > limit) throw new ArgumentException("JSON file exceeds its size limit.");
            return JsonSerializer.Deserialize<T>(stream, Protocol.Json) ?? throw new ArgumentException("JSON value missing.");
        }
        var store = new SavedToolStore();
        if (args[1] == "save")
        { await output.WriteLineAsync(JsonSerializer.Serialize(store.Save(Required("--id"), Read<SavedToolDefinition>(Required("--definition"), SavedTools.MaxDefinitionBytes)), Protocol.Json)); return 0; }
        var artifact = store.Get(Required("--id"));
        if (args[1] == "show") { await output.WriteLineAsync(JsonSerializer.Serialize(artifact, Protocol.Json)); return 0; }
        var code = SavedTools.Invocation(artifact, Required("--digest"), Read<JsonElement>(Required("--inputs"), SavedTools.MaxInputBytes),
            Required("--trusted") == "true", int.Parse(options.GetValueOrDefault("--budget-ms") ?? "10000", CultureInfo.InvariantCulture), options.GetValueOrDefault("--test"));
        var binding = SessionBinding.Load(Required("--session"));
        var capture = Read<OperationResult>(Required("--capture"), Protocol.MaxBytes);
        if (!capture.Succeeded || capture.Snapshot is not { } snapshot || snapshot.Target.ProcessId != binding.ProcessId
            || snapshot.Target.ProcessStart != binding.ProcessStart || snapshot.Target.SessionId != binding.SessionId)
            throw new ArgumentException("Successful inspection capture from this exact session required.");
        if (!Guid.TryParse(Required("--request-id"), out var id) || id == Guid.Empty) throw new ArgumentException("request-id must be a nonempty GUID.");
        var operation = options.ContainsKey("--test") || artifact.Definition.Mode == "query" ? OperationKind.RunQuery : OperationKind.RunModify;
        var result = await (factory?.Invoke(binding) ?? new PipeClient(binding)).ExecuteAsync(new(id, operation, snapshot.Target, Code: code));
        await output.WriteLineAsync(JsonSerializer.Serialize(result, Protocol.Json));
        if (!result.Succeeded) { await error.WriteLineAsync(result.Message); return 1; }
        return 0;
    }
}
