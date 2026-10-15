using System.Text.Json;
using RevitThyme.Core;

namespace RevitThyme.Cli;

public static class UpdateCli
{
    public static async Task<int> Run(string[] args, TextWriter output,
        Func<string?, Task<UpdateResult>>? check = null, Func<string, string?, bool, Task<string>>? bootstrap = null,
        Func<ToolkitInventory>? inventory = null, Func<bool>? revitIsRunning = null)
    {
        var state = (inventory ?? ToolkitInventory.Capture)();
        if (args[0] is "version" or "--version")
        {
            if (args.Length > 2 || args.Length == 2 && args[1] != "--json") throw new ArgumentException("Use version [--json].");
            await output.WriteLineAsync(args.Length == 2 ? JsonSerializer.Serialize(new { version = state.Cli.Version, build = state.Cli.Build, state.Cli, state.Development, state.Installed }, Protocol.Json) : Describe(state));
            return state.Installed.Status == "invalid" ? 1 : 0;
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
        if (state.Installed.Status == "invalid") throw new InvalidOperationException(state.Installed.Message);
        using var http = UpdateChecker.CreateClient();
        var result = UpdateChecker.ForInstalled(await (check?.Invoke(version) ?? new UpdateChecker(http).CheckAsync(version: version)), state.Installed);
        if (command == "check")
        { await output.WriteLineAsync(json ? JsonSerializer.Serialize(new { result.CurrentVersion, result.Build, state.Cli, state.Development, state.Installed, result.LatestVersion, result.Status, result.Message, result.Release }, Protocol.Json)
            : Describe(state) + "\n" + result.Message); return result.Status == "incompatible" ? 1 : 0; }
        if (result.Status == "incompatible") throw new InvalidOperationException(result.Message);
        if (version is null && result.Status is not ("available" or "not_installed")) { await output.WriteLineAsync(result.Message); return 0; }
        if (command == "install" && (revitIsRunning ?? UpdateBootstrap.RevitIsRunning)()) throw new InvalidOperationException("Save your work and close all Revit windows normally before installing an update.");
        string response = await (bootstrap?.Invoke(result.LatestVersion, destination, command == "install") ?? UpdateBootstrap.RunAsync(result.LatestVersion, destination, command == "install"));
        await output.WriteLineAsync(command == "download" ? "Verified installer: " + response + "\nSave your work and close Revit before running it." : response);
        return 0;
    }
    private static string Describe(ToolkitInventory state) => $"CLI: {state.Cli.Build}\nDevelopment: {state.Development?.Build ?? "not detected"}"
        + (state.Development is { } development ? " (" + development.Path + ")" : "")
        + $"\nInstalled RevitThyme (Revit 2027): {state.Installed.Version ?? (state.Installed.Status == "missing" ? "not installed" : state.Installed.Status)}"
        + (state.Installed.Build is { } build ? " (" + build + ")" : "")
        + (state.Installed.Status == "invalid" ? "\n" + state.Installed.Message : "");
}
