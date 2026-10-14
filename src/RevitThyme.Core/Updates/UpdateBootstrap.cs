using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace RevitThyme.Core;

public static class UpdateBootstrap
{
    public static async Task<string> RunAsync(string version, string? destination, bool install)
    {
        ProductVersion.ParseStable(version);
        if (!OperatingSystem.IsWindows()) throw new InvalidOperationException("The Windows installer requires Windows.");
        if (install && RevitIsRunning()) throw new InvalidOperationException("Save your work and close all Revit windows normally before installing an update.");
        if (!install && (destination is null || !Path.IsPathFullyQualified(destination))) throw new ArgumentException("Use an absolute download destination.");
        string script = Path.Combine(Path.GetDirectoryName(typeof(UpdateBootstrap).Assembly.Location)!, "update.ps1");
        if (!File.Exists(script)) throw new FileNotFoundException("The packaged update downloader is missing. Rebuild or reinstall RevitThyme.", script);
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        // PowerShell 7's inherited module path hides Windows PowerShell's built-in modules.
        start.Environment.Remove("PSModulePath");
        foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script, "-Version", version }) start.ArgumentList.Add(argument);
        if (!install)
        { start.ArgumentList.Add("-DownloadOnly"); start.ArgumentList.Add("-Destination"); start.ArgumentList.Add(destination!); }
        using var process = Process.Start(start) ?? throw new IOException("Could not start the update downloader.");
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(); string message = await output; string errors = await error;
        if (process.ExitCode != 0) throw new IOException("Update failed: " + (errors.Length > 0 ? errors : message).Trim());
        if (install) return "RevitThyme " + version + " setup completed. Open Revit normally.";
        string path = Path.Combine(destination!, $"RevitThyme-{version}-win-x64.exe");
        string hash = File.ReadAllText(path + ".sha256").Trim();
        var match = Regex.Match(hash, @"^([0-9A-Fa-f]{64})(?:[ \t]+\*?" + Regex.Escape(Path.GetFileName(path)) + ")?$");
        using var file = File.OpenRead(path);
        if (!match.Success || file.Length == 0 || !string.Equals(Convert.ToHexString(SHA256.HashData(file)), match.Groups[1].Value, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Downloaded installer checksum verification failed.");
        return path;
    }
    public static bool RevitIsRunning()
    {
        var processes = Process.GetProcessesByName("Revit");
        try { return processes.Length != 0; } finally { foreach (var process in processes) process.Dispose(); }
    }
}
