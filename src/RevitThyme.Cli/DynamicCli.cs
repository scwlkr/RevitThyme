using System.Globalization;
using System.Text.Json;
using RevitThyme.Core;

namespace RevitThyme.Cli;

internal static class DynamicCli
{
    public static async Task<int> Run(string[] args, TextWriter output, TextWriter error, Func<SessionBinding, IOperationClient>? factory)
    {
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        string[] allowed = args[0] == "get-operation" ? ["--session", "--request-id", "--json"]
            : ["--session", "--capture", "--source", "--trusted", "--request-id", "--budget-ms", "--json"];
        for (int i = 1; i < args.Length; i++)
        {
            string key = args[i];
            if (!allowed.Contains(key) || options.ContainsKey(key)) throw new ArgumentException("Unknown/repeated option: " + key);
            options.Add(key, key == "--json" ? "true" : ++i < args.Length ? args[i] : throw new ArgumentException("Missing value for " + key));
        }
        string Required(string name) => options.TryGetValue(name, out var value) ? value : throw new ArgumentException("Required: " + name);
        var binding = SessionBinding.Load(Required("--session"));
        if (!Guid.TryParse(Required("--request-id"), out var id) || id == Guid.Empty) throw new ArgumentException("request-id must be a non-empty GUID.");
        var client = factory?.Invoke(binding) ?? new PipeClient(binding);
        OperationRequest request;
        if (args[0] == "get-operation") request = new(Guid.NewGuid(), OperationKind.GetOperation, OperationId: id);
        else
        {
            string Read(string path, int limit)
            {
                if (!Path.IsPathFullyQualified(path) || new FileInfo(path).Length > limit) throw new ArgumentException("Use a bounded absolute file path.");
                return File.ReadAllText(path);
            }
            var capture = JsonSerializer.Deserialize<OperationResult>(Read(Required("--capture"), Protocol.MaxBytes), Protocol.Json);
            if (capture?.Succeeded != true || capture.Snapshot is not { } snapshot) throw new ArgumentException("Successful inspection capture required.");
            if (snapshot.Target.ProcessId != binding.ProcessId || snapshot.Target.ProcessStart != binding.ProcessStart || snapshot.Target.SessionId != binding.SessionId)
                throw new ArgumentException("Capture belongs to a different session.");
            if (Required("--trusted") != "true") throw new ArgumentException("Trusted code requires --trusted true; full process permissions, no sandbox.");
            int budget = int.Parse(options.GetValueOrDefault("--budget-ms") ?? "10000", CultureInfo.InvariantCulture);
            request = new(id, args[0] == "run-query" ? OperationKind.RunQuery : OperationKind.RunModify, snapshot.Target,
                Code: new(Read(Required("--source"), DynamicExecutor.MaxSourceBytes), true, budget));
        }
        var result = await client.ExecuteAsync(request);
        await output.WriteLineAsync(JsonSerializer.Serialize(result, Protocol.Json));
        if (!result.Succeeded) { await error.WriteLineAsync(result.Message); return 1; }
        return 0;
    }
}
