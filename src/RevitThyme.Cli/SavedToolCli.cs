using System.Globalization;
using System.Text.Json;
using RevitThyme.Core;

namespace RevitThyme.Cli;

internal static class SavedToolCli
{
    private sealed record Invocation(string Command, Dictionary<string, string> Options)
    {
        public string Required(string key) => Options.GetValueOrDefault(key) ?? throw new ArgumentException("Required: " + key);
        public bool IsTest => Options.ContainsKey("--test");
    }

    private sealed record Execution(SessionBinding Binding, JsonElement Inputs, CodeRequest Code, OperationRequest Request);

    public static async Task<int> Run(string[] args, TextWriter output, TextWriter error, Func<SessionBinding, IOperationClient>? factory)
    {
        var invocation = Parse(args);
        var store = new SavedToolStore();
        if (invocation.Command == "save")
        {
            var saved = store.Save(invocation.Required("--id"), Read<SavedToolDefinition>(invocation.Required("--definition"), SavedTools.MaxDefinitionBytes));
            await output.WriteLineAsync(JsonSerializer.Serialize(saved, Protocol.Json));
            return 0;
        }
        var artifact = store.Get(invocation.Required("--id"));
        if (invocation.Command == "show")
        {
            await output.WriteLineAsync(JsonSerializer.Serialize(artifact, Protocol.Json));
            return 0;
        }
        var execution = Prepare(invocation, artifact);
        var client = factory?.Invoke(execution.Binding) ?? new PipeClient(execution.Binding);
        var result = await Execute(invocation, artifact, execution, client);
        return await WriteResult(result, output, error);
    }

    private static Invocation Parse(string[] args)
    {
        string command = Command(args);
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        string[] allowed = AllowedOptions(command);
        for (int i = 2; i < args.Length; i++)
        {
            string key = args[i];
            if (!allowed.Contains(key) || options.ContainsKey(key)) throw new ArgumentException("Unknown/repeated option: " + key);
            options.Add(key, key == "--json" ? "true" : ++i < args.Length ? args[i] : throw new ArgumentException("Missing value: " + key));
        }
        return new(command, options);
    }

    private static string Command(string[] args)
    {
        if (args.Length < 2 || args[1] is not ("save" or "show" or "run" or "verify"))
            throw new ArgumentException("Use tool save, show, run or verify. See --help.");
        return args[1];
    }

    private static string[] AllowedOptions(string command) => command switch
    {
        "save" => ["--id", "--definition", "--json"],
        "show" => ["--id", "--json"],
        _ => ["--id", "--digest", "--inputs", "--session", "--capture", "--request-id", "--trusted", "--test", "--budget-ms", "--json"]
    };

    private static T Read<T>(string path, int limit)
    {
        if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("Use absolute JSON paths.");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > limit) throw new ArgumentException("JSON file exceeds its size limit.");
        return JsonSerializer.Deserialize<T>(stream, Protocol.Json) ?? throw new ArgumentException("JSON value missing.");
    }

    private static Execution Prepare(Invocation invocation, SavedToolArtifact artifact)
    {
        var inputs = Read<JsonElement>(invocation.Required("--inputs"), SavedTools.MaxInputBytes);
        var code = SavedTools.Invocation(artifact, invocation.Required("--digest"), inputs,
            invocation.Required("--trusted") == "true", int.Parse(invocation.Options.GetValueOrDefault("--budget-ms") ?? "10000", CultureInfo.InvariantCulture), invocation.Options.GetValueOrDefault("--test"));
        var binding = SessionBinding.Load(invocation.Required("--session"));
        var target = CaptureTarget(invocation, binding);
        if (!Guid.TryParse(invocation.Required("--request-id"), out var id) || id == Guid.Empty)
            throw new ArgumentException("request-id must be a nonempty GUID.");
        var operation = invocation.IsTest || artifact.Definition.Mode == "query" ? OperationKind.RunQuery : OperationKind.RunModify;
        return new(binding, inputs, code, new(id, operation, target, Code: code));
    }

    private static Target? CaptureTarget(Invocation invocation, SessionBinding binding)
    {
        if (invocation.Command == "verify") return null;
        var capture = Read<OperationResult>(invocation.Required("--capture"), Protocol.MaxOperationBytes);
        var snapshot = capture.Snapshot;
        if (!capture.Succeeded || snapshot is null || snapshot.Target.ProcessId != binding.ProcessId
            || snapshot.Target.ProcessStart != binding.ProcessStart || snapshot.Target.SessionId != binding.SessionId)
            throw new ArgumentException("Successful inspection capture from this exact session required.");
        return snapshot.Target;
    }

    private static async Task<OperationResult> Execute(Invocation invocation, SavedToolArtifact artifact, Execution execution, IOperationClient client)
    {
        if (invocation.Command == "verify")
        {
            if (invocation.IsTest || invocation.Options.ContainsKey("--capture"))
                throw new ArgumentException("tool verify does not accept --test or --capture.");
            return await Coordinator(artifact, execution, client).Verify();
        }
        if (invocation.IsTest || artifact.Definition.ResultChecks is null)
            return await client.ExecuteAsync(execution.Request);
        return await Coordinator(artifact, execution, client).Run(execution.Request);
    }

    private static ResultCheckCoordinator Coordinator(SavedToolArtifact artifact, Execution execution, IOperationClient client) =>
        new(client, execution.Request.Id, artifact.Definition.ResultChecks ?? throw new ArgumentException("Tool has no resultChecks."), execution.Inputs,
            ResultChecks.Hash(execution.Code.Source), true, execution.Code.BudgetMilliseconds);

    private static async Task<int> WriteResult(OperationResult result, TextWriter output, TextWriter error)
    {
        await output.WriteLineAsync(JsonSerializer.Serialize(result, Protocol.Json));
        if (!result.Succeeded || result.Verification is { Status: not "passed" })
        {
            await error.WriteLineAsync(result.Verification?.Message ?? result.Message);
            return 1;
        }
        return 0;
    }
}
