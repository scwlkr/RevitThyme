using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using RevitThyme.Core;
using RevitThyme.Mcp;

namespace RevitThyme.McpTests;

internal static class TimberFoldRuns
{
    public static async Task Run()
    {
        string directory = Path.Combine(Path.GetTempPath(), "RevitThyme-mcp-runs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string file = Path.Combine(directory, "session.json");
        using var process = Process.GetCurrentProcess(); string sessionId = Guid.NewGuid().ToString();
        var binding = new SessionBinding(Core.Protocol.Version, process.Id,
            process.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture), sessionId,
            $"RevitThyme-Foundation-{process.Id}-{sessionId}", "2027");
        File.WriteAllText(file, JsonSerializer.Serialize(binding, Core.Protocol.Json));
        try
        {
            var client = new RecordingClient(binding);
            var connection = new RevitConnection(new(directory, file), _ => client);
            var tools = new RevitTools(connection);
            string runId = "25be6789-1f82-4c31-aaaf-245df5cd31fe";
            var status = await tools.TimberFoldStatus(runId);
            Program.Check("MCP TimberFold status works without a capture or native inspection", status.IsError != true &&
                client.Requests is [{ Operation: OperationKind.GetTimberFoldRun, Target: null, TimberFold: null, IsWrite: false } query] &&
                query.OperationId == Guid.Parse(runId) && query.Id != query.OperationId && Data(status).GetProperty("timberRun").GetProperty("runId").GetGuid() == Guid.Parse(runId));
            client.Requests.Clear();
            var cancel = await tools.TimberFoldCancel(runId);
            Program.Check("MCP TimberFold cancellation directly addresses the retained run", cancel.IsError != true &&
                client.Requests is [{ Operation: OperationKind.CancelTimberFoldRun, Target: null, TimberFold: null, IsWrite: true } cancellation] &&
                cancellation.OperationId == Guid.Parse(runId));
            client.Requests.Clear();
            var missingCapture = await tools.TimberFoldPreview("missing", runId);
            Program.Check("MCP preview requires an explicit captured target", missingCapture.IsError == true &&
                Data(missingCapture).GetProperty("code").GetString() == "capture_missing" && client.Requests.Count == 0);
            var context = await connection.Context(CancellationToken.None); client.Requests.Clear();
            var profile = new TimberFoldProfile(ScaleDenominator: 72, StockWidthInches: 30);
            var scope = new TimberScope("phase-2", ["option-1"]);
            var preview = await tools.TimberFoldPreview(context.CaptureId, runId, profile, scope);
            var body = Data(preview);
            Program.Check("MCP preview forwards exact run ID, capture, settings and structured retained summary", preview.IsError != true &&
                client.Requests is [{ Operation: OperationKind.PreviewTimberFold, IsWrite: true } start] && start.Id == Guid.Parse(runId) &&
                start.Target == client.Snapshot.Target && start.TimberFold?.Profile == profile && start.TimberFold.Scope == scope &&
                body.GetProperty("timberRun").GetProperty("nativeImportOutstanding").GetBoolean() &&
                !body.GetProperty("timberRun").GetProperty("manufacturingReady").GetBoolean() &&
                preview.StructuredContent?.GetRawText() == preview.Content.OfType<TextContentBlock>().Single().Text);
            client.Requests.Clear();
            var invalid = new[] { await tools.TimberFoldStatus("bad"), await tools.TimberFoldCancel(Guid.Empty.ToString()),
                await tools.TimberFoldPreview(context.CaptureId, "bad") };
            Program.Check("MCP invalid run IDs fail before any host request", invalid.All(r => r.IsError == true &&
                Data(r).GetProperty("code").GetString() == "invalid_id") && client.Requests.Count == 0);
            using var cancellationSource = new CancellationTokenSource(); cancellationSource.Cancel();
            var cancelled = await tools.TimberFoldStatus(runId, cancellationSource.Token);
            Program.Check("MCP cancelled status does not reach the host", cancelled.IsError == true &&
                Data(cancelled).GetProperty("code").GetString() == "cancelled" && client.Requests.Count == 0);
            var disconnected = new RevitTools(new(new(directory, null), _ => client));
            var notConnected = await disconnected.TimberFoldStatus(runId);
            Program.Check("MCP retained-run lookup preserves explicit connection requirements", notConnected.IsError == true &&
                Data(notConnected).GetProperty("code").GetString() == "not_connected");
            var heldClient = new RecordingClient(binding) { HoldContext = true };
            var heldConnection = new RevitConnection(new(directory, file), _ => heldClient);
            var heldTools = new RevitTools(heldConnection);
            var pendingContext = heldConnection.Context(CancellationToken.None);
            await heldClient.ContextStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
            var directStatus = heldTools.TimberFoldStatus(runId);
            var directCancel = heldTools.TimberFoldCancel(runId);
            bool controlsFinishedBeforeNative = false;
            try
            {
                await Task.WhenAll(directStatus, directCancel).WaitAsync(TimeSpan.FromSeconds(1));
                controlsFinishedBeforeNative = true;
            }
            catch (TimeoutException) { }
            finally { heldClient.ReleaseContext(); }
            await pendingContext; await Task.WhenAll(directStatus, directCancel);
            Program.Check("MCP status and cancellation complete while native context is still pending", controlsFinishedBeforeNative &&
                directStatus.Result.IsError != true && directCancel.Result.IsError != true && heldClient.Requests is
                [{ Operation: OperationKind.Inspect }, { Operation: OperationKind.GetTimberFoldRun }, { Operation: OperationKind.CancelTimberFoldRun }]);
            string secondFile = Path.Combine(directory, "second-session.json"), secondId = Guid.NewGuid().ToString();
            var secondBinding = binding with { SessionId = secondId, PipeName = $"RevitThyme-Foundation-{process.Id}-{secondId}" };
            File.WriteAllText(secondFile, JsonSerializer.Serialize(secondBinding, Core.Protocol.Json));
            try
            {
                var firstClient = new RecordingClient(binding) { HoldControl = true };
                var secondClient = new RecordingClient(secondBinding);
                var switchable = new RevitConnection(new(directory, null), b => b.SessionId == binding.SessionId ? firstClient : secondClient);
                await switchable.Connect(file, CancellationToken.None);
                var switchableTools = new RevitTools(switchable);
                var firstLookup = switchableTools.TimberFoldStatus(runId);
                await firstClient.ControlStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
                var reconnect = switchable.Connect(secondFile, CancellationToken.None);
                bool switchedBeforeControlFinished = false;
                try { await reconnect.WaitAsync(TimeSpan.FromSeconds(1)); switchedBeforeControlFinished = true; }
                catch (TimeoutException) { }
                finally { firstClient.ReleaseControl(); }
                var firstReply = await firstLookup; await reconnect;
                var secondReply = await switchableTools.TimberFoldStatus(runId);
                Program.Check("MCP control keeps its chosen session when another connection completes mid-call", switchedBeforeControlFinished &&
                    Data(firstReply).GetProperty("timberRun").GetProperty("target").GetProperty("sessionId").GetString() == binding.SessionId &&
                    Data(secondReply).GetProperty("timberRun").GetProperty("target").GetProperty("sessionId").GetString() == secondId &&
                    firstClient.Requests.Count(r => r.Operation == OperationKind.GetTimberFoldRun) == 1 &&
                    secondClient.Requests.Count(r => r.Operation == OperationKind.GetTimberFoldRun) == 1);
            }
            finally { File.Delete(secondFile); }
        }
        finally { File.Delete(file); Directory.Delete(directory); }
    }

    private static JsonElement Data(CallToolResult result)
    {
        using var document = JsonDocument.Parse(result.Content.OfType<TextContentBlock>().Single().Text);
        return document.RootElement.Clone();
    }

    private sealed class RecordingClient(SessionBinding binding) : IOperationClient
    {
        public List<OperationRequest> Requests { get; } = [];
        public bool HoldContext { get; init; }
        public bool HoldControl { get; init; }
        public TaskCompletionSource ContextStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ControlStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<OperationResult> heldContext = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<OperationResult> heldControl = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Guid contextRequestId;
        private OperationRequest? controlRequest;
        public Snapshot Snapshot { get; } = new(new(binding.ProcessId, binding.ProcessStart, binding.SessionId, "fixture", "view", 1),
            "2027", "Fixture", "", false, false, "Review", "FloorPlan", 1, true, null);
        public Task<OperationResult> ExecuteAsync(OperationRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            if (request.Operation == OperationKind.Inspect && HoldContext)
            { contextRequestId = request.Id; ContextStarted.TrySetResult(); return heldContext.Task; }
            if (request.Operation == OperationKind.GetTimberFoldRun && HoldControl)
            { controlRequest = request; ControlStarted.TrySetResult(); return heldControl.Task; }
            return Task.FromResult(Reply(request));
        }
        private OperationResult Reply(OperationRequest request) => new(request.Id, Outcome.Inspected, "accepted", "Preview accepted.",
                Snapshot: request.Operation == OperationKind.Inspect ? Snapshot : null,
                TimberRun: request.Operation == OperationKind.Inspect ? null : new(
                    Guid.Parse("25be6789-1f82-4c31-aaaf-245df5cd31fe"), Snapshot.Target, TimberRunState.Accepted, "Accepted", "private-run", null, 0, 0, 0, []));
        public void ReleaseContext() => heldContext.TrySetResult(new(contextRequestId, Outcome.Inspected, "inspected", "Context captured.", Snapshot: Snapshot));
        public void ReleaseControl() => heldControl.TrySetResult(Reply(controlRequest ?? throw new InvalidOperationException("Control has not started.")));
    }
}
