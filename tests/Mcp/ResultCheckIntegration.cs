using System.Diagnostics;
using System.Text.Json;
using RevitThyme.Core;
using RevitThyme.Mcp;

namespace RevitThyme.McpTests;

internal static class ResultCheckIntegration
{
    private static void Check(string name, bool value) => Program.Check("result checks: " + name, value);
    private static JsonElement Json(string text) { using var doc = JsonDocument.Parse(text); return doc.RootElement.Clone(); }
    public static async Task Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "RevitThyme-checks-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        string? previous = Environment.GetEnvironmentVariable("REVITTHYME_TOOLS_DIRECTORY"); Environment.SetEnvironmentVariable("REVITTHYME_TOOLS_DIRECTORY", root);
        try
        {
            using var process = Process.GetCurrentProcess();
            var binding = new SessionBinding(Protocol.Version, process.Id, process.StartTime.ToUniversalTime().Ticks.ToString(), Guid.NewGuid().ToString(), "", "2027");
            binding = binding with { PipeName = $"RevitThyme-Foundation-{binding.ProcessId}-{binding.SessionId}" };
            await ResultCheckBoundaryIntegration.Run(binding);
            string session = Path.Combine(root, "session.json"); File.WriteAllText(session, JsonSerializer.Serialize(binding, Protocol.Json));
            var plan = new ResultCheckPlan([new("native name", "return new { exists=true, value=doc.ActiveView.Name };", ExpectedInput: "name")],
                "return new { modified=new long[] {doc.ActiveView.Id.Value}, deleted=Array.Empty<long>() };");
            var inputs = Json("{\"name\":\"wanted\"}");
            var tool = new SavedToolDefinition("Rename", "Rename native view", "Rename with independent native check", "modify",
                "doc.ActiveView.Name=inputs.GetProperty(\"name\").GetString(); return new {success=true};", [new("name", "string", "Desired name")], "Native name changed",
                [new("example", inputs, "Disposable view", "Name wanted", "return doc.ActiveView.Name == \"wanted\";")], plan);
            var artifact = new SavedToolStore().Save(Guid.NewGuid().ToString(), tool);
            var code = SavedTools.Invocation(artifact, artifact.Digest, inputs, true, 10000);
            Check("checked action source binds exact saved definition digest", code.Source.Contains(artifact.Digest, StringComparison.Ordinal));
            Check("changed check plan changes action source attribution", SavedTools.Invocation(artifact with { Digest = new string('f', 64) }, new string('f', 64), inputs, true, 10000).Source != code.Source);
            ResultCheckCoordinator Coordinator(ResultCheckFakeClient client, Guid id, ResultCheckPlan? p = null, JsonElement? values = null, string? hash = null) =>
                new(client, id, p ?? plan, values ?? inputs, hash ?? ResultChecks.Hash(code.Source), true);
            var native = new ResultCheckFakeClient(binding); var connection = new RevitConnection(new(root, session), _ => native); var mcp = new RevitTools(connection);
            var capture = await connection.Context(default); Guid requestId = Guid.NewGuid();
            var reply = await mcp.RunTool(capture.CaptureId, requestId.ToString(), artifact.ToolId, artifact.Digest, inputs, true);
            var passed = reply.StructuredContent!.Value.Deserialize<OperationResult>(Protocol.Json)!;
            Check("MCP configured tool checks actual native observations", reply.IsError != true && passed.Outcome == Outcome.Applied && passed.Verification?.Status == "passed" && passed.Execution?.Value?.GetProperty("success").GetBoolean() == true);
            Check("phase IDs deterministic and preparation precedes the single action", passed.Verification?.PreparationId == Coordinator(native, requestId).PreparationId
                && native.Requests.Where(r => r.Operation != OperationKind.GetOperation && r.Operation != OperationKind.Inspect).Select(r => r.Operation).SequenceEqual([OperationKind.RunObservation, OperationKind.RunModify, OperationKind.RunObservation]) && native.Actions == 1);
            var generatedObserver = native.Requests.Last(r => r.Operation == OperationKind.RunObservation).Code!.Source;
            int extra = DynamicExecutor.MaxSourceBytes - System.Text.Encoding.UTF8.GetByteCount(generatedObserver) + 1;
            var headerOverflowPlan = plan with { Checks = [plan.Checks[0] with { Source = plan.Checks[0].Source + new string(' ', extra) }] };
            var neverDispatched = new ResultCheckFakeClient(binding); bool headerRejected = false;
            try { _ = Coordinator(neverDispatched, Guid.NewGuid(), headerOverflowPlan); }
            catch (ArgumentException) { headerRejected = true; }
            Check("digest header source overflow rejects before any native request or action", headerRejected && neverDispatched.Requests.Count == 0 && neverDispatched.Actions == 0);
            var recovered = await mcp.VerifyTool(requestId.ToString(), artifact.ToolId, artifact.Digest, inputs, true);
            Check("saved recovery uses retained checks and never reexecutes action", recovered.IsError != true && native.Actions == 1 && native.Requests.Count(r => r.Operation == OperationKind.RunObservation) == 2);
            foreach (var scenario in new[] { "no-write", "extra-id", "missing-target", "compile_error", "runtime_error", "result_too_large", "missing-data", "stale", "timeout", "disconnect" })
            {
                var fake = new ResultCheckFakeClient(binding)
                {
                    Observed = scenario == "no-write" ? "old" : "wanted", ExtraModified = scenario == "extra-id", Exists = scenario != "missing-target",
                    ObservationError = scenario is "compile_error" or "runtime_error" or "result_too_large" ? scenario : null,
                    MissingObservation = scenario == "missing-data", InterveningRevision = scenario == "stale", TimeoutAfterCommit = scenario == "timeout", FailVerificationTransport = scenario == "disconnect"
                };
                Guid id = Guid.NewGuid(); var result = await Coordinator(fake, id).Run(new(id, OperationKind.RunModify, fake.Snapshot.Target, Code: code));
                string expected = scenario switch { "timeout" => "passed", "disconnect" or "stale" => "unconfirmed", "compile_error" or "runtime_error" or "result_too_large" or "missing-data" => "error", _ => "failed" };
                Check(scenario + " preserves applied receipt and truthful verification", result.Outcome == Outcome.Applied && result.Execution?.Changes is not null && result.Verification?.Status == expected && fake.Actions == 1);
                if (scenario == "extra-id") Check("unexpected native IDs flagged despite semantic pass", result.Verification?.Checks?[0].Status == "passed" && result.Verification.UnexpectedChanges?.Modified.SequenceEqual([2L]) == true);
                if (scenario == "timeout") Check("commit response timeout recovers original ID exactly once", fake.Requests.Count(r => r.Operation == OperationKind.RunModify) == 1 && result.Id == id);
            }
            var core = Coordinator(native, requestId);
            foreach (string lookup in new[] { "pending", "not_found", "host_stopped" })
            {
                foreach (bool uncertain in new[] { true, false })
                {
                    var lost = new ResultCheckFakeClient(binding) { ReturnTransportLostAfterCommit = uncertain, ActionLookupFault = lookup };
                    Guid lostId = Guid.NewGuid();
                    var preserved = await Coordinator(lost, lostId).Run(new(lostId, OperationKind.RunModify, lost.Snapshot.Target, Code: code));
                    Check((uncertain ? "returned transport_lost" : "known applied") + " plus " + lookup + " preserves original identity/outcome",
                        preserved.Id == lostId && preserved.Outcome == (uncertain ? Outcome.Unconfirmed : Outcome.Applied)
                        && preserved.Verification?.Status == "unconfirmed" && lost.Actions == 1 && lost.Requests.Count(r => r.Operation == OperationKind.RunModify) == 1);
                }
            }
            Guid unknownId = Guid.NewGuid(); var unknown = await Coordinator(native, unknownId).Verify();
            Check("public missing-history verification identifies original unresolved action", unknown.Id == unknownId && unknown.Outcome == Outcome.Unconfirmed);
            foreach (var numbers in new[] { ("{\"x\":[1,{\"y\":1e100}]}", "{\"x\":[1.0,{\"y\":10e99}]}"), ("[0,-0.0,1.2300]", "[-0,0e50,123e-2]") })
            {
                var nestedPlan = plan with { Checks = [plan.Checks[0] with { ExpectedInput = null, Expected = Json(numbers.Item1) }] };
                var nested = new ResultCheckFakeClient(binding) { ObservedJson = Json(numbers.Item2) }; Guid nestedId = Guid.NewGuid();
                Check("nested JSON numeric equality is semantic: " + numbers.Item1,
                    (await Coordinator(nested, nestedId, nestedPlan).Run(new(nestedId, OperationKind.RunModify, nested.Snapshot.Target, Code: code))).Verification?.Status == "passed");
            }
            Check("nested numeric lexical forms bind identical deterministic recovery IDs",
                Coordinator(native, requestId, values: Json("{\"name\":[1,{\"x\":1.0}]}" )).PreparationId
                == Coordinator(native, requestId, values: Json("{\"name\":[1e0,{\"x\":10e-1}]}" )).PreparationId);
            Check("canonical integer inputs preserve GetInt64 authoring semantics", ResultChecks.CanonicalInputs(Json("{\"count\":20,\"other\":10e-1}")).GetProperty("count").GetInt64() == 20
                && ResultChecks.CanonicalInputs(Json("{\"count\":20,\"other\":10e-1}")).GetProperty("other").GetInt64() == 1);
            Check("changed plan recovery cannot pass", (await Coordinator(native, requestId, plan with { MaxAdded = 1, AddedCategoryIds = [-2000011] }).Verify()).Verification?.Status == "unconfirmed");
            Check("changed inputs recovery cannot pass", (await Coordinator(native, requestId, values: Json("{\"name\":\"other\"}")).Verify()).Verification?.Status == "unconfirmed");
            Check("changed source recovery cannot pass", (await Coordinator(native, requestId, hash: new string('0', 64)).Verify()).Verification?.Status == "unconfirmed");
            native.History.Remove(core.PreparationId);
            Check("missing retained pre-action policy never passes", (await core.Verify()).Verification?.Status == "unconfirmed");
            Check("missing original native history never passes", (await Coordinator(native, Guid.NewGuid()).Verify()).Verification?.Status == "unconfirmed");
            var formatted = ResultChecks.Observation("return true;", Json("{\n \"a\": 1\n}"));
            Check("public observation helper hashes serialized context exactly", formatted.Source.StartsWith("// observation-data-sha256:" + ResultChecks.Hash(JsonSerializer.Serialize(formatted.ObservationData, Protocol.Json)) + "\n", StringComparison.Ordinal));
            var nullExpected = JsonSerializer.Deserialize<ResultCheck>("{\"name\":\"null value\",\"source\":\"return null;\",\"expected\":null}", Protocol.Json)!;
            ResultChecks.Validate(plan with { Checks = [nullExpected] });
            Check("explicit expected JSON null is distinct from omitted expected", nullExpected.Expected is { ValueKind: JsonValueKind.Null }
                && JsonSerializer.Serialize(nullExpected, Protocol.Json).Contains("\"expected\":null", StringComparison.Ordinal));
            // CLI uses the same retained coordinator and preserves applied + failure with exit 1.
            string inputFile = Path.Combine(root, "inputs.json"), captureFile = Path.Combine(root, "capture.json"), planFile = Path.Combine(root, "plan.json");
            File.WriteAllText(inputFile, inputs.GetRawText()); File.WriteAllText(planFile, JsonSerializer.Serialize(plan, Protocol.Json));
            var cliNative = new ResultCheckFakeClient(binding) { Observed = "old" }; Guid cliId = Guid.NewGuid();
            File.WriteAllText(captureFile, JsonSerializer.Serialize(new OperationResult(Guid.NewGuid(), Outcome.Inspected, "inspected", "Capture", cliNative.Snapshot), Protocol.Json));
            var output = new StringWriter(); var errorWriter = new StringWriter();
            string[] cliArgs = ["tool", "run", "--id", artifact.ToolId, "--digest", artifact.Digest, "--inputs", inputFile, "--session", session, "--capture", captureFile, "--request-id", cliId.ToString(), "--trusted", "true", "--json"];
            int exit = await RevitThyme.Cli.Program.RunAsync(cliArgs, output, errorWriter, _ => cliNative);
            var cliResult = JsonSerializer.Deserialize<OperationResult>(output.ToString(), Protocol.Json)!;
            Check("CLI exits failure while retaining applied action", exit == 1 && cliResult.Outcome == Outcome.Applied && cliResult.Verification?.Status == "failed");
            output.GetStringBuilder().Clear();
            string[] verifyArgs = ["tool", "verify", "--id", artifact.ToolId, "--digest", artifact.Digest, "--inputs", inputFile, "--session", session, "--request-id", cliId.ToString(), "--trusted", "true", "--json"];
            Check("CLI saved verification recovers without capture/reexecution", await RevitThyme.Cli.Program.RunAsync(verifyArgs, output, errorWriter, _ => cliNative) == 1 && cliNative.Actions == 1);
            await using var stdio = await Program.Client(directory: Path.Combine(root, "absent"));
            var tools = await stdio.ListToolsAsync();
            foreach (string name in new[] { "prepare_result_checks", "verify_result", "verify_tool" })
            {
                var item = tools.Single(t => t.Name == name);
                Check(name + " advertises trusted/destructive idempotent operation and object inputs", item.ProtocolTool.Annotations is { ReadOnlyHint: false, DestructiveHint: true, IdempotentHint: true }
                    && item.JsonSchema.GetProperty("properties").GetProperty("inputs").GetProperty("type").GetString() == "object");
            }
            foreach (string name in new[] { "prepare_result_checks", "verify_result", "save_tool" })
            {
                var properties = tools.Single(t => t.Name == name).JsonSchema.GetProperty("properties");
                var checkSchema = name == "save_tool" ? properties.GetProperty("definition").GetProperty("properties").GetProperty("resultChecks") : properties.GetProperty("plan");
                var expectedSchema = checkSchema.GetProperty("properties").GetProperty("checks").GetProperty("items").GetProperty("properties").GetProperty("expected");
                Check(name + " advertises concrete arbitrary JSON expected schema", expectedSchema.GetProperty("anyOf").GetArrayLength() == 6);
            }
            var saved = await Program.Call(stdio, "save_tool", new() { ["toolId"] = artifact.ToolId, ["definition"] = tool });
            Check("stdio stores structured result plan exactly", saved.Result.IsError != true && saved.Data.GetProperty("definition").GetProperty("resultChecks").GetProperty("checks")[0].GetProperty("expectedInput").GetString() == "name");
        }
        finally { Environment.SetEnvironmentVariable("REVITTHYME_TOOLS_DIRECTORY", previous); Directory.Delete(root, true); }
    }
}
