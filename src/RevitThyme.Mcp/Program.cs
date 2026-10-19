using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using RevitThyme.Core;

namespace RevitThyme.Mcp;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args is ["--help"])
        {
            Console.WriteLine("revitthyme-mcp [--session ABSOLUTE_FILE] [--sessions-directory ABSOLUTE_FOLDER]\nOfficial MCP stdio server. Explicit connection. Trusted C# requires host opt-in; see DYNAMIC-EXECUTION.md."); return 0;
        }
        if (args is ["--version"]) { Console.WriteLine(ProductVersion.Stage); return 0; }
        try
        {
            var options = ServerSettings.Parse(args);
            var builder = Host.CreateApplicationBuilder(Array.Empty<string>());
            builder.Logging.ClearProviders(); builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
            builder.Services.AddSingleton(options); builder.Services.AddSingleton<RevitConnection>();
            builder.Services.AddMcpServer().WithStdioServerTransport().WithTools<RevitTools>();
            await builder.Build().RunAsync(); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error.Message); return 2; }
    }
}
public sealed record ServerSettings(string SessionsDirectory, string? SessionFile)
{
    public static ServerSettings Parse(string[] args)
    {
        var values = new Dictionary<string, string>();
        for (int i = 0; i < args.Length; i += 2)
        {
            if (i + 1 >= args.Length || args[i] is not ("--session" or "--sessions-directory") || !values.TryAdd(args[i], args[i + 1])
                || !Path.IsPathFullyQualified(args[i + 1])) throw new ArgumentException("Use distinct options with absolute paths. See --help.");
        }
        return new(values.GetValueOrDefault("--sessions-directory") ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RevitThyme", "sessions"), values.GetValueOrDefault("--session"));
    }
}
