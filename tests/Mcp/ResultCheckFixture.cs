using System.Diagnostics;
using System.Text.Json;
using ModelContextProtocol.Client;
using RevitThyme.Core;

namespace RevitThyme.McpTests;

// Evidence and targeting shared by the result-check native and real-Agent probes.
internal sealed class ResultCheckFixture
{
    internal string Folder { get; }
    internal string Evidence { get; }
    internal string Fixture { get; }
    internal string Session { get; }
    internal string Store { get; }
    internal JsonElement Launch { get; }
    internal SessionBinding Binding { get; }
    private readonly string fixtureHash, registrationHash, baseline, baselineHash;
    private readonly List<object> calls = [], checks = [];
    private string? documentId;
    private bool? originalModified;
    internal ResultCheckFixture(string folder, string role)
    {
        Folder = Path.GetFullPath(folder);
        Launch = JsonDocument.Parse(File.ReadAllText(Path.Combine(Folder, "launch.json"))).RootElement.Clone();
        Fixture = Launch.GetProperty("fixture").GetString()!;
        if (Launch.GetProperty("folder").GetString() != Folder || Fixture != Path.Combine(Folder, "Fixture.rvt"))
            throw new InvalidDataException("Use the exact owned development run and copied fixture.");
        Session = Path.Combine(Folder, "sessions", Launch.GetProperty("session_id").GetString() + ".json");
        Binding = SessionBinding.Load(Session);
        if (Binding.ProcessId != Launch.GetProperty("pid").GetInt32() || Binding.ProcessStart != Launch.GetProperty("start").GetString())
            throw new InvalidDataException("Owned process/start differs from the session.");
        if (Binding.ProtocolVersion != Protocol.Version || Binding.ProtocolVersion < 14)
            throw new InvalidDataException("Result observation requires the newly installed native protocol; do not test against an older host.");
        Evidence = Path.Combine(Folder, "result-check-" + role + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Evidence); Store = Path.Combine(Evidence, "tools");
        var plan = JsonDocument.Parse(File.ReadAllText(Path.Combine(Folder, "plan.json"))).RootElement.Clone();
        baseline = plan.GetProperty("fixture_source").GetString()!; baselineHash = SavedToolLive.Hash(baseline);
        if (!string.Equals(baselineHash, plan.GetProperty("fixture_sha256").GetString(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Protected baseline no longer matches prepared fixture source.");
        fixtureHash = SavedToolLive.Hash(Fixture); registrationHash = SavedToolLive.Hash(Launch.GetProperty("registration").GetString()!);
        using var native = Process.GetProcessById(Binding.ProcessId);
        string bundle = Launch.GetProperty("bundle").GetString()!;
        string manifestPath = Path.Combine(bundle, "manifest.json");
        var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath)).RootElement.Clone();
        var modules = native.Modules.Cast<ProcessModule>()
            .Where(m => m.ModuleName.StartsWith("RevitThyme", StringComparison.OrdinalIgnoreCase))
            .Select(m => new { name = m.ModuleName, path = m.FileName, sha256 = SavedToolLive.Hash(m.FileName), productVersion = m.FileVersionInfo.ProductVersion }).ToArray();
        string[] required = ["RevitThyme.Addin.dll", "RevitThyme.Core.dll", "RevitThyme.Codex.dll", "RevitThyme.UI.dll"];
        string expectedVersion = manifest.GetProperty("version").GetString() + "+" + plan.GetProperty("source_revision").GetString();
        foreach (string name in required)
        {
            var selected = modules.Where(m => string.Equals(m.name, name, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (selected.Length != 1 || !string.Equals(selected[0].path, Path.Combine(bundle, name), StringComparison.OrdinalIgnoreCase)
                || selected[0].productVersion != expectedVersion)
                throw new InvalidDataException("Required native module is missing, ambiguous, outside the exact package, or built from a different source: " + name);
            if (manifest.GetProperty("files").GetProperty(name).GetString() != selected[0].sha256)
                throw new InvalidDataException("Loaded native module differs from the package manifest.");
        }
        Write("identity.json", new { Launch, Binding, plan, baseline, baselineHash, fixtureHash, registrationHash, manifestSha256 = SavedToolLive.Hash(manifestPath), manifest,
            requiredModules = modules.Where(m => required.Contains(m.name, StringComparer.OrdinalIgnoreCase)),
            unrelatedPreservedModules = modules.Where(m => !required.Contains(m.name, StringComparer.OrdinalIgnoreCase)),
            mcpExecutable = Environment.GetEnvironmentVariable("REVITTHYME_QUALIFICATION_MCP"),
            clientAssemblySha256 = SavedToolLive.Hash(typeof(ResultCheckFixture).Assembly.Location) });
    }
    internal Task<McpClient> Client() => SavedToolLive.Client(Session, Store);
    internal void Check(string name, bool passed)
    { checks.Add(new { name, passed }); Write("progress.json", checks); Program.Check(name, passed); }
    internal async Task<JsonElement> Call(McpClient client, string tool, Dictionary<string, object?>? arguments = null, bool? error = false)
    {
        var result = await Program.Call(client, tool, arguments);
        calls.Add(new { tool, arguments, result.Data, result.Result.IsError }); Write("calls.json", calls);
        if (error.HasValue) Check(tool + (error.Value ? " rejects" : " succeeds"), (result.Result.IsError == true) == error.Value);
        return result.Data;
    }
    internal async Task<JsonElement> Context(McpClient client)
    {
        var value = await Call(client, "get_context"); var snapshot = value.GetProperty("result").GetProperty("snapshot");
        originalModified ??= snapshot.GetProperty("isModified").GetBoolean();
        var target = snapshot.GetProperty("target"); string id = target.GetProperty("documentId").GetString()!;
        documentId ??= id;
        Check("native capture remains exact owned fixture/process/document", snapshot.GetProperty("documentPath").GetString() == Fixture
            && target.GetProperty("processId").GetInt32() == Binding.ProcessId && target.GetProperty("processStart").GetString() == Binding.ProcessStart && documentId == id);
        return value;
    }
    internal async Task<JsonElement> Query(McpClient client, string source)
    {
        var context = await Context(client);
        var value = await Call(client, "run_query", new()
        {
            ["captureId"] = context.GetProperty("captureId").GetString(), ["requestId"] = Guid.NewGuid().ToString(), ["trusted"] = true, ["source"] = source
        });
        return value.GetProperty("execution").GetProperty("value");
    }
    internal async Task Undo(McpClient client, string originalName, string originalNumber)
    {
        await Query(client, "var undo = RevitCommandId.LookupPostableCommandId(PostableCommand.Undo); if (!uiapp.CanPostCommand(undo)) throw new InvalidOperationException(\"Undo unavailable\"); uiapp.PostCommand(undo); return true;");
        var deadline = DateTime.UtcNow.AddSeconds(20); bool restored;
        do
        {
            await Task.Delay(300); var actual = await Query(client, "return new { name = doc.ActiveView.Name, number = doc.ProjectInformation.Number };");
            restored = actual.GetProperty("name").GetString() == originalName && actual.GetProperty("number").GetString() == originalNumber;
        } while (!restored && DateTime.UtcNow < deadline);
        Check("one native Undo restores intended and unrelated changes", restored);
    }
    internal async Task Preservation(McpClient client)
    {
        var after = await Context(client);
        Check("protected baseline, copied model file and installed registration are preserved", SavedToolLive.Hash(Fixture) == fixtureHash
            && SavedToolLive.Hash(baseline) == baselineHash && SavedToolLive.Hash(Launch.GetProperty("registration").GetString()!) == registrationHash);
        Check("native modification state is restored", after.GetProperty("result").GetProperty("snapshot").GetProperty("isModified").GetBoolean() == originalModified);
    }
    internal void Finish(bool passed, Exception? error = null)
    { Write("result.json", new { passed, error = error?.ToString(), checks, fixture = Fixture, session = Session, store = Store }); Console.WriteLine("Result-check evidence: " + Evidence); }
    internal void Write(string name, object value) => File.WriteAllText(Path.Combine(Evidence, name), JsonSerializer.Serialize(value, Core.Protocol.Json));
}
