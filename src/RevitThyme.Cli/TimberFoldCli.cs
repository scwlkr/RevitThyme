using System.Text.Json;
using RevitThyme.Core;

namespace RevitThyme.Cli;

internal static class TimberFoldCli
{
    public static async Task<int> Run(string[] args, TextWriter output, TextWriter error,
        Func<SessionBinding, IOperationClient>? clientFactory)
    {
        if (args is not ["timberfold", "inspect", ..])
            throw new ArgumentException("Use timberfold inspect --session ABSOLUTE_FILE [--profile JSON] [--scope JSON] [--json].");
        var options = Program.Parse(args[2..]);
        if (options.Keys.Except(["--session", "--profile", "--scope", "--json"]).Any())
            throw new ArgumentException("Unknown TimberFold inspection option.");
        var profile = Read<TimberFoldProfile>(options, "--profile");
        var scope = Read<TimberScope>(options, "--scope");
        var binding = SessionBinding.Load(Program.Required(options, "--session"));
        var client = clientFactory?.Invoke(binding) ?? new PipeClient(binding);
        var context = await client.ExecuteAsync(new(Guid.NewGuid(), OperationKind.Inspect));
        var result = context.Succeeded && context.Snapshot is { } snapshot
            ? await client.ExecuteAsync(new(Guid.NewGuid(), OperationKind.InspectTimberFold, snapshot.Target,
                TimberFold: new(profile, scope))) : context;
        if (options.ContainsKey("--json")) await output.WriteLineAsync(JsonSerializer.Serialize(result, Protocol.Json));
        else
        {
            await output.WriteLineAsync(result.Message);
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
