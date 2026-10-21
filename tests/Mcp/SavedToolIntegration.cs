using System.Diagnostics;
using System.Text.Json;
using RevitThyme.Core;
using RevitThyme.Mcp;

namespace RevitThyme.McpTests;

internal static class SavedToolIntegration
{
    private static JsonElement Json(string value) { using var doc = JsonDocument.Parse(value); return doc.RootElement.Clone(); }
    private static void Check(string name, bool value) => Program.Check("saved tools: " + name, value);
    public static async Task Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "RevitThyme-saved-tools-" + Guid.NewGuid().ToString("N"));
        string? before = Environment.GetEnvironmentVariable("REVITTHYME_TOOLS_DIRECTORY");
        Environment.SetEnvironmentVariable("REVITTHYME_TOOLS_DIRECTORY", root);
        Directory.CreateDirectory(root);
        try
        {
            string id = Guid.NewGuid().ToString();
            var definition = new SavedToolDefinition("Echo input", "Save a tool returning my label and count.", "Returns provided data literally.", "query",
                "return new { label = inputs.GetProperty(\"label\").GetString(), count = inputs.GetProperty(\"count\").GetInt64() };",
                [new("label", "string", "Literal label"), new("count", "integer", "Item count")], "Echoed label and count.",
                [new("example", Json("{\"label\":\"hello\",\"count\":2}"), "Any disposable fixture.", "True for example inputs.", "return inputs.GetProperty(\"count\").GetInt64() == 2;")]);
            SavedToolArtifact saved;
            await using (var first = await Program.Client(directory: Path.Combine(root, "absent")))
            {
                var tools = await first.ListToolsAsync();
                var runInputs = tools.Single(t => t.Name == "run_tool").JsonSchema.GetProperty("properties").GetProperty("inputs");
                var testInputs = tools.Single(t => t.Name == "save_tool").JsonSchema.GetProperty("properties").GetProperty("definition")
                    .GetProperty("properties").GetProperty("tests").GetProperty("items").GetProperty("properties").GetProperty("inputs");
                Check("run inputs advertise the required object contract", runInputs.GetProperty("type").GetString() == "object" && runInputs.GetProperty("additionalProperties").GetBoolean());
                Check("stored test inputs advertise the required object contract", testInputs.GetProperty("type").GetString() == "object" && testInputs.GetProperty("additionalProperties").GetBoolean());
                var created = await Program.Call(first, "save_tool", new() { ["toolId"] = id, ["definition"] = definition });
                saved = created.Data.Deserialize<SavedToolArtifact>(Protocol.Json)!;
                Check("stdio saves complete untested definition", created.Result.IsError != true && saved.Definition.Request == definition.Request && saved.TestStatus == "not_run");
                var replay = await Program.Call(first, "save_tool", new() { ["toolId"] = id, ["definition"] = definition });
                Check("same save ID and definition replays without replacement", replay.Data.GetProperty("digest").GetString() == saved.Digest);
                var collision = await Program.Call(first, "save_tool", new() { ["toolId"] = id, ["definition"] = definition with { Name = "Changed" } });
                Check("changed definition cannot overwrite saved ID", collision.Result.IsError == true);
                var malformed = await Program.Call(first, "save_tool", new() { ["toolId"] = Guid.NewGuid().ToString(), ["definition"] = definition with { Tests = [] } });
                Check("a concrete executable test is required", malformed.Result.IsError == true);
                var stringInputs = await Program.Call(first, "save_tool", new() { ["toolId"] = Guid.NewGuid().ToString(),
                    ["definition"] = definition with { Tests = [definition.Tests[0] with { Inputs = JsonSerializer.SerializeToElement("{\"label\":\"hello\",\"count\":2}") }] } });
                Check("stringified test inputs are rejected without coercion", stringInputs.Result.IsError == true);
            }
            await using (var restarted = await Program.Client(directory: Path.Combine(root, "absent")))
            {
                var found = await Program.Call(restarted, "get_tool", new() { ["toolId"] = id });
                Check("saved definition survives server restart", found.Data.GetProperty("digest").GetString() == saved.Digest);
                var traversal = await Program.Call(restarted, "get_tool", new() { ["toolId"] = "../outside" });
                Check("path traversal ID rejected", traversal.Result.IsError == true);
            }
            var store = new SavedToolStore();
            void Reject(string name, Action action)
            { try { action(); Check(name, false); } catch (Exception e) when (e is ArgumentException or IOException) { Check(name, true); } }
            Reject("digest mismatch rejected before native submission", () => SavedTools.Invocation(saved, new string('0', 64), Json("{\"label\":\"a\",\"count\":1}"), true, 10000));
            Reject("missing trust rejected", () => SavedTools.Invocation(saved, saved.Digest, Json("{\"label\":\"a\",\"count\":1}"), false, 10000));
            Reject("duplicate input properties rejected", () => SavedTools.Bind(definition, Json("{\"label\":\"a\",\"label\":\"b\",\"count\":1}")));
            Reject("unknown inputs rejected", () => SavedTools.Bind(definition, Json("{\"label\":\"a\",\"count\":1,\"extra\":true}")));
            Reject("wrong input type rejected", () => SavedTools.Bind(definition, Json("{\"label\":true,\"count\":1}")));
            Reject("missing required input rejected", () => SavedTools.Bind(definition, Json("{\"label\":\"a\"}")));
            string label = "\"\\\r\nΩ🙂 ; return 999; //";
            var inputs = JsonSerializer.SerializeToElement(new { label, count = 2 });
            string bound = SavedTools.Bind(definition, inputs);
            Check("C# binding uses serialized literal data and maps authored lines", bound.Contains("#line 1 \"saved-tool.cs\"", StringComparison.Ordinal)
                && !bound.Contains(label, StringComparison.Ordinal));
            Check("input property order does not change replay source", SavedTools.Bind(definition, Json("{\"count\":2,\"label\":\"same\"}")) == SavedTools.Bind(definition, Json("{\"label\":\"same\",\"count\":2}")));
            Reject("final expanded source limit enforced", () => SavedTools.Bind(definition with { Source = new string(' ', DynamicExecutor.MaxSourceBytes - 1) + ";" }, inputs));
            string definitionFile = Path.Combine(root, "definition.json"); File.WriteAllText(definitionFile, JsonSerializer.Serialize(definition, Protocol.Json));
            var output = new StringWriter(); var error = new StringWriter(); string cliId = Guid.NewGuid().ToString();
            Check("CLI saves the same contract", await RevitThyme.Cli.Program.RunAsync(["tool", "save", "--id", cliId, "--definition", definitionFile, "--json"], output, error) == 0);
            output.GetStringBuilder().Clear();
            Check("CLI reads persisted definition", await RevitThyme.Cli.Program.RunAsync(["tool", "show", "--id", cliId, "--json"], output, error) == 0 && output.ToString().Contains(definition.Name, StringComparison.Ordinal));
            using var process = Process.GetCurrentProcess();
            var binding = new SessionBinding(Protocol.Version, process.Id, process.StartTime.ToUniversalTime().Ticks.ToString(), Guid.NewGuid().ToString(), "", "2027");
            binding = binding with { PipeName = $"RevitThyme-Foundation-{binding.ProcessId}-{binding.SessionId}" };
            var native = new RecordingClient(binding);
            string session = Path.Combine(root, "session.json"), capture = Path.Combine(root, "capture.json"), inputFile = Path.Combine(root, "inputs.json");
            File.WriteAllText(session, JsonSerializer.Serialize(binding, Protocol.Json));
            File.WriteAllText(capture, JsonSerializer.Serialize(native.Inspection(), Protocol.Json)); File.WriteAllText(inputFile, inputs.GetRawText());
            string requestId = Guid.NewGuid().ToString();
            string[] run = ["tool", "run", "--id", cliId, "--digest", store.Get(cliId).Digest, "--inputs", inputFile, "--session", session,
                "--capture", capture, "--request-id", requestId, "--trusted", "true", "--json"];
            Check("CLI invokes exact captured trusted query", await RevitThyme.Cli.Program.RunAsync(run, output, error, _ => native) == 0
                && native.Last is { Operation: OperationKind.RunQuery, Code.Trusted: true } && native.Last.Id.ToString() == requestId && native.Last.Code.Source == bound);
            var connection = new RevitConnection(new(root, session), _ => native);
            var mcp = new RevitTools(connection); var context = await connection.Context(default);
            var invoked = await mcp.RunTool(context.CaptureId, Guid.NewGuid().ToString(), id, saved.Digest, inputs, true);
            Check("MCP uses identical shared input binding", invoked.IsError != true && native.Last?.Code?.Source == bound);
            var tested = await mcp.RunTool(context.CaptureId, Guid.NewGuid().ToString(), id, saved.Digest, inputs, true, testName: "example");
            Check("manual stored assertion uses rollback query", tested.IsError != true && native.Last is { Operation: OperationKind.RunQuery } && native.Last.Code!.Source.Contains("GetInt64() == 2", StringComparison.Ordinal));
            Check("no successful invocation invents tested state", store.Get(id).TestStatus == "not_run");
        }
        finally
        {
            Environment.SetEnvironmentVariable("REVITTHYME_TOOLS_DIRECTORY", before);
            Directory.Delete(root, true);
        }
    }
    private sealed class RecordingClient(SessionBinding binding) : IOperationClient
    {
        public OperationRequest? Last { get; private set; }
        public OperationResult Inspection() => new(Guid.NewGuid(), Outcome.Inspected, "inspected", "Recording transport boundary only.",
            new(new(binding.ProcessId, binding.ProcessStart, binding.SessionId, "fixture", "view", 1), "2027", "Fixture", "", false, false, "View", "FloorPlan", 1, true, null));
        public Task<OperationResult> ExecuteAsync(OperationRequest request, CancellationToken cancellationToken = default)
        { Last = request; return Task.FromResult(request.Operation == OperationKind.Inspect ? Inspection() with { Id = request.Id } : new OperationResult(request.Id, Outcome.Completed, "recorded", "Transport boundary; not native proof.")); }
    }
}
