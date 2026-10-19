using System.Diagnostics;
using System.Text.Json;
using RevitThyme.Core;

namespace RevitThyme.McpTests;

internal static class DynamicLive
{
    public static async Task Run(string folder)
    {
        using var ready = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "ready.json")));
        using var launch = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "launch.json")));
        var binding = ready.RootElement.GetProperty("binding");
        Program.Check("dynamic process/start matches owned launch", binding.GetProperty("processId").GetInt32() == launch.RootElement.GetProperty("pid").GetInt32()
            && binding.GetProperty("processStart").GetString() == launch.RootElement.GetProperty("start").GetString());
        string session = Path.Combine(folder, "sessions", binding.GetProperty("sessionId").GetString() + ".json");
        await using var client = await Program.Client(session, Path.Combine(folder, "sessions"));
        var evidence = new List<object>();
        async Task<JsonElement> Call(string tool, Dictionary<string, object?>? args = null, string? expected = null)
        {
            var call = await Program.Call(client, tool, args); evidence.Add(new { tool, args, error = call.Result.IsError, data = call.Data });
            Program.Check(tool + " " + (expected ?? "succeeds"), expected is null ? call.Result.IsError != true
                : call.Data.GetProperty("code").GetString() == expected && call.Result.IsError == (expected is not ("applied" or "inspected")));
            return call.Data;
        }
        async Task<string> Context()
        {
            var value = await Call("get_context");
            Program.Check("dynamic capture remains exact disposable fixture", value.GetProperty("result").GetProperty("snapshot").GetProperty("documentPath").GetString() == ready.RootElement.GetProperty("fixture").GetString());
            return value.GetProperty("captureId").GetString()!;
        }
        Dictionary<string, object?> Args(string capture, string source, bool trusted = true) => new() { ["captureId"] = capture, ["requestId"] = Guid.NewGuid().ToString(), ["source"] = source, ["trusted"] = trusted };
        try
        {
            Program.Check("live official SDK exposes 22 tools", (await client.ListToolsAsync()).Count == 22);
            string capture = await Context();
            await Call("run_query", Args(capture, "return doc.Title;", false), "trusted_code_disabled");
            var invalid = await Call("run_query", Args(capture, "return missing_symbol;"), "compile_error");
            Program.Check("MCP exposes structured Roslyn diagnostics", invalid.GetProperty("execution").GetProperty("diagnostics").GetArrayLength() > 0);
            var query = await Call("run_query", Args(capture, "return new { name = doc.ActiveView.Name, count = new FilteredElementCollector(doc).GetElementCount() };"), "inspected");
            Program.Check("MCP query returns actual native value", query.GetProperty("execution").GetProperty("value").GetProperty("name").GetString() == "Foundation Plan");
            capture = await Context();
            await Call("run_modify", Args(capture, "doc.ActiveView.Name = \"Must roll back\"; throw new Exception(\"expected\");"), "runtime_error");
            capture = await Context();
            string source = "doc.ActiveView.Name = \"Dynamic modified\"; var level = new FilteredElementCollector(doc).OfClass(typeof(Level)).FirstElementId(); var type = new FilteredElementCollector(doc).OfClass(typeof(WallType)).Cast<WallType>().First(t=>t.Kind==WallKind.Basic); for(int i=0;i<2;i++) Wall.Create(doc,Line.CreateBound(new XYZ(0,i*15,0),new XYZ(10,i*15,0)),type.Id,level,9,0,false,false); return new { done=true };";
            var args = Args(capture, source);
            var changed = await Call("run_modify", args, "applied");
            Program.Check("native change report includes actual additions and modified view", changed.GetProperty("execution").GetProperty("changes").GetProperty("added").GetArrayLength() >= 2
                && changed.GetProperty("execution").GetProperty("changes").GetProperty("modified").GetArrayLength() >= 1);
            await Call("run_modify", args, "applied");
            await Call("get_operation", new() { ["requestId"] = args["requestId"] }, "applied");
            var conflicting = new Dictionary<string, object?>(args) { ["source"] = "return 2;" }; await Call("run_modify", conflicting, "id_conflict");
            await Call("run_query", Args(capture, "return 1;"), "stale_target");
            File.WriteAllText(Path.Combine(folder, "smoke-complete.json"), "{}");
            var wait = Stopwatch.StartNew();
            while (!File.Exists(Path.Combine(folder, "cabin-ready.json")))
            {
                if (File.Exists(Path.Combine(folder, "native-result.json")) || wait.Elapsed.TotalSeconds > 90) throw new TimeoutException("Native Undo/Redo verification did not complete.");
                await Task.Delay(500);
            }
            // CLI uses the same operation and a caller-owned ID, recoverable after disconnect.
            var pipe = new PipeClient(SessionBinding.Load(session));
            var inspection = await pipe.ExecuteAsync(new(Guid.NewGuid(), OperationKind.Inspect));
            string captureFile = Path.Combine(folder, "cli-capture.json"), sourceFile = Path.Combine(folder, "cli-query.csx");
            File.WriteAllText(captureFile, JsonSerializer.Serialize(inspection, Core.Protocol.Json)); File.WriteAllText(sourceFile, "return new { walls = new FilteredElementCollector(doc).OfClass(typeof(Wall)).GetElementCount() };");
            string cliId = Guid.NewGuid().ToString();
            var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (string argument in new[] { Path.Combine(folder, "bundle", "cli", "revitthyme.dll"), "run-query", "--session", session, "--capture", captureFile, "--source", sourceFile, "--trusted", "true", "--request-id", cliId, "--json" }) start.ArgumentList.Add(argument);
            using (var p = Process.Start(start)!)
            {
                var stdout = p.StandardOutput.ReadToEndAsync(); var stderr = p.StandardError.ReadToEndAsync(); await p.WaitForExitAsync();
                string text = await stdout; evidence.Add(new { cli = "run-query", exit = p.ExitCode, text, error = await stderr });
                Program.Check("CLI executes trusted native C# query", p.ExitCode == 0 && JsonDocument.Parse(text).RootElement.GetProperty("execution").GetProperty("value").GetProperty("walls").GetInt32() == 0);
            }
            await Call("get_operation", new() { ["requestId"] = cliId }, "inspected");
            capture = await Context();
            string cabin = File.ReadAllText(Path.Combine(folder, "bundle", "eval", "build-cabin.csx"));
            var cabinArgs = Args(capture, cabin); cabinArgs["budgetMilliseconds"] = 30000;
            var result = await Call("run_modify", cabinArgs, "applied");
            File.WriteAllText(Path.Combine(folder, "cabin-metrics.json"), JsonSerializer.Serialize(new { toolCalls = 1, errors = 0,
                elapsedMilliseconds = result.GetProperty("execution").GetProperty("elapsedMilliseconds").GetInt64(), sourceSha256 = result.GetProperty("execution").GetProperty("sourceSha256").GetString() }, Core.Protocol.Json));
            Program.Check("cabin code executed with native change report and source hash", result.GetProperty("execution").GetProperty("sourceSha256").GetString()!.Length == 64 && result.GetProperty("execution").GetProperty("changes").GetProperty("added").GetArrayLength() > 10);
            File.WriteAllText(Path.Combine(folder, "dynamic-mcp-complete.json"), JsonSerializer.Serialize(new { passed = true, evidence, elapsedMilliseconds = wait.ElapsedMilliseconds }, Core.Protocol.Json));
        }
        catch { File.WriteAllText(Path.Combine(folder, "dynamic-mcp-failed.json"), JsonSerializer.Serialize(evidence, Core.Protocol.Json)); throw; }
    }
}
