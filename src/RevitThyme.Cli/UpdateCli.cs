using System.Text.Json;
using RevitThyme.Core;

namespace RevitThyme.Cli;

public static class UpdateCli
{
    public static async Task<int> Run(string[] args, TextWriter output,
        Func<string?, Task<UpdateResult>>? check = null, Func<string, string?, bool, Task<string>>? bootstrap = null)
    {
        if (args[0] == "version")
        {
            if (args.Length > 2 || args.Length == 2 && args[1] != "--json") throw new ArgumentException("Use version [--json].");
            await output.WriteLineAsync(args.Length == 2 ? JsonSerializer.Serialize(new { version = ProductVersion.Current, build = ProductVersion.Build }, Protocol.Json)
                : "RevitThyme " + ProductVersion.Build); return 0;
        }
        if (args.Length < 2 || args[1] is not ("check" or "download" or "install")) throw new ArgumentException("Use update check, update download or update install.");
        string command = args[1]; string? version = null, destination = null; bool json = false;
        var seen = new HashSet<string>();
        for (int i = 2; i < args.Length; i++)
        {
            string key = args[i];
            if (!seen.Add(key)) throw new ArgumentException("Repeated option: " + key);
            if (key == "--json" && command == "check") { json = true; continue; }
            if (key != "--version" && !(key == "--destination" && command == "download")) throw new ArgumentException("Unknown update option: " + key);
            if (++i == args.Length || args[i].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException("Missing value for " + key);
            if (key == "--version") { version = args[i]; ProductVersion.ParseStable(version); } else destination = args[i];
        }
        if (command == "download" && (destination is null || !Path.IsPathFullyQualified(destination))) throw new ArgumentException("Use update download --destination ABSOLUTE_FOLDER.");
        if (command == "install" && UpdateBootstrap.RevitIsRunning()) throw new InvalidOperationException("Save your work and close all Revit windows normally before installing an update.");
        using var http = UpdateChecker.CreateClient();
        var result = await (check?.Invoke(version) ?? new UpdateChecker(http).CheckAsync(version: version));
        if (command == "check")
        { await output.WriteLineAsync(json ? JsonSerializer.Serialize(result, Protocol.Json) : $"Installed/build: {result.Build}\n{result.Message}"); return result.Status == "incompatible" ? 1 : 0; }
        if (result.Status == "incompatible") throw new InvalidOperationException(result.Message);
        if (version is null && result.Status != "available") { await output.WriteLineAsync(result.Message); return 0; }
        string response = await (bootstrap?.Invoke(result.LatestVersion, destination, command == "install") ?? UpdateBootstrap.RunAsync(result.LatestVersion, destination, command == "install"));
        await output.WriteLineAsync(command == "download" ? "Verified installer: " + response + "\nSave your work and close Revit before running it." : response);
        return 0;
    }
}
