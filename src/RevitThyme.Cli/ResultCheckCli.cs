using System.Globalization;
using System.Text.Json;
using RevitThyme.Core;

namespace RevitThyme.Cli;

internal static class ResultCheckCli
{
    public static async Task<int> Run(string[] args, TextWriter output, TextWriter error, Func<SessionBinding, IOperationClient>? factory)
    {
        if (args.Length < 2 || args[1] is not ("prepare" or "verify")) throw new ArgumentException("Use result-check prepare or verify.");
        string[] allowed = ["--plan", "--inputs", "--source-sha256", "--request-id", "--session", "--trusted", "--budget-ms", "--json", "--capture"];
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
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
            using var stream = File.OpenRead(path); if (stream.Length > limit) throw new ArgumentException("JSON file exceeds limit.");
            return JsonSerializer.Deserialize<T>(stream, Protocol.Json) ?? throw new ArgumentException("JSON value missing.");
        }
        var binding = SessionBinding.Load(Required("--session"));
        var coordinator = new ResultCheckCoordinator(factory?.Invoke(binding) ?? new PipeClient(binding), Guid.Parse(Required("--request-id")),
            Read<ResultCheckPlan>(Required("--plan"), SavedTools.MaxDefinitionBytes), Read<JsonElement>(Required("--inputs"), SavedTools.MaxInputBytes),
            Required("--source-sha256"), Required("--trusted") == "true", int.Parse(options.GetValueOrDefault("--budget-ms") ?? "10000", CultureInfo.InvariantCulture));
        if (args[1] == "prepare")
        {
            var capture = Read<OperationResult>(Required("--capture"), Protocol.MaxOperationBytes);
            if (!capture.Succeeded || capture.Snapshot is not { } snapshot || snapshot.Target.ProcessId != binding.ProcessId
                || snapshot.Target.ProcessStart != binding.ProcessStart || snapshot.Target.SessionId != binding.SessionId)
                throw new ArgumentException("Successful capture from this exact session required.");
            var result = await coordinator.Prepare(snapshot.Target);
            await output.WriteLineAsync(JsonSerializer.Serialize(result, Protocol.Json));
            if (!result.Receipt.Succeeded) { await error.WriteLineAsync(result.Receipt.Message); return 1; } return 0;
        }
        if (options.ContainsKey("--capture")) throw new ArgumentException("Verification uses the retained original target, not --capture.");
        var verified = await coordinator.Verify(); await output.WriteLineAsync(JsonSerializer.Serialize(verified, Protocol.Json));
        if (!verified.Succeeded || verified.Verification?.Status != "passed") { await error.WriteLineAsync(verified.Verification?.Message ?? verified.Message); return 1; }
        return 0;
    }
}
