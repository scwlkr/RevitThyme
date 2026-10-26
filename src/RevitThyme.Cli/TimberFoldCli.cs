using System.Collections.Frozen;
using System.Text.Json;
using RevitThyme.Core;

namespace RevitThyme.Cli;

internal static class TimberFoldCli
{
    private static readonly FrozenDictionary<string, OperationKind> Commands = new Dictionary<string, OperationKind>(StringComparer.Ordinal)
    {
        ["inspect"] = OperationKind.InspectTimberFold, ["preview"] = OperationKind.PreviewTimberFold,
        ["generate"] = OperationKind.GenerateTimberFold, ["reconcile"] = OperationKind.ReconcileTimberFold,
        ["status"] = OperationKind.GetTimberFoldRun, ["cancel"] = OperationKind.CancelTimberFoldRun,
        ["verify"] = OperationKind.VerifyTimberFoldRun
    }.ToFrozenDictionary(StringComparer.Ordinal);
    private sealed record Invocation(OperationKind Operation, SessionBinding Binding, bool Json, Guid RunId,
        Guid? ParentId, TimberFoldRequest? Settings);

    public static async Task<int> Run(string[] args, TextWriter output, TextWriter error,
        Func<SessionBinding, IOperationClient>? clientFactory)
    {
        var invocation = Parse(args);
        var client = clientFactory?.Invoke(invocation.Binding) ?? new PipeClient(invocation.Binding);
        var result = await Execute(client, invocation);
        await Write(output, invocation.Json, result);
        if (!result.Succeeded) { await error.WriteLineAsync(result.Message); return 1; }
        return 0;
    }

    private static Invocation Parse(string[] args)
    {
        if (args.Length < 2 || !Commands.TryGetValue(args[1], out var operation))
            throw new ArgumentException("Use timberfold inspect|preview|generate|reconcile|status|cancel|verify --session ABSOLUTE_FILE; run commands require --run-id GUID, generate requires --preview-run-id GUID and reconcile requires --generation-run-id GUID.");
        var options = Program.Parse(args[2..]);
        if (options.Keys.Except(Allowed(args[1])).Any()) throw new ArgumentException("Unknown TimberFold " + args[1] + " option.");
        Guid runId = operation == OperationKind.InspectTimberFold ? Guid.Empty : ReadRunId(options);
        Guid? parentId = operation == OperationKind.GenerateTimberFold ? ReadParentId(options, "--preview-run-id", runId) : null;
        var profile = Read<TimberFoldProfile>(options, "--profile");
        if (operation == OperationKind.ReconcileTimberFold) parentId = ReadParentId(options, "--generation-run-id", runId);
        var scope = Read<TimberScope>(options, "--scope");
        var binding = SessionBinding.Load(Program.Required(options, "--session"));
        var settings = operation is OperationKind.InspectTimberFold or OperationKind.PreviewTimberFold ? new TimberFoldRequest(profile, scope) : null;
        return new(operation, binding, options.ContainsKey("--json"), runId, parentId, settings);
    }

    private static string[] Allowed(string command) => command switch
    {
        "inspect" => ["--session", "--profile", "--scope", "--json"],
        "preview" => ["--session", "--run-id", "--profile", "--scope", "--json"],
        "generate" => ["--session", "--run-id", "--preview-run-id", "--json"],
        "reconcile" => ["--session", "--run-id", "--generation-run-id", "--json"],
        _ => ["--session", "--run-id", "--json"]
    };
    private static Guid ReadRunId(Dictionary<string, string?> options)
    {
        if (!Guid.TryParse(Program.Required(options, "--run-id"), out var id) || id == Guid.Empty)
            throw new ArgumentException("run-id must be a non-empty GUID.");
        return id;
    }
    private static Guid ReadParentId(Dictionary<string, string?> options, string name, Guid runId)
    {
        if (!Guid.TryParse(Program.Required(options, name), out var id) || id == Guid.Empty || id == runId)
            throw new ArgumentException(name[2..] + " must be a non-empty GUID distinct from the new run-id.");
        return id;
    }

    private static Task<OperationResult> Execute(IOperationClient client, Invocation invocation) => invocation.Operation switch
    {
        OperationKind.GetTimberFoldRun or OperationKind.CancelTimberFoldRun or OperationKind.VerifyTimberFoldRun =>
            client.ExecuteAsync(new(Guid.NewGuid(), invocation.Operation, OperationId: invocation.RunId)),
        OperationKind.GenerateTimberFold => Generate(client, invocation),
        _ => FreshContext(client, invocation)
    };
    private static async Task<OperationResult> Generate(IOperationClient client, Invocation invocation)
    {
        var preview = await client.ExecuteAsync(new(Guid.NewGuid(), OperationKind.GetTimberFoldRun, OperationId: invocation.ParentId));
        if (!preview.Succeeded || preview.TimberRun is not { } parent) return preview;
        return await client.ExecuteAsync(new(invocation.RunId, invocation.Operation, parent.Target, OperationId: invocation.ParentId));
    }
    private static async Task<OperationResult> FreshContext(IOperationClient client, Invocation invocation)
    {
        var context = await client.ExecuteAsync(new(Guid.NewGuid(), OperationKind.Inspect));
        if (!context.Succeeded || context.Snapshot is not { } snapshot) return context;
        Guid id = invocation.Operation == OperationKind.InspectTimberFold ? Guid.NewGuid() : invocation.RunId;
        return await client.ExecuteAsync(new(id, invocation.Operation, snapshot.Target, OperationId: invocation.ParentId, TimberFold: invocation.Settings));
    }

    private static async Task Write(TextWriter output, bool json, OperationResult result)
    {
        if (json) { await output.WriteLineAsync(JsonSerializer.Serialize(result, Protocol.Json)); return; }
        await output.WriteLineAsync(result.Message);
        if (result.TimberRun is { } run) await WriteRun(output, run);
        if (result.TimberFold is { } timber) await WriteInspection(output, timber);
    }
    private static async Task WriteRun(TextWriter output, TimberRunSummary run)
    {
        await output.WriteLineAsync($"Run {run.RunId}: {run.State}, {run.Stage}; {run.PanelCount} panels, {run.SheetCount} sheets.");
        await output.WriteLineAsync("Private run files: " + run.Directory);
        foreach (var diagnostic in run.Diagnostics)
            await output.WriteLineAsync($"{diagnostic.Severity} {diagnostic.Code}: {diagnostic.Message}");
        await output.WriteLineAsync("Manufacturing readiness: false; native import " +
            (run.NativeImportOutstanding ? "outstanding" : "verified") + "; physical checks outstanding.");
    }
    private static async Task WriteInspection(TextWriter output, TimberFoldReply timber)
    {
        var info = timber.Inspection;
        await output.WriteLineAsync($"{info.SourceCount} sources, {info.FaceCount} faces, {info.OpeningCount} openings; {info.ErrorCount} errors, {info.WarningCount} warnings.");
        foreach (var diagnostic in info.Diagnostics)
            await output.WriteLineAsync($"{diagnostic.Severity} {diagnostic.Code} [{diagnostic.SourceId}]: {diagnostic.Message}");
        if (timber.Evidence is { } evidence) await output.WriteLineAsync("Full private inspection: " + evidence.Directory);
    }

    private static T? Read<T>(Dictionary<string, string?> options, string name) where T : class
    {
        if (!options.TryGetValue(name, out string? path)) return null;
        if (path is null || !Path.IsPathFullyQualified(path) || new FileInfo(path).Length > 16384)
            throw new ArgumentException("Use an absolute TimberFold JSON settings file up to 16 KiB.");
        return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Protocol.Json)
            ?? throw new ArgumentException("TimberFold settings cannot be null.");
    }
}
