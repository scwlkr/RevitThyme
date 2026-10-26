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
            foreach (var command in new[] { "status", "cancel", "verify" })
            {
                client.Requests.Clear(); output.GetStringBuilder().Clear();
                exit = await RevitThyme.Cli.Program.RunAsync(["timberfold", command, "--run-id", runId.ToString(), "--session", session, "--json"], output, error, _ => client);
                var reply = JsonSerializer.Deserialize<OperationResult>(output.ToString(), Protocol.Json)!;
                check("CLI " + command + " uses the retained run without inspection or a capture", exit == 0 &&
                    client.Requests is [{ Target: null, TimberFold: null } query] && query.OperationId == runId && query.Id != runId &&
                    query.Operation == (command == "status" ? OperationKind.GetTimberFoldRun : command == "cancel" ?
                        OperationKind.CancelTimberFoldRun : OperationKind.VerifyTimberFoldRun) &&
                    query.IsWrite == (command == "cancel") && reply.TimberRun?.RunId == runId && !reply.TimberRun.ManufacturingReady);
            }
            client.Requests.Clear(); output.GetStringBuilder().Clear();
            var generationId = Guid.Parse("d634def7-1932-47f0-8b70-c7c20146bd68");
            try { exit = await RevitThyme.Cli.Program.RunAsync(["timberfold", "generate", "--preview-run-id", runId.ToString(),
                "--run-id", generationId.ToString(), "--session", session, "--json"], output, error, _ => client); }
            catch (ArgumentException) { exit = 2; }
            check("CLI generation uses the retained preview target and distinct caller run ID without resending settings", exit == 0 &&
                client.Requests is [{ Operation: OperationKind.GetTimberFoldRun, OperationId: var parent }, var generate] &&
                parent == runId && generate.Operation == OperationKind.GenerateTimberFold && generate.Id == generationId &&
                generate.OperationId == runId && generate.Target == client.Snapshot.Target && generate.TimberFold is null && generate.IsWrite);
            client.Requests.Clear(); output.GetStringBuilder().Clear();
            var recoveryId = Guid.Parse("c3872dc6-447f-41de-bad4-d1325a4864a3");
            try { exit = await RevitThyme.Cli.Program.RunAsync(["timberfold", "reconcile", "--generation-run-id", generationId.ToString(),
                "--run-id", recoveryId.ToString(), "--session", session, "--json"], output, error, _ => client); }
            catch (ArgumentException) { exit = 2; }
            check("CLI reconciliation captures the current target and keeps a distinct recovery and generation ID", exit == 0 &&
                client.Requests is [{ Operation: OperationKind.Inspect }, var reconcile] && reconcile.Operation.ToString() == "ReconcileTimberFold" &&
                reconcile.Id == recoveryId && reconcile.OperationId == generationId && reconcile.Target == client.Snapshot.Target &&
                reconcile.TimberFold is null && reconcile.IsWrite);
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
                    new[] { "cancel" }, new[] { "status", "--run-id", runId.ToString(), "--profile", profileFile },
                    new[] { "generate", "--run-id", runId.ToString() },
                    new[] { "generate", "--run-id", runId.ToString(), "--preview-run-id", Guid.Empty.ToString() },
                    new[] { "generate", "--run-id", runId.ToString(), "--preview-run-id", "bad" },
                    new[] { "reconcile", "--run-id", runId.ToString(), "--generation-run-id", Guid.Empty.ToString() },
                    new[] { "reconcile", "--run-id", runId.ToString(), "--generation-run-id", "bad" },
                    new[] { "generate", "--run-id", runId.ToString(), "--preview-run-id", runId.ToString() },
                    new[] { "reconcile", "--run-id", runId.ToString() },
                    new[] { "reconcile", "--run-id", runId.ToString(), "--generation-run-id", runId.ToString() },
                    new[] { "reconcile", "--run-id", runId.ToString(), "--generation-run-id", generationId.ToString(), "--profile", profileFile },
                    new[] { "verify", "--run-id", runId.ToString(), "--scope", scopeFile } })
                {
                    int calls = client.Requests.Count; bool rejected = false;
                    try { await RevitThyme.Cli.Program.RunAsync(["timberfold", .. invalid, "--session", session], output, error, _ => client); }
                    catch (ArgumentException) { rejected = true; }
                    check("CLI rejects invalid run options before session work: " + string.Join(' ', invalid.Take(3)), rejected && client.Requests.Count == calls);
                }
            }
            finally { File.Delete(profileFile); File.Delete(scopeFile); }
            foreach (var (arguments, message) in new[]
            {
                (new[] { "timberfold", "inspect" }, "Required: --session"),
                (new[] { "timberfold", "preview", "--run-id", runId.ToString(), "--session", session, "--bogus", "bad" }, "Unknown TimberFold preview option.")
            })
            {
                int factories = 0; string? observed = null;
                try { await RevitThyme.Cli.Program.RunAsync(arguments, new StringWriter(), new StringWriter(), _ => { factories++; return client; }); }
                catch (ArgumentException exception) { observed = exception.Message; }
                check("CLI rejection preserves the command-specific diagnostic", observed == message && factories == 0);
            }
            await ReplyBoundaries(client, session, runId, generationId, check);
            await HumanRun(client, session, runId, check);
            await SettingsBoundaries(client, session, runId, Path.Combine(directory, "settings.json"), check);
            client.Requests.Clear(); client.ContextFails = true;
            exit = await RevitThyme.Cli.Program.RunAsync(["timberfold", "preview", "--run-id", runId.ToString(), "--session", session], output, error, _ => client);
            check("CLI failed native context never submits a preview", exit == 1 && client.Requests is [{ Operation: OperationKind.Inspect }]);
        }
        finally { File.Delete(session); Directory.Delete(directory); }
    }

    private static async Task ReplyBoundaries(RecordingClient client, string session, Guid previewId, Guid generationId, Action<string, bool> check)
    {
        try
        {
            foreach (var parent in new[]
            {
                new OperationResult(Guid.Empty, Outcome.Rejected, "missing_preview", "Preview not found."),
                new(Guid.Empty, Outcome.Inspected, "empty_preview", "Preview has no retained summary.")
            })
            {
                client.ParentReply = parent; client.Requests.Clear();
                var output = new StringWriter(); var error = new StringWriter();
                int exit = await RevitThyme.Cli.Program.RunAsync(["timberfold", "generate", "--run-id", generationId.ToString(),
                    "--preview-run-id", previewId.ToString(), "--session", session, "--json"], output, error, _ => client);
                var reply = JsonSerializer.Deserialize<OperationResult>(output.ToString(), Protocol.Json);
                check("CLI relays incomplete parent replies without submitting generation: " + parent.Code,
                    exit == (parent.Succeeded ? 0 : 1) && client.Requests is [{ Operation: OperationKind.GetTimberFoldRun } request]
                    && reply?.Id == request.Id && reply.Code == parent.Code && reply.Message == parent.Message
                    && error.ToString() == (parent.Succeeded ? "" : parent.Message + Environment.NewLine));
            }
            client.ParentReply = null;
            client.ContextReply = new(Guid.Empty, Outcome.Inspected, "empty_context", "Inspection has no snapshot.");
            client.Requests.Clear();
            var contextOutput = new StringWriter(); var contextError = new StringWriter();
            int contextExit = await RevitThyme.Cli.Program.RunAsync(["timberfold", "preview", "--run-id", previewId.ToString(),
                "--session", session, "--json"], contextOutput, contextError, _ => client);
            var context = JsonSerializer.Deserialize<OperationResult>(contextOutput.ToString(), Protocol.Json);
            check("CLI relays a successful context without a snapshot without submitting work", contextExit == 0 &&
                client.Requests is [{ Operation: OperationKind.Inspect } inspected] && context?.Id == inspected.Id
                && context.Code == "empty_context" && contextError.ToString() == "");
        }
        finally { client.ParentReply = null; client.ContextReply = null; }
    }

    private static async Task HumanRun(RecordingClient client, string session, Guid runId, Action<string, bool> check)
    {
        client.RunReply = new(runId, client.Snapshot.Target, TimberRunState.KitReady, "KitReady", "private-ready", "fingerprint", 3, 2, 0,
            [new("fixture_notice", TimberSeverity.Warning, "Check fixture.")], NativeCommitted: true);
        try
        {
            var output = new StringWriter(); var error = new StringWriter();
            int exit = await RevitThyme.Cli.Program.RunAsync(["timberfold", "status", "--run-id", runId.ToString(), "--session", session], output, error, _ => client);
            string expected = string.Join(Environment.NewLine, new[] { "Preview accepted.", $"Run {runId}: KitReady, KitReady; 3 panels, 2 sheets.",
                "Private run files: private-ready", "Warning fixture_notice: Check fixture.",
                "Manufacturing readiness: false; native import verified; physical checks outstanding.", "" });
            check("CLI human run output retains diagnostics, private paths and readiness text", exit == 0 && output.ToString() == expected && error.ToString() == "");
        }
        finally { client.RunReply = null; }
    }

    private static async Task SettingsBoundaries(RecordingClient client, string session, Guid runId, string path, Action<string, bool> check)
    {
        try
        {
            File.WriteAllText(path, "{}".PadRight(16384)); client.Requests.Clear();
            int exit = await RevitThyme.Cli.Program.RunAsync(["timberfold", "preview", "--run-id", runId.ToString(), "--session", session,
                "--profile", path], new StringWriter(), new StringWriter(), _ => client);
            check("CLI accepts an exact 16 KiB settings file", exit == 0 && client.Requests.Count == 2);
            foreach (string text in new[] { "null", "{", "{}".PadRight(16385) })
            {
                File.WriteAllText(path, text); int factories = 0; bool rejected = false;
                try { await RevitThyme.Cli.Program.RunAsync(["timberfold", "preview", "--run-id", runId.ToString(), "--session", session,
                    "--profile", path], new StringWriter(), new StringWriter(), _ => { factories++; return client; }); }
                catch (Exception error) when (error is ArgumentException or JsonException) { rejected = true; }
                check("CLI rejects null, malformed and oversized settings before client creation", rejected && factories == 0);
            }
        }
        finally { File.Delete(path); }
    }

    private sealed class RecordingClient(SessionBinding binding) : IOperationClient
    {
        public List<OperationRequest> Requests { get; } = [];
        public bool ContextFails { get; set; }
        public OperationResult? ContextReply { get; set; }
        public OperationResult? ParentReply { get; set; }
        public TimberRunSummary? RunReply { get; set; }
        public Snapshot Snapshot { get; } = new(new(binding.ProcessId, binding.ProcessStart, binding.SessionId, "fixture", "view", 1),
            "2027", "Fixture", "", false, false, "Review", "FloorPlan", 1, true, null);
        public Task<OperationResult> ExecuteAsync(OperationRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            if (request.Operation == OperationKind.Inspect && ContextReply is { } context) return Task.FromResult(context with { Id = request.Id });
            if (request.Operation == OperationKind.GetTimberFoldRun && ParentReply is { } parent) return Task.FromResult(parent with { Id = request.Id });
            if (request.Operation == OperationKind.Inspect && ContextFails)
                return Task.FromResult(OperationResult.Reject(request, "no_document", "No document."));
            return Task.FromResult(new OperationResult(request.Id, Outcome.Inspected, "accepted", "Preview accepted.",
                Snapshot: request.Operation == OperationKind.Inspect ? Snapshot : null,
                TimberRun: request.Operation == OperationKind.Inspect ? null : RunReply ?? new(
                    Guid.Parse("7abf1df1-d90c-4fb1-869b-55c5b8bd0614"), Snapshot.Target, TimberRunState.Accepted, "Accepted", "private-run",
                    null, 0, 0, 0, [])));
        }
    }
}
