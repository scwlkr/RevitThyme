using System.Security.Cryptography;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace RevitThyme.McpTests;

// Real packaged server over the official SDK transport. No native session is selected.
internal static class PackagedStdio
{
    private sealed record CheckResult(string Name, bool Passed);
    internal static async Task Run(string bundle, string expectedManifestSha256, string evidence)
    {
        if (!Path.IsPathFullyQualified(bundle) || !Path.IsPathFullyQualified(evidence))
            throw new ArgumentException("Use absolute bundle and fresh evidence directories.");
        bundle = Path.TrimEndingDirectorySeparator(Path.GetFullPath(bundle)); evidence = Path.GetFullPath(evidence);
        if (Directory.Exists(evidence) || File.Exists(evidence) || evidence.StartsWith(bundle + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Preserve the bundle and previous evidence; choose a fresh external directory.");
        RequirePlain(bundle);
        string manifestPath = Path.Combine(bundle, "manifest.json");
        RequirePlain(manifestPath);
        if (new FileInfo(manifestPath).Length > 1048576) throw new InvalidDataException("Package manifest exceeds its bounded size.");
        string manifestSha256 = Hash(manifestPath);
        if (expectedManifestSha256.Length != 64 || !expectedManifestSha256.All(Uri.IsHexDigit) ||
            !manifestSha256.Equals(expectedManifestSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Package manifest differs from the explicit candidate pin.");
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
        var files = manifest.RootElement.GetProperty("files").EnumerateObject()
            .Where(p => p.Name.StartsWith("mcp/", StringComparison.Ordinal)).ToDictionary(p => p.Name, p => p.Value.GetString()!, StringComparer.Ordinal);
        if (files.Count == 0 || !files.ContainsKey("mcp/revitthyme-mcp.exe")) throw new InvalidDataException("Packaged MCP inventory is missing.");
        CheckFiles();
        string system = Environment.SystemDirectory;
        if (new[] { "python.exe", "python3.exe", "py.exe", "pyrevit.exe" }.Any(name => File.Exists(Path.Combine(system, name))))
            throw new InvalidDataException("Controlled system PATH contains a Python/pyRevit executable; cannot establish this bounded no-Python-PATH proof.");
        Directory.CreateDirectory(evidence);
        string sessions = Path.Combine(evidence, "empty-sessions"); Directory.CreateDirectory(sessions);
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var checks = new List<CheckResult>(); bool completed = false;
        var environment = new Dictionary<string, string?>
        {
            ["PATH"] = system, ["SystemRoot"] = Environment.GetEnvironmentVariable("SystemRoot"),
            ["WINDIR"] = Environment.GetEnvironmentVariable("WINDIR"),
            ["TEMP"] = evidence, ["TMP"] = evidence,
            ["USERPROFILE"] = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ["LOCALAPPDATA"] = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
        };
        string executable = Path.Combine(bundle, "mcp", "revitthyme-mcp.exe");
        try
        {
            await using (var client = await McpClient.CreateAsync(new StdioClientTransport(new()
            {
                Command = executable, Arguments = ["--sessions-directory", sessions], Name = "Installed RevitThyme qualification",
                WorkingDirectory = evidence, InheritEnvironmentVariables = false, EnvironmentVariables = environment
            }), cancellationToken: lifetime.Token))
            {
                var tools = await client.ListToolsAsync(cancellationToken: lifetime.Token);
                Check("installed MCP starts over SDK stdio without Python or pyRevit on PATH", tools.Count == 29);
                string[] timber = ["timberfold_inspect", "timberfold_preview", "timberfold_generate", "timberfold_status",
                    "timberfold_cancel", "timberfold_verify", "timberfold_reconcile"];
                Check("installed server exposes all seven shared TimberFold controls", timber.All(name => tools.Count(t => t.Name == name) == 1));
                var reconcile = tools.Single(t => t.Name == "timberfold_reconcile");
                Check("installed reconciliation requires fresh capture and separate generation identity", reconcile.ProtocolTool.Annotations is
                    { ReadOnlyHint: false, DestructiveHint: false, IdempotentHint: true } &&
                    reconcile.JsonSchema.ToString().Contains("captureId", StringComparison.Ordinal) &&
                    reconcile.JsonSchema.ToString().Contains("generationRunId", StringComparison.Ordinal) &&
                    !reconcile.JsonSchema.ToString().Contains("directory", StringComparison.OrdinalIgnoreCase));
                Save("tools.json", JsonSerializer.SerializeToUtf8Bytes(tools.Select(t => new
                { t.Name, t.Description, schema = t.JsonSchema, annotations = t.ProtocolTool.Annotations }), RevitThyme.Core.Protocol.Json));
                var empty = await Call("list_sessions", null);
                Check("explicit empty discovery cannot select a working native session", empty.Result.IsError != true && empty.Data.GetArrayLength() == 0);
                foreach (string tool in new[] { "get_context", "timberfold_status", "timberfold_verify" })
                {
                    var reply = await Call(tool, tool == "get_context" ? null : new() { ["runId"] = Guid.NewGuid().ToString("D") });
                    Check("installed " + tool + " refuses native work without explicit connection", reply.Result.IsError == true &&
                        reply.Data.GetProperty("code").GetString() == "not_connected");
                }
                var invalid = await Call("timberfold_reconcile", new()
                { ["captureId"] = "no-capture", ["requestId"] = "invalid", ["generationRunId"] = Guid.NewGuid().ToString("D") });
                Check("installed reconciliation rejects invalid identity before native work", invalid.Result.IsError == true && invalid.Data.GetProperty("code").GetString() == "invalid_id");
                CheckFiles();
                Check("SDK activity preserves exact packaged MCP files and empty session inventory", Directory.GetFileSystemEntries(sessions).Length == 0);

                async Task<(CallToolResult Result, JsonElement Data)> Call(string name, Dictionary<string, object?>? arguments)
                {
                    var result = await client.CallToolAsync(name, arguments ?? [], cancellationToken: lifetime.Token);
                    var text = result.Content.OfType<TextContentBlock>().Single().Text;
                    Save(name + ".json", System.Text.Encoding.UTF8.GetBytes(text));
                    using var json = JsonDocument.Parse(text);
                    return (result, json.RootElement.Clone());
                }
            }
            completed = true;
        }
        finally
        {
            Save("qualification.json", JsonSerializer.SerializeToUtf8Bytes(new
            {
                passed = completed && checks.Count == 9 && checks.All(c => c.Passed), bundle, executable, manifestSha256,
                mcpFiles = files, inheritedEnvironment = false, controlledPath = system,
                pythonOrPyRevitExecutablesOnPath = false, nativeConnectionSelected = false,
                nativeQualificationOutstanding = true, physicalChecksOutstanding = true, checks,
                limitation = "Installed stdio transport, tool discovery and disconnected guards only. Does not qualify native Revit generation or physical fabrication."
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        void Check(string name, bool passed)
        {
            checks.Add(new(name, passed)); Program.Check(name, passed);
        }
        void CheckFiles()
        {
            RequirePlain(Path.Combine(bundle, "mcp"));
            if (Hash(manifestPath) != manifestSha256) throw new InvalidDataException("Package manifest changed during qualification.");
            foreach (var (name, expected) in files)
            {
                if (name.Contains('\\') || name.Contains(':') || name.Split('/').Any(p => p is "" or "." or "..") ||
                    Path.IsPathRooted(name) || expected.Length != 64) throw new InvalidDataException("Invalid packaged MCP identity.");
                string path = Path.Combine(bundle, name); RequirePlain(path);
                if (Hash(path) != expected) throw new InvalidDataException("Packaged MCP file changed: " + name);
            }
            var actualFiles = new List<string>(); var folders = new Stack<string>(); folders.Push(Path.Combine(bundle, "mcp"));
            int entries = 0;
            while (folders.TryPop(out var folder))
                foreach (string child in Directory.EnumerateFileSystemEntries(folder))
                {
                    if (++entries > 512) throw new InvalidDataException("Packaged MCP inventory exceeds its bounded size.");
                    var attributes = File.GetAttributes(child);
                    if ((attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Packaged MCP inventory is redirected.");
                    if ((attributes & FileAttributes.Directory) != 0) folders.Push(child);
                    else actualFiles.Add(Path.GetRelativePath(bundle, child).Replace('\\', '/'));
                }
            var actual = actualFiles.Order(StringComparer.Ordinal);
            if (!actual.SequenceEqual(files.Keys.Order(StringComparer.Ordinal))) throw new InvalidDataException("Packaged MCP inventory differs from its manifest.");
        }
        void Save(string name, byte[] bytes)
        {
            using var stream = new FileStream(Path.Combine(evidence, name), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            stream.Write(bytes); stream.Flush(true);
        }
    }
    private static string Hash(string path)
    { using var stream = File.OpenRead(path); return Convert.ToHexStringLower(SHA256.HashData(stream)); }
    private static void RequirePlain(string path)
    {
        for (FileSystemInfo? item = File.Exists(path) ? new FileInfo(path) : new DirectoryInfo(path); item is not null;
            item = item is FileInfo file ? file.Directory : ((DirectoryInfo)item).Parent)
            if ((item.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Package verification paths cannot be redirected.");
    }
}
