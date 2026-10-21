using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using ModelContextProtocol.Client;
using RevitThyme.Core;

namespace RevitThyme.McpTests;

// External SDK client, real installed native host, copied development fixture only.
internal sealed class SavedToolLive
{
    private readonly string folder, evidence, fixture, session, store;
    private readonly JsonElement launch;
    private readonly List<object> calls = [];
    private readonly List<object> checks = [];
    private readonly string fixtureHash, registrationHash;
    private string documentId = "";
    private SavedToolLive(string folder)
    {
        this.folder = Path.GetFullPath(folder);
        launch = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "launch.json"))).RootElement.Clone();
        fixture = launch.GetProperty("fixture").GetString()!;
        if (launch.GetProperty("folder").GetString() != this.folder || fixture != Path.Combine(this.folder, "Fixture.rvt"))
            throw new InvalidDataException("Use the exact owned development run.");
        evidence = Path.Combine(folder, "saved-tool-native-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(evidence);
        store = Path.Combine(evidence, "tools");
        session = Path.Combine(folder, "sessions", launch.GetProperty("session_id").GetString() + ".json");
        var binding = SessionBinding.Load(session);
        if (binding.ProcessId != launch.GetProperty("pid").GetInt32() || binding.ProcessStart != launch.GetProperty("start").GetString())
            throw new InvalidDataException("Native process/start differs from owned launch.");
        fixtureHash = Hash(fixture); registrationHash = Hash(launch.GetProperty("registration").GetString()!);
        string hostManifest = Path.Combine(launch.GetProperty("bundle").GetString()!, "manifest.json");
        Write("identity.json", new { launch, binding, fixtureHash, registrationHash, hostManifest = JsonDocument.Parse(File.ReadAllText(hostManifest)).RootElement,
            mcpExecutable = Environment.GetEnvironmentVariable("REVITTHYME_QUALIFICATION_MCP"),
            source = SourceRevision(), clientFiles = Directory.GetFiles(AppContext.BaseDirectory, "RevitThyme*.dll").ToDictionary(p => Path.GetFileName(p)!, Hash) });
    }
    public static Task Run(string folder) => new SavedToolLive(folder).Qualify();
    internal static Task<McpClient> Client(string session, string store)
    {
        string? executable = Environment.GetEnvironmentVariable("REVITTHYME_QUALIFICATION_MCP");
        if (executable is not null && (!Path.IsPathFullyQualified(executable) || !File.Exists(executable)))
            throw new ArgumentException("Qualification MCP must be an absolute existing executable.");
        return McpClient.CreateAsync(new StdioClientTransport(new()
        {
            Command = executable ?? "dotnet", Arguments = executable is null
                ? [Path.Combine(AppContext.BaseDirectory, "revitthyme-mcp.dll"), "--session", session] : ["--session", session],
            Name = "Saved tool native qualification", EnvironmentVariables = new Dictionary<string, string?> { ["REVITTHYME_TOOLS_DIRECTORY"] = store }
        }));
    }
    private void Check(string name, bool passed)
    { checks.Add(new { name, passed }); Write("progress.json", new { checks }); Program.Check(name, passed); }
    private async Task<JsonElement> Call(McpClient client, string name, Dictionary<string, object?>? args = null, bool error = false)
    {
        var result = await Program.Call(client, name, args);
        calls.Add(new { name, args, result.Data, result.Result.IsError }); Write("calls.json", calls);
        Check(name + (error ? " rejects invalid request" : " succeeds"), (result.Result.IsError == true) == error);
        return result.Data;
    }
    private async Task<JsonElement> Context(McpClient client)
    {
        var context = await Call(client, "get_context"); var snapshot = context.GetProperty("result").GetProperty("snapshot");
        var target = snapshot.GetProperty("target");
        Check("capture remains exact owned fixture/process", snapshot.GetProperty("documentPath").GetString() == fixture
            && target.GetProperty("processId").GetInt32() == launch.GetProperty("pid").GetInt32()
            && target.GetProperty("processStart").GetString() == launch.GetProperty("start").GetString());
        string id = target.GetProperty("documentId").GetString()!;
        if (documentId.Length == 0) documentId = id;
        Check("captured document identity remains pinned", documentId == id);
        return context;
    }
    private static Dictionary<string, object?> RunArgs(JsonElement artifact, string capture, object inputs) => new()
    {
        ["toolId"] = artifact.GetProperty("toolId").GetString(), ["digest"] = artifact.GetProperty("digest").GetString(),
        ["captureId"] = capture, ["requestId"] = Guid.NewGuid().ToString(), ["inputs"] = inputs, ["trusted"] = true
    };
    private async Task Qualify()
    {
        bool passed = false; string? failure = null;
        try
        {
            await using var client = await Client(session, store);
            var context = await Context(client); string original = context.GetProperty("result").GetProperty("snapshot").GetProperty("viewName").GetString()!;
            string capture = context.GetProperty("captureId").GetString()!;
            var query = new SavedToolDefinition("Native input query", "Create a reusable view query with a caller marker", "Read native view and echo typed input", "query",
                "return new { marker = inputs.GetProperty(\"marker\").GetString(), view = doc.ActiveView.Name, elements = new FilteredElementCollector(doc).GetElementCount() };",
                [new("marker", "string", "Caller marker")], "Native view name and caller marker", [new("marker check", JsonSerializer.SerializeToElement(new { marker = "native-query" }),
                    "Copied editable development model with active view", "Returns true", "return !string.IsNullOrEmpty(doc.ActiveView.Name) && inputs.GetProperty(\"marker\").GetString() == \"native-query\";")]);
            string queryId = Guid.NewGuid().ToString();
            var saved = await Call(client, "save_tool", new() { ["toolId"] = queryId, ["definition"] = query });
            Check("saving does not claim verification", saved.GetProperty("testStatus").GetString() == "not_run");
            var args = RunArgs(saved, capture, new { marker = "native-query" });
            var result = await Call(client, "run_tool", args); var value = result.GetProperty("execution").GetProperty("value");
            Check("saved query compiles and uses typed input in native Revit", value.GetProperty("marker").GetString() == "native-query"
                && value.GetProperty("view").GetString() == original && value.GetProperty("elements").GetInt32() > 0);
            string escaped = "\"; throw new Exception(\"must remain data\"); // \\path\\file\r\nUnicode \u2603 \uD83C\uDF3F";
            args = RunArgs(saved, (await Context(client)).GetProperty("captureId").GetString()!, new { marker = escaped });
            Check("quotes, backslashes, CRLF and Unicode stay literal native input data", (await Call(client, "run_tool", args))
                .GetProperty("execution").GetProperty("value").GetProperty("marker").GetString() == escaped);
            args = RunArgs(saved, (await Context(client)).GetProperty("captureId").GetString()!, new { marker = "native-query" }); args["testName"] = "marker check";
            Check("saved assertion runs as native rollback query", (await Call(client, "run_tool", args)).GetProperty("execution").GetProperty("value").GetBoolean());
            args = RunArgs(saved, capture, new { marker = "native-query" }); args["digest"] = new string('0', 64); await Call(client, "run_tool", args, true);
            args = RunArgs(saved, capture, new { marker = "native-query" }); args["trusted"] = false; await Call(client, "run_tool", args, true);
            args = RunArgs(saved, capture, new { marker = 123 }); await Call(client, "run_tool", args, true);
            await using (var restarted = await Client(session, store))
            {
                var retrieved = await Call(restarted, "get_tool", new() { ["toolId"] = queryId });
                Check("definition and digest survive MCP process restart", retrieved.GetProperty("digest").GetString() == saved.GetProperty("digest").GetString());
            }
            string renamed = "Saved Tool Native " + Guid.NewGuid().ToString("N")[..8];
            var modify = new SavedToolDefinition("Native input rename", "Create a reusable active-view rename with old-value check", "Rename exact captured active view", "modify",
                "if (doc.ActiveView.Name != inputs.GetProperty(\"expected\").GetString()) throw new InvalidOperationException(\"View changed\"); doc.ActiveView.Name = inputs.GetProperty(\"name\").GetString(); return new { name = doc.ActiveView.Name };",
                [new("expected", "string", "Expected current name"), new("name", "string", "Desired name")], "Active view has desired name",
                [new("rename check", JsonSerializer.SerializeToElement(new { expected = original, name = renamed }), "Exact captured editable active view in copied fixture", "Returns true",
                    "return doc.ActiveView.Name == inputs.GetProperty(\"name\").GetString();")]);
            var rename = await Call(client, "save_tool", new() { ["toolId"] = Guid.NewGuid().ToString(), ["definition"] = modify });
            args = RunArgs(rename, (await Context(client)).GetProperty("captureId").GetString()!, new { expected = original, name = renamed });
            result = await Call(client, "run_tool", args);
            Check("saved modify applies and reports real changed IDs", result.GetProperty("code").GetString() == "applied"
                && result.GetProperty("execution").GetProperty("changes").GetProperty("modified").GetArrayLength() > 0);
            var replay = await Call(client, "run_tool", args);
            Check("same saved request replays retained result", replay.GetProperty("id").GetString() == result.GetProperty("id").GetString()
                && replay.GetProperty("execution").GetProperty("sourceSha256").GetString() == result.GetProperty("execution").GetProperty("sourceSha256").GetString());
            var after = await Context(client);
            Check("independent native capture confirms modified view", after.GetProperty("result").GetProperty("snapshot").GetProperty("viewName").GetString() == renamed);
            args = RunArgs(rename, after.GetProperty("captureId").GetString()!, new { expected = original, name = renamed }); args["testName"] = "rename check";
            Check("stored assertion independently verifies native modification", (await Call(client, "run_tool", args)).GetProperty("execution").GetProperty("value").GetBoolean());
            await Call(client, "run_query", new()
            {
                ["captureId"] = (await Context(client)).GetProperty("captureId").GetString(), ["requestId"] = Guid.NewGuid().ToString(), ["trusted"] = true,
                ["source"] = "var undo = Autodesk.Revit.UI.RevitCommandId.LookupPostableCommandId(Autodesk.Revit.UI.PostableCommand.Undo); if (!uiapp.CanPostCommand(undo)) throw new InvalidOperationException(\"Undo unavailable\"); uiapp.PostCommand(undo); return true;"
            });
            var deadline = DateTime.UtcNow.AddSeconds(20); bool restored = false;
            do
            {
                await Task.Delay(500); var readback = await Context(client);
                restored = readback.GetProperty("result").GetProperty("snapshot").GetProperty("viewName").GetString() == original;
            } while (!restored && DateTime.UtcNow < deadline);
            Check("one native Undo restores original view name", restored);
            Check("fixture file and installed registration remain unchanged", Hash(fixture) == fixtureHash
                && Hash(launch.GetProperty("registration").GetString()!) == registrationHash);
            passed = true;
        }
        catch (Exception error) { failure = error.ToString(); throw; }
        finally { Write("result.json", new { passed, failure, checks, calls, fixture, fixtureHash, registrationHash, store, session }); }
        Console.WriteLine("Saved-tool native evidence: " + evidence);
    }
    internal static string Hash(string path) { using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite); return Convert.ToHexStringLower(SHA256.HashData(stream)); }
    private static string SourceRevision()
    {
        using var process = Process.Start(new ProcessStartInfo("git", "rev-parse HEAD") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true })!;
        string revision = process.StandardOutput.ReadToEnd().Trim(); process.WaitForExit(); return revision;
    }
    private void Write(string name, object value) => File.WriteAllText(Path.Combine(evidence, name), JsonSerializer.Serialize(value, Core.Protocol.Json));
}
