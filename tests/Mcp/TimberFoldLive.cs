using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using RevitThyme.Core;

namespace RevitThyme.McpTests;

internal static class TimberFoldLive
{
    public static async Task Run(string folder, string fixtureReceipt)
    {
        if (!Path.IsPathFullyQualified(folder) || !Path.IsPathFullyQualified(fixtureReceipt)) throw new ArgumentException("Use absolute development-run and fixture-receipt paths.");
        using var planJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "plan.json")));
        using var launchJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "launch.json")));
        using var fixtureJson = JsonDocument.Parse(File.ReadAllText(fixtureReceipt));
        var plan = planJson.RootElement; var launch = launchJson.RootElement; var fixture = fixtureJson.RootElement;
        string model = Text(plan, "fixture"), original = Text(plan, "fixture_source"), bundle = Text(launch, "bundle");
        string manifestHash = Text(plan, "manifest_hash"), originalHash = Text(fixture, "rvtSha256");
        string sessionId = Text(launch, "session_id"), session = Path.Combine(folder, "sessions", sessionId + ".json");
        Program.Check("native TimberFold launch and original fixture receipts agree", Text(plan, "folder") == folder && Text(launch, "folder") == folder &&
            Text(launch, "fixture") == model && Text(fixture, "modelPath") == original && Property(fixture, "nativeFixtureChecksPassed").GetBoolean() &&
            Hash(model) == originalHash.ToLowerInvariant() && Hash(original) == originalHash.ToLowerInvariant() &&
            Text(plan, "fixture_sha256").Equals(originalHash, StringComparison.OrdinalIgnoreCase));
        Program.Check("native TimberFold installed bundle matches its pinned manifest", Hash(Path.Combine(bundle, "manifest.json")).Equals(manifestHash, StringComparison.OrdinalIgnoreCase) &&
            Text(launch, "manifest_hash").Equals(manifestHash, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(bundle) == manifestHash.ToLowerInvariant());
        var binding = SessionBinding.Load(session);
        Program.Check("native TimberFold selects exactly the recorded process/start/session", binding.SessionId == sessionId &&
            binding.ProcessId == Property(launch, "pid").GetInt32() && binding.ProcessStart == Text(launch, "start") && binding.RevitVersion == "2027");
        string executable = Path.Combine(bundle, "mcp", "revitthyme-mcp.exe");
        Program.Check("native TimberFold uses the immutable installed MCP executable", Hash(executable).Equals(Text(launch, "mcp_hash"), StringComparison.OrdinalIgnoreCase));
        using (var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(bundle, "manifest.json"))))
            foreach (var entry in Property(manifest.RootElement, "files").EnumerateObject().Where(p => p.Name.StartsWith("mcp/", StringComparison.Ordinal)))
            {
                string path = Path.GetFullPath(Path.Combine(bundle, entry.Name));
                if (!path.StartsWith(bundle + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                    !Hash(path).Equals(entry.Value.GetString(), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Installed MCP payload changed: " + entry.Name);
            }
        string evidence = Path.Combine(folder, "timberfold-mcp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(evidence); Console.WriteLine("Native TimberFold MCP evidence: " + evidence);
        Write(Path.Combine(evidence, "inputs.json"), new { folder, fixtureReceipt, plan, launch, fixtureSha256 = originalHash,
            installedMcpSha256 = Hash(executable), testAssemblySha256 = Hash(typeof(TimberFoldLive).Assembly.Location) });
        var calls = new List<object>(); object callsGate = new();
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(210));
        var transport = new StdioClientTransport(new() { Command = executable, Arguments = ["--session", session, "--sessions-directory", Path.Combine(folder, "sessions")],
            Name = "Pinned native TimberFold", StandardErrorLines = line => { lock (callsGate) File.AppendAllText(Path.Combine(evidence, "mcp-stderr.txt"), line + Environment.NewLine); } });
        Guid runId = Guid.NewGuid(), generationRunId = Guid.NewGuid(), recoveryRunId = Guid.NewGuid();
        try
        {
            await using var client = await McpClient.CreateAsync(new RecordingClientTransport(transport, evidence), cancellationToken: lifetime.Token);
            Program.Check("native installed MCP advertises the 26 current tools", (await client.ListToolsAsync(cancellationToken: lifetime.Token)).Count == 26);
            var before = await Call("get_context"); var baseline = Property(Property(before.Data, "result"), "snapshot").Deserialize<Snapshot>(Core.Protocol.Json)!;
            Program.Check("native MCP capture is the exact disposable fixture and launch document", baseline.Target.ProcessId == binding.ProcessId &&
                baseline.Target.ProcessStart == binding.ProcessStart && baseline.Target.SessionId == sessionId && baseline.DocumentPath == model && !baseline.IsWorkshared &&
                baseline.Target.DocumentId == Text(launch, "document_id"));
            using var previewBudget = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); previewBudget.CancelAfter(TimeSpan.FromSeconds(60));
            var start = await Call("timberfold_preview", new() { ["captureId"] = Text(before.Data, "captureId"), ["requestId"] = runId.ToString() }, previewBudget.Token);
            var summary = Property(start.Data, "timberRun").Deserialize<TimberRunSummary>(Core.Protocol.Json)!;
            while (!summary.Terminal)
            {
                await Task.Delay(100, previewBudget.Token);
                var status = await Call("timberfold_status", new() { ["runId"] = runId.ToString() }, previewBudget.Token);
                summary = Property(status.Data, "timberRun").Deserialize<TimberRunSummary>(Core.Protocol.Json)!;
            }
            var expected = Property(Property(fixture, "sourceMap"), "expected");
            Program.Check("native MCP preview reaches the independently expected shell inventory", summary.RunId == runId && summary.Target == baseline.Target &&
                summary.State == TimberRunState.PreviewReady && summary.PanelCount == Property(expected, "AnalyticShellPanels").GetInt32() && summary.SheetCount > 0 &&
                !summary.ManufacturingReady && summary.NativeImportOutstanding && summary.PhysicalChecksOutstanding);
            using (var snapshot = JsonDocument.Parse(File.ReadAllText(Path.Combine(summary.Directory, "snapshot.json"))))
                Program.Check("native MCP preview retains all expected wall/roof faces and openings", Property(snapshot.RootElement, "faces").GetArrayLength() == Property(expected, "AnalyticShellPanels").GetInt32() &&
                    Property(snapshot.RootElement, "openings").GetArrayLength() == Property(expected, "Windows").GetInt32() + Property(expected, "Openings").GetInt32());
            using (var preview = JsonDocument.Parse(File.ReadAllText(Path.Combine(summary.Directory, "preview.json"))))
            {
                Program.Check("native MCP output is a verified digital preview with outstanding manufacturing checks", Text(preview.RootElement, "state") == "PreviewVerified" &&
                    !Property(preview.RootElement, "manufacturingReady").GetBoolean());
                foreach (var file in Property(preview.RootElement, "cuttingFiles").EnumerateArray())
                    Program.Check("native MCP preview hash matches " + Text(file, "name"), Hash(Path.Combine(summary.Directory, "cutting", Text(file, "name"))) == Text(file, "sha256"));
            }
            var queryContext = await Call("get_context");
            var queryBaseline = Property(Property(queryContext.Data, "result"), "snapshot").Deserialize<Snapshot>(Core.Protocol.Json)!;
            Program.Check("native preview leaves the complete captured source context unchanged", queryBaseline == baseline);
            string marker = Path.Combine(evidence, "native-callback-entered.marker");
            string source = "using (var marker = new System.IO.FileStream(" + JsonSerializer.Serialize(marker) +
                ", System.IO.FileMode.CreateNew, System.IO.FileAccess.Write, System.IO.FileShare.Read)) { marker.WriteByte(1); } " +
                "System.Threading.Thread.Sleep(3000); return new { held = true };";
            string queryId = Guid.NewGuid().ToString();
            var heldQuery = Call("run_query", new() { ["captureId"] = Text(queryContext.Data, "captureId"), ["requestId"] = queryId,
                ["source"] = source, ["trusted"] = true, ["budgetMilliseconds"] = 5000 });
            var enteredBudget = Stopwatch.StartNew();
            while (!File.Exists(marker))
            {
                if (heldQuery.IsCompleted) { await heldQuery; throw new InvalidOperationException("Native query completed without its callback-entry marker."); }
                if (enteredBudget.Elapsed > TimeSpan.FromSeconds(15)) throw new TimeoutException("Native callback did not enter within 15 seconds.");
                await Task.Delay(20, lifetime.Token);
            }
            var responsiveStatus = Call("timberfold_status", new() { ["runId"] = runId.ToString() });
            var responsiveCancel = Call("timberfold_cancel", new() { ["runId"] = runId.ToString() });
            Reply[] controlResults; bool returnedBeforeNative; Reply queryReply;
            try
            {
                controlResults = await Task.WhenAll(responsiveStatus, responsiveCancel);
                returnedBeforeNative = !heldQuery.IsCompleted;
            }
            finally { queryReply = await heldQuery; }
            var after = await Call("get_context"); var final = Property(Property(after.Data, "result"), "snapshot").Deserialize<Snapshot>(Core.Protocol.Json)!;
            Program.Check("native MCP qualification preserves revision, source context and both RVT files", final == baseline &&
                Hash(model) == originalHash.ToLowerInvariant() && Hash(original) == originalHash.ToLowerInvariant());
            var execution = Property(queryReply.Data, "execution"); var changes = Property(execution, "changes");
            Program.Check("held read-only query completed with no reported model changes", Property(execution, "value").GetProperty("held").GetBoolean() &&
                (changes.ValueKind == JsonValueKind.Null || new[] { "added", "modified", "deleted" }.All(name => Property(changes, name).GetArrayLength() == 0)));
            Program.Check("native status/cancel bypass a real pending API callback within one second", returnedBeforeNative &&
                controlResults.All(r => r.Milliseconds < 1000 && Property(r.Data, "timberRun").Deserialize<TimberRunSummary>(Core.Protocol.Json) is
                    { State: TimberRunState.PreviewReady } completed && completed.RunId == runId));
            var verifiedPreview = await Call("timberfold_verify", new() { ["runId"] = runId.ToString() });
            Program.Check("native MCP verifies retained preview evidence without modifying the source", Property(verifiedPreview.Data, "timberRun")
                .Deserialize<TimberRunSummary>(Core.Protocol.Json) is { State: TimberRunState.PreviewReady } checkedPreview && checkedPreview.RunId == runId);
            using var generationBudget = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); generationBudget.CancelAfter(TimeSpan.FromSeconds(90));
            var generated = await Call("timberfold_generate", new() { ["captureId"] = Text(after.Data, "captureId"),
                ["requestId"] = generationRunId.ToString(), ["previewRunId"] = runId.ToString() }, generationBudget.Token);
            var kit = Property(generated.Data, "timberRun").Deserialize<TimberRunSummary>(Core.Protocol.Json)!;
            while (!kit.Terminal)
            {
                await Task.Delay(100, generationBudget.Token);
                kit = Property((await Call("timberfold_status", new() { ["runId"] = generationRunId.ToString() }, generationBudget.Token)).Data,
                    "timberRun").Deserialize<TimberRunSummary>(Core.Protocol.Json)!;
            }
            Program.Check("native MCP generation commits a verified kit with the exact preview inventory", kit is
                { State: TimberRunState.KitReady, NativeCommitted: true, NativeImportOutstanding: false, PhysicalChecksOutstanding: true, ManufacturingReady: false } &&
                kit.RunId == generationRunId && kit.PreviewRunId == runId && kit.PanelCount == summary.PanelCount && kit.SheetCount == summary.SheetCount);
            CheckKitEvidence(kit);
            var verifiedKit = await Call("timberfold_verify", new() { ["runId"] = generationRunId.ToString() });
            Program.Check("native MCP verification rechecks the complete generated kit", Property(verifiedKit.Data, "timberRun")
                .Deserialize<TimberRunSummary>(Core.Protocol.Json) is { State: TimberRunState.KitReady, NativeCommitted: true } checkedKit && checkedKit.RunId == generationRunId);
            var afterGeneration = Property(Property((await Call("get_context")).Data, "result"), "snapshot").Deserialize<Snapshot>(Core.Protocol.Json)!;
            Program.Check("native kit leaves the same fixture open and both source RVTs unsaved", afterGeneration.Target.DocumentId == baseline.Target.DocumentId &&
                afterGeneration.Target.ViewId == baseline.Target.ViewId && afterGeneration.DocumentPath == model && afterGeneration.IsModified &&
                Hash(model) == originalHash.ToLowerInvariant() && Hash(original) == originalHash.ToLowerInvariant());
            var reconciliation = await TimberFoldReconciliationLive.Run(kit, recoveryRunId, afterGeneration, original, originalHash,
                async (tool, args, token) => (await Call(tool, args, token)).Data, lifetime.Token);
            Write(Path.Combine(evidence, "complete.json"), new { passed = true, runId, generationRunId, recoveryRunId, summary, kit, baseline, final, afterGeneration, reconciliation,
                statusMilliseconds = controlResults[0].Milliseconds, cancelMilliseconds = controlResults[1].Milliseconds, returnedBeforeNative,
                physicalChecksOutstanding = true, nativeDxfImportQualifiedByThisHarness = false, calls });

            async Task<Reply> Call(string tool, Dictionary<string, object?>? args = null, CancellationToken token = default)
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, token); timeout.CancelAfter(TimeSpan.FromSeconds(15));
                var watch = Stopwatch.StartNew();
                var result = await client.CallToolAsync(tool, args ?? [], cancellationToken: timeout.Token);
                double milliseconds = watch.Elapsed.TotalMilliseconds;
                string text = result.Content.OfType<TextContentBlock>().Single().Text;
                using var json = JsonDocument.Parse(text); var data = json.RootElement.Clone();
                lock (callsGate) calls.Add(new { tool, args, milliseconds, error = result.IsError, text, structured = result.StructuredContent });
                if (result.IsError == true) throw new InvalidOperationException(tool + " failed: " + text);
                return new(data, milliseconds);
            }
        }
        catch (Exception error)
        { Write(Path.Combine(evidence, "failed.json"), new { passed = false, runId, generationRunId, recoveryRunId, error = error.ToString(), calls }); throw; }
    }

    private static void CheckKitEvidence(TimberRunSummary kit)
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(kit.Directory, "kit.json")));
        Program.Check("native MCP kit manifest retains digital-only acceptance and native readback", Text(manifest.RootElement, "state") == "DigitalKitVerified" &&
            !Property(manifest.RootElement, "manufacturingReady").GetBoolean() && Property(manifest.RootElement, "physicalChecksOutstanding").GetBoolean() &&
            Property(manifest.RootElement, "nativeReadbackPassed").GetBoolean());
        foreach (var file in Property(manifest.RootElement, "files").EnumerateArray())
        {
            string path = Path.GetFullPath(Path.Combine(kit.Directory, Text(file, "name")));
            Program.Check("native MCP generated file hash matches " + Text(file, "name"), path.StartsWith(kit.Directory + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase) && new FileInfo(path).Length == Property(file, "bytes").GetInt64() && Hash(path) == Text(file, "sha256"));
        }
        var receipt = JsonSerializer.Deserialize<TimberNativeKitReceipt>(File.ReadAllText(Path.Combine(kit.Directory, "native-receipt.json")), Core.Protocol.Json)!;
        Program.Check("native MCP receipt records committed geometry, readback and actual DWG files", receipt is
            { State: TimberNativeKitState.Committed, SourcePreserved: true, NativeReadbackPassed: true } && receipt.RunId == kit.RunId &&
            receipt.CreatedElements.Length > 0 && receipt.Files.Any(f => f.Role == "CutDwg") && receipt.Files.Any(f => f.Role == "CalibrationDwg"));
    }

    private sealed record Reply(JsonElement Data, double Milliseconds);
    private static JsonElement Property(JsonElement value, string name) => value.EnumerateObject().Single(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)).Value;
    private static string Text(JsonElement value, string name) => Property(value, name).GetString() ?? throw new InvalidDataException("Missing " + name);
    private static string Hash(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
    private static void Write(string path, object value)
    { using var stream = new FileStream(path, FileMode.CreateNew); JsonSerializer.Serialize(stream, value, Core.Protocol.Json); }

    // Official SDK client/stdio transport remains the executor. This decorator
    // records actual JSON-RPC envelopes and IDs in both directions, not mock calls.
    private sealed class RecordingClientTransport(IClientTransport inner, string directory) : IClientTransport
    {
        public string Name => inner.Name;
        public async Task<ITransport> ConnectAsync(CancellationToken cancellationToken = default) =>
            new RecordingTransport(await inner.ConnectAsync(cancellationToken), directory);
    }
    private sealed class RecordingTransport : ITransport
    {
        private readonly ITransport inner;
        private readonly string directory;
        private readonly Channel<JsonRpcMessage> incoming = Channel.CreateUnbounded<JsonRpcMessage>();
        private readonly CancellationTokenSource stopping = new();
        private readonly Task pump;
        private int serial;
        public RecordingTransport(ITransport inner, string directory)
        { this.inner = inner; this.directory = directory; pump = Pump(); }
        public string? SessionId => inner.SessionId;
        public ChannelReader<JsonRpcMessage> MessageReader => incoming.Reader;
        public async Task SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken = default)
        { Record("out", message); await inner.SendMessageAsync(message, cancellationToken); }
        private async Task Pump()
        {
            try
            {
                await foreach (var message in inner.MessageReader.ReadAllAsync(stopping.Token))
                { Record("in", message); await incoming.Writer.WriteAsync(message, stopping.Token); }
                incoming.Writer.TryComplete();
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested) { incoming.Writer.TryComplete(); }
            catch (Exception error) { incoming.Writer.TryComplete(error); }
        }
        private void Record(string direction, JsonRpcMessage message)
        {
            string name = FormattableString.Invariant($"rpc-{Interlocked.Increment(ref serial):0000}-{direction}.json");
            using var stream = new FileStream(Path.Combine(directory, name), FileMode.CreateNew);
            JsonSerializer.Serialize(stream, message, McpJsonUtilities.DefaultOptions);
        }
        public async ValueTask DisposeAsync()
        { stopping.Cancel(); await inner.DisposeAsync(); await pump; stopping.Dispose(); }
    }
}
