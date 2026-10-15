using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using ModelContextProtocol.Client;
using RevitThyme.Core;

namespace RevitThyme.McpTests;

// External fixture qualification. It never launches Revit or creates/retargets a fixture.
internal static class ParameterIdentityLive
{
    public static async Task Run(string folder, string stage)
    {
        if (!Path.IsPathFullyQualified(folder) || stage is not ("inspect" or "edit" or "undo"))
            throw new ArgumentException("Use --parameter-identity-live ABSOLUTE_RUN_FOLDER inspect|edit|undo.");
        JsonElement Read(string file) => JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, file))).RootElement.Clone();
        var plan = Read("plan.json"); var launch = Read("launch.json"); var fixture = Read("fixture-info.json");
        if (fixture.TryGetProperty("execution", out var execution)) fixture = execution.GetProperty("value");
        string Text(JsonElement value, string property) => value.GetProperty(property).GetString()!;
        string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        string bundle = Text(launch, "bundle"), sessionId = Text(launch, "session_id"), documentId = Text(launch, "document_id");
        string cli = Path.Combine(bundle, "cli", "revitthyme.exe"), mcp = Path.Combine(bundle, "mcp", "revitthyme-mcp.exe");
        Program.Check("identity plan and launch are the exact disposable run", Text(plan, "folder") == folder && Text(launch, "folder") == folder
            && Text(plan, "fixture") == Path.Combine(folder, "Fixture.rvt") && Text(plan, "fixture") == Text(launch, "fixture"));
        Program.Check("identity launched manifest equals prepared candidate", Hash(Path.Combine(bundle, "manifest.json")) == Text(plan, "manifest_hash")
            && Text(launch, "manifest_hash") == Text(plan, "manifest_hash"));
        Program.Check("identity packaged CLI and MCP hashes match launch", Hash(cli) == Text(launch, "cli_hash") && Hash(mcp) == Text(launch, "mcp_hash"));
        Program.Check("identity launch has pinned session and document", Guid.TryParseExact(sessionId, "D", out _) && !string.IsNullOrWhiteSpace(documentId));
        string session = Path.Combine(folder, "sessions", sessionId + ".json"); var binding = SessionBinding.Load(session);
        Program.Check("identity session matches exact process/start/Revit 2027", binding.ProcessId == launch.GetProperty("pid").GetInt32()
            && binding.ProcessStart == Text(launch, "start") && binding.SessionId == sessionId && binding.RevitVersion == "2027");
        using var process = Process.GetProcessById(binding.ProcessId);
        Program.Check("identity native executable is the recorded Revit", process.ProcessName == "Revit" && process.MainModule?.FileName == Text(launch, "revit"));
        var evidence = new List<object>(); string element = Text(fixture, "elementUniqueId");
        await using var client = await McpClient.CreateAsync(new StdioClientTransport(new()
        { Command = mcp, Arguments = ["--session", session, "--sessions-directory", Path.Combine(folder, "sessions")], Name = "RevitThyme identity fixture" }));
        Snapshot SnapshotOf(JsonElement result)
        {
            var snapshot = JsonSerializer.Deserialize<Snapshot>(result.GetProperty("snapshot"), Core.Protocol.Json)!;
            Program.Check("identity result remains exact pinned process/session/document", snapshot.Target.ProcessId == binding.ProcessId
                && snapshot.Target.ProcessStart == binding.ProcessStart && snapshot.Target.SessionId == binding.SessionId
                && snapshot.Target.DocumentId == documentId && snapshot.DocumentPath == Text(plan, "fixture") && !snapshot.IsWorkshared);
            return snapshot;
        }
        async Task<JsonElement> Call(string tool, Dictionary<string, object?>? args = null, string? expected = null)
        {
            var call = await Program.Call(client, tool, args);
            int responseBytes = JsonSerializer.SerializeToUtf8Bytes(call.Result).Length;
            int operationBytes = System.Text.Encoding.UTF8.GetByteCount(call.Data.GetRawText());
            evidence.Add(new { tool, args, responseBytes, operationBytes, error = call.Result.IsError, data = call.Data });
            Program.Check("identity actual MCP response remains within byte budget", responseBytes <= Core.Protocol.MaxBytes && operationBytes <= Core.Protocol.MaxBytes);
            Program.Check("identity " + tool + " " + (expected ?? "succeeds"), expected is null ? call.Result.IsError != true
                : Text(call.Data, "code") == expected && call.Result.IsError == (expected is not ("applied" or "inspected" or "unchanged")));
            return call.Data;
        }
        async Task<(string Capture, Snapshot Snapshot)> Context()
        {
            var value = await Call("get_context"); return (Text(value, "captureId"), SnapshotOf(value.GetProperty("result")));
        }
        async Task<JsonElement> Cli(params string[] arguments)
        {
            var start = new ProcessStartInfo(cli) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (string argument in arguments) start.ArgumentList.Add(argument);
            using var child = Process.Start(start)!; var stdout = child.StandardOutput.ReadToEndAsync(); var stderr = child.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90)); await child.WaitForExitAsync(timeout.Token);
            string output = await stdout; evidence.Add(new { cli = arguments, exit = child.ExitCode, output, error = await stderr });
            Program.Check("identity packaged CLI " + arguments[0], child.ExitCode == 0); return JsonDocument.Parse(output).RootElement.Clone();
        }
        async Task<(ParameterInfo[] Parameters, int[] Offsets)> Pages(string capture, Snapshot before, bool useCli)
        {
            var parameters = new List<ParameterInfo>(); var offsets = new List<int>(); int? offset = 0;
            string captureFile = Path.Combine(folder, "identity-cli-capture.json");
            while (offset is not null)
            {
                int position = offset.Value; offsets.Add(position); JsonElement data;
                if (useCli)
                {
                    var args = new List<string> { "inspect-elements", "--session", session, "--element", element, "--json" };
                    if (position > 0) args.AddRange(["--capture", captureFile, "--parameter-offset", position.ToString(CultureInfo.InvariantCulture)]);
                    data = await Cli(args.ToArray()); if (position == 0) File.WriteAllText(captureFile, data.GetRawText());
                }
                else data = await Call("inspect_elements", new() { ["captureId"] = capture, ["elementIds"] = new[] { element }, ["parameterOffset"] = position }, "inspected");
                var snapshot = SnapshotOf(data);
                Program.Check("identity continuation preserves revision and modified flag", snapshot.Target == before.Target && snapshot.IsModified == before.IsModified);
                var detail = JsonSerializer.Deserialize<ElementDetail>(data.GetProperty("details")[0], Core.Protocol.Json)!;
                Program.Check("identity page uses exact element and advances", detail.Element.UniqueId == element && detail.Parameters.Length <= 20
                    && (detail.NextParameterOffset is null || detail.NextParameterOffset == position + detail.Parameters.Length && detail.Parameters.Length > 0));
                parameters.AddRange(detail.Parameters); offset = detail.NextParameterOffset;
            }
            Program.Check("identity pages retain distinct exact parameter IDs in native order", parameters.Select(p => p.Id).Distinct().Count() == parameters.Count
                && parameters.Select(p => long.Parse(p.Id, CultureInfo.InvariantCulture)).SequenceEqual(parameters.Select(p => long.Parse(p.Id, CultureInfo.InvariantCulture)).Order()));
            return (parameters.ToArray(), offsets.ToArray());
        }
        ParameterInfo[] Oracle()
        {
            var value = Read("oracle-before.json"); if (value.TryGetProperty("execution", out var result)) value = result.GetProperty("value");
            return JsonSerializer.Deserialize<ParameterInfo[]>(value.GetProperty("parameters"), Core.Protocol.Json)!;
        }
        try
        {
            Program.Check("identity official SDK exposes parameter tools", (await client.ListToolsAsync()).Any(t => t.Name == "inspect_elements"));
            var context = await Context(); var before = context.Snapshot;
            var baseline = Oracle(); var current = await Pages(context.Capture, before, false);
            if (stage == "inspect")
            {
                var cliPages = await Pages(context.Capture, before, true);
                Program.Check("identity MCP and packaged CLI match direct API oracle exactly", current.Parameters.SequenceEqual(baseline) && cliPages.Parameters.SequenceEqual(baseline));
                Program.Check("identity CLI and MCP preserve native pagination boundaries", current.Offsets.Length > 1 && current.Offsets.SequenceEqual(cliPages.Offsets));
                string sameName = Text(fixture, "sameName"); var duplicates = baseline.Where(p => p.Name == sameName).ToArray();
                var shared = duplicates.Where(p => p.IsShared).ToArray();
                Program.Check("identity same-name built-in and separate shared GUIDs survive", duplicates.Any(p => p.BuiltInParameter is not null && !p.IsShared && p.SharedGuid is null)
                    && shared.Length >= 2 && shared.Select(p => p.SharedGuid).Distinct().Count() == shared.Length && shared.All(p => p.SharedGuid is not null && p.BuiltInParameter is null));
                Program.Check("identity shared status guards all nullable GUIDs", baseline.All(p => p.IsShared == (p.SharedGuid is not null)));
                var ordinary = baseline.Single(p => p.Id == Text(fixture, "ordinaryParameterId"));
                Program.Check("identity designated nonshared non-built-in field retains null identities", !ordinary.IsShared && ordinary.SharedGuid is null && ordinary.BuiltInParameter is null);
                var duplicatePages = shared.Select(p => Array.FindLastIndex(current.Offsets, offset => offset <= Array.FindIndex(baseline, item => item.Id == p.Id))).Distinct();
                Program.Check("identity duplicate shared fields cross a page boundary", duplicatePages.Count() > 1);
                var cases = fixture.GetProperty("expectedCases"); var nativeStates = fixture.GetProperty("nativeHasValue");
                ParameterInfo Case(string name) => baseline.Single(p => p.Id == Text(cases, name));
                Program.Check("identity designated assigned field retains native HasValue", Case("assigned").HasValue && nativeStates.GetProperty("assigned").GetBoolean()
                    && !string.IsNullOrEmpty(Case("assigned").Value.Text));
                Program.Check("identity designated unassigned integer retains raw zero and native false", !Case("unassigned").HasValue
                    && !nativeStates.GetProperty("unassigned").GetBoolean() && Case("unassigned").Value == new ParameterValue("Integer", Integer: 0));
                Program.Check("identity designated assigned zero retains exact native state", Case("zero").Value == new ParameterValue("Integer", Integer: 0)
                    && Case("zero").HasValue == nativeStates.GetProperty("zero").GetBoolean());
                Program.Check("identity designated empty string retains exact native state", Case("empty").Value == new ParameterValue("String", Text: "")
                    && Case("empty").HasValue == nativeStates.GetProperty("empty").GetBoolean());
                var longText = baseline.Single(p => p.Id == Text(fixture, "longStringParameterId"));
                Program.Check("identity long string remains clipped to 512 and blocked", longText.Truncated && longText.Value.Text?.Length == 512);
                var after = await Context(); Program.Check("identity pure inspection preserves target revision and modified flag", after.Snapshot.Target == before.Target && after.Snapshot.IsModified == before.IsModified);
                File.WriteAllText(Path.Combine(folder, "identity-baseline.json"), JsonSerializer.Serialize(baseline, Core.Protocol.Json));
            }
            else if (stage == "edit")
            {
                Program.Check("identity edit begins with oracle baseline", current.Parameters.SequenceEqual(baseline));
                var selected = baseline.Single(p => p.SharedGuid == Guid.Parse(Text(fixture, "selectedSharedGuid")));
                var wanted = new ParameterValue("String", Text: Text(fixture, "testValue"));
                var edit = new ParameterEdit(element, selected.Id, selected.Value, wanted);
                async Task<JsonElement> Set(string capture, ParameterEdit change, string code) => await Call("set_parameters", new()
                { ["captureId"] = capture, ["requestId"] = Guid.NewGuid().ToString(), ["edits"] = new[] { change } }, code);
                var readOnly = baseline.First(p => p.IsReadOnly && p.Value.Storage is "String" or "Integer" or "Double");
                await Set(context.Capture, new(element, readOnly.Id, readOnly.Value, readOnly.Value), "parameter_blocked");
                var truncated = baseline.Single(p => p.Id == Text(fixture, "longStringParameterId"));
                await Set(context.Capture, new(element, truncated.Id, truncated.Value, new("String", Text: "blocked")), "parameter_blocked");
                await Set(context.Capture, edit with { Expected = new("String", Text: "deliberately stale") }, "stale_parameter");
                var guarded = await Context(); Program.Check("identity rejected edits do not mutate revision or modified flag", guarded.Snapshot.Target == before.Target && guarded.Snapshot.IsModified == before.IsModified);
                await Set(context.Capture, edit, "applied");
                await Call("inspect_elements", new() { ["captureId"] = context.Capture, ["elementIds"] = new[] { element } }, "stale_target");
                context = await Context(); var after = await Pages(context.Capture, context.Snapshot, false);
                Program.Check("identity exact-ID edit changes only chosen duplicate", after.Parameters.Length == baseline.Length && after.Parameters.All(p => p ==
                    (p.Id == selected.Id ? baseline.Single(b => b.Id == p.Id) with { Value = wanted, HasValue = true } : baseline.Single(b => b.Id == p.Id))));
                await Set(context.Capture, edit, "stale_parameter");
                var rejected = await Context(); Program.Check("identity stale expected after edit preserves revision", rejected.Snapshot.Target == context.Snapshot.Target);
                File.WriteAllText(Path.Combine(folder, "identity-edit-ready.json"), JsonSerializer.Serialize(new { edit, snapshot = rejected.Snapshot, next = "Perform one native Undo, then run undo stage." }, Core.Protocol.Json));
            }
            else
            {
                Program.Check("identity post-Undo readback restores every original raw value and identity", current.Parameters.SequenceEqual(baseline));
                Program.Check("identity undo verification follows completed exact-ID edit", File.Exists(Path.Combine(folder, "identity-edit-ready.json")));
            }
            File.WriteAllText(Path.Combine(folder, "identity-" + stage + "-complete.json"), JsonSerializer.Serialize(new
            { passed = true, stage, sourceRevision = Text(plan, "source_revision"), manifestHash = Text(plan, "manifest_hash"), binding, fixture, before, evidence }, Core.Protocol.Json));
        }
        catch
        {
            File.WriteAllText(Path.Combine(folder, "identity-" + stage + "-failed.json"), JsonSerializer.Serialize(evidence, Core.Protocol.Json)); throw;
        }
    }
}
