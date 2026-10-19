using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using RevitThyme.Core;

namespace RevitThyme.Unit;

internal static class TimberFoldCliRuns
{
    public static async Task Run(Action<string, bool> check)
    {
        string directory = Path.Combine(Path.GetTempPath(), "RevitThyme-cli-runs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        using var process = Process.GetCurrentProcess();
        string sessionId = Guid.NewGuid().ToString();
        var binding = new SessionBinding(Protocol.Version, process.Id, process.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture),
            sessionId, $"RevitThyme-Foundation-{process.Id}-{sessionId}", "2027");
        string session = Path.Combine(directory, "session.json");
        File.WriteAllText(session, JsonSerializer.Serialize(binding, Protocol.Json));
        try
        {
            var client = new RecordingClient(binding);
            var output = new StringWriter(); var error = new StringWriter();
            var runId = Guid.Parse("7abf1df1-d90c-4fb1-869b-55c5b8bd0614");
            int exit;
            try { exit = await RevitThyme.Cli.Program.RunAsync(["timberfold", "preview", "--run-id", runId.ToString(), "--session", session, "--json"], output, error, _ => client); }
            catch (ArgumentException) { exit = 2; }
            check("CLI preview captures a fresh target and sends the caller run ID through the shared operation", exit == 0 &&
                client.Requests is [{ Operation: OperationKind.Inspect }, var preview] &&
                preview.Operation == OperationKind.PreviewTimberFold && preview.Id == runId && preview.Target == client.Snapshot.Target &&
                preview.TimberFold is not null && preview.OperationId is null && preview.IsWrite);
            foreach (var command in new[] { "status", "cancel" })
            {
                client.Requests.Clear(); output.GetStringBuilder().Clear();
                exit = await RevitThyme.Cli.Program.RunAsync(["timberfold", command, "--run-id", runId.ToString(), "--session", session, "--json"], output, error, _ => client);
                var reply = JsonSerializer.Deserialize<OperationResult>(output.ToString(), Protocol.Json)!;
                check("CLI " + command + " uses the retained run without inspection or a capture", exit == 0 &&
                    client.Requests is [{ Target: null, TimberFold: null } query] && query.OperationId == runId && query.Id != runId &&
                    query.Operation == (command == "status" ? OperationKind.GetTimberFoldRun : OperationKind.CancelTimberFoldRun) &&
                    query.IsWrite == (command == "cancel") && reply.TimberRun?.RunId == runId && !reply.TimberRun.ManufacturingReady);
            }
            string profileFile = Path.Combine(directory, "profile.json"), scopeFile = Path.Combine(directory, "scope.json");
            try
            {
                File.WriteAllText(profileFile, "{\"scaleDenominator\":72,\"stockWidthInches\":30}");
                File.WriteAllText(scopeFile, "{\"phaseId\":\"phase-2\",\"designOptionIds\":[\"option-1\"]}");
                client.Requests.Clear(); output.GetStringBuilder().Clear();
                exit = await RevitThyme.Cli.Program.RunAsync(["timberfold", "preview", "--run-id", runId.ToString(), "--session", session,
                    "--profile", profileFile, "--scope", scopeFile], output, error, _ => client);
                check("CLI preview preserves explicit profile and scope and describes retained preview readiness", exit == 0 &&
                    client.Requests[1].TimberFold is { Profile: { ScaleDenominator: 72, StockWidthInches: 30 }, Scope: { PhaseId: "phase-2", DesignOptionIds: ["option-1"] } } &&
                    output.ToString().Contains(runId.ToString(), StringComparison.Ordinal) && output.ToString().Contains("Manufacturing readiness: false", StringComparison.Ordinal));
                foreach (var invalid in new[] {
                    new[] { "preview", "--run-id", "bad" }, new[] { "status", "--run-id", Guid.Empty.ToString() },
                    new[] { "cancel" }, new[] { "status", "--run-id", runId.ToString(), "--profile", profileFile } })
                {
                    int calls = client.Requests.Count; bool rejected = false;
                    try { await RevitThyme.Cli.Program.RunAsync(["timberfold", .. invalid, "--session", session], output, error, _ => client); }
                    catch (ArgumentException) { rejected = true; }
                    check("CLI rejects invalid run options before session work: " + string.Join(' ', invalid.Take(3)), rejected && client.Requests.Count == calls);
                }
            }
            finally { File.Delete(profileFile); File.Delete(scopeFile); }
            client.Requests.Clear(); client.ContextFails = true;
            exit = await RevitThyme.Cli.Program.RunAsync(["timberfold", "preview", "--run-id", runId.ToString(), "--session", session], output, error, _ => client);
            check("CLI failed native context never submits a preview", exit == 1 && client.Requests is [{ Operation: OperationKind.Inspect }]);
        }
        finally { File.Delete(session); Directory.Delete(directory); }
    }

    private sealed class RecordingClient(SessionBinding binding) : IOperationClient
    {
        public List<OperationRequest> Requests { get; } = [];
        public bool ContextFails { get; set; }
        public Snapshot Snapshot { get; } = new(new(binding.ProcessId, binding.ProcessStart, binding.SessionId, "fixture", "view", 1),
            "2027", "Fixture", "", false, false, "Review", "FloorPlan", 1, true, null);
        public Task<OperationResult> ExecuteAsync(OperationRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            if (request.Operation == OperationKind.Inspect && ContextFails)
                return Task.FromResult(OperationResult.Reject(request, "no_document", "No document."));
            return Task.FromResult(new OperationResult(request.Id, Outcome.Inspected, "accepted", "Preview accepted.",
                Snapshot: request.Operation == OperationKind.Inspect ? Snapshot : null,
                TimberRun: request.Operation == OperationKind.Inspect ? null : new(
                    Guid.Parse("7abf1df1-d90c-4fb1-869b-55c5b8bd0614"), Snapshot.Target, TimberRunState.Accepted, "Accepted", "private-run",
                    null, 0, 0, 0, [])));
        }
    }
}
