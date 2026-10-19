using System.Text.Json;
using RevitThyme.Core;

namespace RevitThyme.Cli;

internal static class TimberFoldCli
{
    public static async Task<int> Run(string[] args, TextWriter output, TextWriter error,
        Func<SessionBinding, IOperationClient>? clientFactory)
    {
        if (args.Length < 2 || args[1] is not ("inspect" or "preview" or "status" or "cancel"))
            throw new ArgumentException("Use timberfold inspect|preview|status|cancel --session ABSOLUTE_FILE; preview/status/cancel require --run-id GUID.");
        string command = args[1];
        var options = Program.Parse(args[2..]);
        string[] allowed = command switch
        {
            "inspect" => ["--session", "--profile", "--scope", "--json"],
            "preview" => ["--session", "--run-id", "--profile", "--scope", "--json"],
            _ => ["--session", "--run-id", "--json"]
        };
        if (options.Keys.Except(allowed).Any()) throw new ArgumentException("Unknown TimberFold " + command + " option.");
        Guid runId = Guid.Empty;
        if (command != "inspect" && (!Guid.TryParse(Program.Required(options, "--run-id"), out runId) || runId == Guid.Empty))
            throw new ArgumentException("run-id must be a non-empty GUID.");
        var profile = Read<TimberFoldProfile>(options, "--profile");
        var scope = Read<TimberScope>(options, "--scope");
        var binding = SessionBinding.Load(Program.Required(options, "--session"));
        var client = clientFactory?.Invoke(binding) ?? new PipeClient(binding);
        OperationResult result;
        if (command is "status" or "cancel")
            result = await client.ExecuteAsync(new(Guid.NewGuid(), command == "status" ? OperationKind.GetTimberFoldRun : OperationKind.CancelTimberFoldRun,
                OperationId: runId));
        else
        {
            var context = await client.ExecuteAsync(new(Guid.NewGuid(), OperationKind.Inspect));
            result = context.Succeeded && context.Snapshot is { } snapshot
                ? await client.ExecuteAsync(new(command == "preview" ? runId : Guid.NewGuid(),
                    command == "preview" ? OperationKind.PreviewTimberFold : OperationKind.InspectTimberFold,
                    snapshot.Target, TimberFold: new(profile, scope))) : context;
        }
        if (options.ContainsKey("--json")) await output.WriteLineAsync(JsonSerializer.Serialize(result, Protocol.Json));
        else
        {
            await output.WriteLineAsync(result.Message);
            if (result.TimberRun is { } run)
            {
                await output.WriteLineAsync($"Run {run.RunId}: {run.State}, {run.Stage}; {run.PanelCount} panels, {run.SheetCount} sheets.");
                await output.WriteLineAsync("Private run files: " + run.Directory);
                foreach (var diagnostic in run.Diagnostics)
                    await output.WriteLineAsync($"{diagnostic.Severity} {diagnostic.Code}: {diagnostic.Message}");
                await output.WriteLineAsync("Manufacturing readiness: false; native import and physical checks outstanding.");
            }
            if (result.TimberFold is { } timber)
            {
                var info = timber.Inspection;
                await output.WriteLineAsync($"{info.SourceCount} sources, {info.FaceCount} faces, {info.OpeningCount} openings; {info.ErrorCount} errors, {info.WarningCount} warnings.");
                foreach (var diagnostic in info.Diagnostics)
                    await output.WriteLineAsync($"{diagnostic.Severity} {diagnostic.Code} [{diagnostic.SourceId}]: {diagnostic.Message}");
                if (timber.Evidence is { } evidence) await output.WriteLineAsync("Full private inspection: " + evidence.Directory);
            }
        }
        if (!result.Succeeded) { await error.WriteLineAsync(result.Message); return 1; }
        return 0;
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
