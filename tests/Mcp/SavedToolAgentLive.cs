using System.Text.Json;
using RevitThyme.Codex;
using RevitThyme.Core;

namespace RevitThyme.McpTests;

// The same App Server integration used by the dockable panel, with its real model/runtime.
// All generated tool data and temporary authentication stay in a fresh owned run directory.
internal static class SavedToolAgentLive
{
    public static async Task Run(string folder, bool requireResultChecks = false)
    {
        folder = Path.GetFullPath(folder);
        var launch = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "launch.json"))).RootElement.Clone();
        string fixture = launch.GetProperty("fixture").GetString()!;
        if (launch.GetProperty("folder").GetString() != folder || fixture != Path.Combine(folder, "Fixture.rvt"))
            throw new InvalidDataException("Use the exact copied development fixture.");
        string sessionFile = Path.Combine(folder, "sessions", launch.GetProperty("session_id").GetString() + ".json");
        var binding = SessionBinding.Load(sessionFile);
        if (binding.ProcessId != launch.GetProperty("pid").GetInt32() || binding.ProcessStart != launch.GetProperty("start").GetString())
            throw new InvalidDataException("Owned process/start changed.");
        string root = Directory.GetCurrentDirectory();
        if (!File.Exists(Path.Combine(root, "RevitThyme.sln"))) throw new ArgumentException("Run from the candidate checkout root.");
        string evidence = Path.Combine(folder, (requireResultChecks ? "result-check-agent-" : "saved-tool-agent-") + Guid.NewGuid().ToString("N"));
        string agentHome = Path.Combine(evidence, "agent"), store = Path.Combine(evidence, "tools");
        Directory.CreateDirectory(Path.Combine(agentHome, "codex-home"));
        string auth = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "auth.json");
        string authCopy = Path.Combine(agentHome, "codex-home", "auth.json");
        string bundle = launch.GetProperty("bundle").GetString()!;
        string executable = Environment.GetEnvironmentVariable("REVITTHYME_QUALIFICATION_MCP") ?? Path.Combine(AppContext.BaseDirectory, "revitthyme-mcp.exe");
        if (!File.Exists(executable))
            executable = Path.Combine(root, "src", "RevitThyme.Mcp", "bin", new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name, "net10.0", "revitthyme-mcp.exe");
        string skills = Environment.GetEnvironmentVariable("REVITTHYME_QUALIFICATION_SKILLS") ?? Path.Combine(root, "skills");
        if (!Path.IsPathFullyQualified(skills) || !File.Exists(Path.Combine(skills, "create-revit-tool", "SKILL.md")))
            throw new ArgumentException("Qualification skills must be an absolute directory containing create-revit-tool.");
        var options = new AgentOptions(AgentOptions.FindRuntime(bundle), executable, sessionFile, Path.Combine(agentHome, "workspace"),
            skills, Path.Combine(agentHome, "skills"), true);
        var checks = new List<object>(); var events = new List<object>(); var completedTools = new List<string>();
        JsonElement? lastCall = null; int approvalCount = 0; bool agentVerifiedResult = false;
        string hash = SavedToolLive.Hash(fixture), originalAuthHash = SavedToolLive.Hash(auth);
        bool passed = false; string? failure = null;
        void Write(string name, object value) => File.WriteAllText(Path.Combine(evidence, name), JsonSerializer.Serialize(value, Core.Protocol.Json));
        void Check(string name, bool value) { checks.Add(new { name, passed = value }); Write("progress.json", checks); Program.Check(name, value); }
        try
        {
            File.Copy(auth, authCopy, false);
            await using (var agent = new AgentSession(options, value =>
            {
                var start = value.Launch();
                // App Server filters child environment; configure the private MCP store explicitly.
                start.ArgumentList.Add("-c");
                start.ArgumentList.Add("mcp_servers.revitthyme.env={REVITTHYME_TOOLS_DIRECTORY=" + JsonSerializer.Serialize(store.Replace('\\', '/')) + "}");
                return AppServerClient.Start(start);
            }))
            {
                agent.Event += e =>
                {
                    if (!e.Method.StartsWith("item/", StringComparison.Ordinal) && !e.Method.StartsWith("turn/", StringComparison.Ordinal)) return;
                    lock (events)
                    {
                        events.Add(e); Write("events.json", events);
                        if (e.Method == "item/started" && e.Parameters.TryGetProperty("item", out var started)
                            && AgentSession.Text(started, "type") == "mcpToolCall") lastCall = started.Clone();
                        if (e.Method == "item/completed" && e.Parameters.TryGetProperty("item", out var item)
                            && AgentSession.Text(item, "type") == "mcpToolCall" && AgentSession.Text(item, "status") == "completed")
                        {
                            string tool = AgentSession.Text(item, "tool"); completedTools.Add(tool);
                            if (requireResultChecks && tool.EndsWith("run_tool", StringComparison.Ordinal)
                                && (!item.GetProperty("arguments").TryGetProperty("testName", out var testName) || testName.ValueKind == JsonValueKind.Null))
                            {
                                var reply = item.GetProperty("result");
                                var native = reply.TryGetProperty("structuredContent", out var structured) && structured.ValueKind == JsonValueKind.Object ? structured
                                    : JsonDocument.Parse(reply.GetProperty("content")[0].GetProperty("text").GetString()!).RootElement;
                                agentVerifiedResult = native.TryGetProperty("verification", out var verified) && verified.ValueKind == JsonValueKind.Object
                                    && AgentSession.Text(verified, "status") == "passed"
                                    && AgentSession.Text(verified, "operationId") == AgentSession.Text(item.GetProperty("arguments"), "requestId");
                            }
                        }
                    }
                };
                agent.Approval += request => _ = Approve(request);
                async Task Approve(ServerRequest request)
                {
                    Write("approval-" + Interlocked.Increment(ref approvalCount) + ".json", request);
                    try
                    {
                        JsonElement call;
                        lock (events) call = lastCall ?? throw new InvalidOperationException("Approval has no active MCP call.");
                        string tool = AgentSession.Text(call, "tool"); var arguments = call.GetProperty("arguments");
                        bool allowed = tool == "save_tool" ? AgentSession.Text(arguments.GetProperty("definition"), "mode") == "query"
                            : tool == "run_tool" && new SavedToolStore(store).Get(AgentSession.Text(arguments, "toolId")).Definition.Mode == "query";
                        if (!allowed || request.Method != "mcpServer/elicitation/request")
                            throw new InvalidOperationException("Qualification approves only its generated saved query.");
                        var schema = request.Parameters.GetProperty("requestedSchema"); var content = new Dictionary<string, object>();
                        foreach (var property in schema.GetProperty("properties").EnumerateObject())
                        {
                            if (property.Value.TryGetProperty("enum", out var values))
                            {
                                string? accepted = values.EnumerateArray().Select(v => v.GetString()).FirstOrDefault(v => v is "Approve this call" or "Accept" or "Allow once" or "approve" or "approved");
                                content[property.Name] = accepted ?? throw new InvalidOperationException("Unsupported approval choices.");
                            }
                            else if (AgentSession.Text(property.Value, "type") == "boolean") content[property.Name] = true;
                            else throw new InvalidOperationException("Unsupported approval input.");
                        }
                        await agent.Respond(request, new { action = "accept", content });
                    }
                    catch (Exception error)
                    {
                        Write("approval-rejected.json", new { error = error.ToString() }); await agent.Decline(request);
                    }
                }
                await agent.Connect();
                Check("real Agent runtime has a signed-in account", (await agent.Account()).GetProperty("account").ValueKind == JsonValueKind.Object);
                var skill = (await agent.Skills()).Single(s => s.Name == "create-revit-tool");
                Check("runtime discovers bundled tool creation skill", skill.Path.StartsWith(skills, StringComparison.OrdinalIgnoreCase));
                await agent.NewConversation();
                Write("runtime.json", new { options.McpExecutable, mcpSha256 = SavedToolLive.Hash(executable), options.CodexExecutable, options.BundledSkills,
                    skillSha256 = SavedToolLive.Hash(skill.Path), agentAssemblySha256 = SavedToolLive.Hash(typeof(AgentSession).Assembly.Location), binding, agent.ThreadId });
                string marker = "agent-marker-" + Guid.NewGuid().ToString("N")[..8];
                string prompt = $"Create and save a reusable tool named Qualification View Marker. It must be a read-only query with one required string input named marker, returning a JSON object with properties marker (the supplied string) and viewName (the native active view name). Include a concrete executable assertion test with assumptions and expected results. Then retrieve the saved tool and run it with marker {marker}. This is an explicitly authorized trusted query only in disposable fixture {fixture}, process {binding.ProcessId}; inspect and verify this exact target first. Do not modify the model, save or sync, or execute any other code. Report the saved tool ID, digest and actual native returned values. Saving does not mean its tests passed.";
                if (requireResultChecks)
                    prompt += " Include a resultChecks plan that automatically observes the native active view name after execution and compares it with the exact name you inspected. Permit no added, modified or deleted elements. Use real native observation sources, not a constant true or a claimed result from the action. Run the saved tool and report its separate verification status and native observations. Also run its stored assertion test. A successful action alone does not mean its result checks passed.";
                Write("request.json", new { prompt });
                var send = agent.Send(prompt, skill);
                TurnOutcome outcome;
                try { outcome = await send.WaitAsync(TimeSpan.FromMinutes(4)); }
                catch (TimeoutException)
                {
                    if (agent.TurnId is not null) await agent.Interrupt();
                    await send.WaitAsync(TimeSpan.FromSeconds(30)); throw;
                }
                Check("plain-language creation turn completes", outcome.Status == "completed");
                lock (events)
                    Check("Agent actually saves, retrieves and runs a tool through MCP", new[] { "save_tool", "get_tool", "run_tool" }
                        .All(name => completedTools.Any(tool => tool.EndsWith(name, StringComparison.Ordinal))));
                if (requireResultChecks) Check("actual Agent action received a passing native result verification", agentVerifiedResult);
                var files = Directory.GetFiles(store, "*.json"); Check("one generated reusable definition is persisted", files.Length == 1);
                string id = Path.GetFileNameWithoutExtension(files.Single()); var artifact = new SavedToolStore(store).Get(id);
                Check("generated contract contains required typed input, expected result and executable test", artifact.Definition.Mode == "query"
                    && artifact.Definition.Inputs is [{ Name: "marker", Type: "string", Required: true }]
                    && artifact.Definition.Tests.Length > 0 && !string.IsNullOrWhiteSpace(artifact.Definition.ExpectedResult)
                    && artifact.TestStatus == "not_run");
                if (requireResultChecks)
                {
                    var definition = JsonSerializer.SerializeToElement(artifact.Definition, Core.Protocol.Json);
                    Check("Agent persisted an explicit automatic native result plan", definition.TryGetProperty("resultChecks", out var plan)
                        && plan.ValueKind == JsonValueKind.Object && plan.GetProperty("checks").GetArrayLength() > 0);
                }
                await using var client = await SavedToolLive.Client(sessionFile, store);
                var context = await Program.Call(client, "get_context");
                var snapshot = context.Data.GetProperty("result").GetProperty("snapshot");
                Check("independent capture remains pinned to fixture/process", context.Result.IsError != true
                    && snapshot.GetProperty("documentPath").GetString() == fixture
                    && snapshot.GetProperty("target").GetProperty("processId").GetInt32() == binding.ProcessId);
                string independentMarker = "independent-" + Guid.NewGuid().ToString("N")[..8];
                var result = await Program.Call(client, "run_tool", new()
                {
                    ["toolId"] = id, ["digest"] = artifact.Digest, ["captureId"] = context.Data.GetProperty("captureId").GetString(),
                    ["requestId"] = Guid.NewGuid().ToString(), ["inputs"] = new { marker = independentMarker }, ["trusted"] = true
                });
                Write("independent-native-result.json", new { artifact, result.Data, result.Result.IsError });
                Check("Agent-generated source compiles and accepts a new input in actual Revit", result.Result.IsError != true
                    && result.Data.GetProperty("execution").GetProperty("value").GetProperty("marker").GetString() == independentMarker
                    && result.Data.GetProperty("execution").GetProperty("value").GetProperty("viewName").GetString() == snapshot.GetProperty("viewName").GetString());
                if (requireResultChecks)
                    Check("Agent-generated native checks automatically verify the actual query", result.Data.GetProperty("verification").GetProperty("status").GetString() == "passed"
                        && result.Data.GetProperty("verification").GetProperty("checks").GetArrayLength() > 0);
                foreach (var test in artifact.Definition.Tests)
                {
                    var testContext = await Program.Call(client, "get_context");
                    var assertion = await Program.Call(client, "run_tool", new()
                    {
                        ["toolId"] = id, ["digest"] = artifact.Digest, ["captureId"] = testContext.Data.GetProperty("captureId").GetString(),
                        ["requestId"] = Guid.NewGuid().ToString(), ["inputs"] = test.Inputs, ["trusted"] = true, ["testName"] = test.Name
                    });
                    Write("generated-assertion-" + Array.IndexOf(artifact.Definition.Tests, test) + ".json", new { test.Name, assertion.Data, assertion.Result.IsError });
                    Check("Agent-generated assertion compiles and returns native boolean true: " + test.Name, assertion.Result.IsError != true
                        && assertion.Data.GetProperty("execution").GetProperty("value").ValueKind == JsonValueKind.True);
                }
                if (requireResultChecks)
                    await ResultCheckAgentLive.CheckSensitivity(client, artifact, independentMarker, snapshot.GetProperty("viewName").GetString()!, Check, Write);
                var after = await Program.Call(client, "get_context");
                Check("Agent creation and assertions preserve native view and modification state", after.Result.IsError != true
                    && after.Data.GetProperty("result").GetProperty("snapshot").GetProperty("viewName").GetString() == snapshot.GetProperty("viewName").GetString()
                    && after.Data.GetProperty("result").GetProperty("snapshot").GetProperty("isModified").GetBoolean() == snapshot.GetProperty("isModified").GetBoolean());
                Check("fixture file remains unchanged", SavedToolLive.Hash(fixture) == hash);
                passed = true;
            }
        }
        catch (Exception error) { failure = error.ToString(); throw; }
        finally
        {
            if (File.Exists(authCopy)) File.Delete(authCopy);
            bool authPreserved = SavedToolLive.Hash(auth) == originalAuthHash;
            Write("result.json", new { passed = passed && authPreserved, failure, checks, authCopyRemoved = !File.Exists(authCopy), originalAuthPreserved = authPreserved, fixture, store, hash });
            if (!authPreserved) throw new InvalidOperationException("Original authentication state changed during qualification.");
        }
        Console.WriteLine("Saved-tool Agent evidence: " + evidence);
    }
}
