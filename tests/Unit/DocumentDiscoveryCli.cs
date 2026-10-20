using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using RevitThyme.Core;

namespace RevitThyme.Unit;

internal static class DocumentDiscoveryCli
{
    public static async Task Run(Action<string, bool> check)
    {
        string folder = Path.Combine(Path.GetTempPath(), "RevitThyme-DocumentCli-" + Guid.NewGuid());
        Directory.CreateDirectory(folder);
        string session = Path.Combine(folder, "session.json"), capture = Path.Combine(folder, "page.json");
        try
        {
            using var process = Process.GetCurrentProcess(); string sessionId = Guid.NewGuid().ToString();
            var binding = new SessionBinding(Protocol.Version, process.Id,
                process.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture), sessionId,
                $"RevitThyme-Foundation-{process.Id}-{sessionId}", "2027");
            File.WriteAllText(session, JsonSerializer.Serialize(binding, Protocol.Json));
            var client = new RecordingClient(binding); var output = new StringWriter(); var error = new StringWriter();
            string[] Arguments(params string[] extra) => ["list-documents", "--session", session, "--json", .. extra];
            void Reset() { client.Requests.Clear(); output.GetStringBuilder().Clear(); error.GetStringBuilder().Clear(); }
            void Capture(OperationResult result) => File.WriteAllText(capture, JsonSerializer.Serialize(result, Protocol.Json));
            async Task<int> Run(params string[] extra) => await Cli.Program.RunAsync(Arguments(extra), output, error, _ => client);

            Guid requestId = Guid.NewGuid();
            int exit = await Run("--request-id", requestId.ToString());
            var first = JsonSerializer.Deserialize<OperationResult>(output.ToString(), Protocol.Json)!;
            check("CLI document discovery captures Home before the shared read operation", exit == 0
                && client.Requests is [{ Operation: OperationKind.Inspect }, { Operation: OperationKind.ListDocuments }]
                && client.Requests[1].Target == client.Snapshot.Target && client.Requests[1].Id == requestId
                && client.Requests[1].DocumentQuery == new DocumentQuery() && client.Requests.All(r => !r.IsWrite));
            check("CLI document JSON preserves opaque identity, unsaved paths and native flags", first.Documents is { Total: 2, NextOffset: 1 }
                && first.Documents.InventoryToken == RecordingClient.Token && first.Documents.Items.SequenceEqual(client.Documents)
                && first.Snapshot is { HasDocument: false });
            Capture(first); Reset();
            exit = await Run("--capture", capture, "--offset", "1", "--limit", "1");
            check("CLI document continuation forwards the exact capture target and inventory token without refreshing", exit == 0
                && client.Requests is [{ Operation: OperationKind.ListDocuments, DocumentQuery: { Offset: 1, Limit: 1 } }]
                && client.Requests[0].Target == first.Snapshot!.Target && client.Requests[0].Id != Guid.Empty
                && client.Requests[0].DocumentQuery!.InventoryToken == first.Documents!.InventoryToken);

            Reset(); Capture(first with { Documents = null });
            exit = await Run("--capture", capture);
            check("CLI explicit first-page inspection capture retains Home target", exit == 0
                && client.Requests is [{ Operation: OperationKind.ListDocuments }]
                && client.Requests[0].Target == first.Snapshot!.Target && client.Requests[0].DocumentQuery == new DocumentQuery());

            async Task Invalid(string name, string[] extra)
            {
                Reset(); bool rejected = false;
                try { await Run(extra); }
                catch (ArgumentException) { rejected = true; }
                check(name, rejected && client.Requests.Count == 0);
            }
            await Invalid("CLI document continuation requires a previous capture", ["--offset", "1"]);
            await Invalid("CLI document continuation refuses an inspection-only capture", ["--capture", capture, "--offset", "1"]);
            Capture(first);
            await Invalid("CLI document continuation refuses a skipped offset", ["--capture", capture, "--offset", "2"]);
            Capture(first with { Documents = first.Documents! with { NextOffset = null } });
            await Invalid("CLI document continuation refuses an exhausted page", ["--capture", capture, "--offset", "1"]);
            foreach (string token in new[] { "", new string('z', 64), new string('a', 63) })
            {
                Capture(first with { Documents = first.Documents! with { InventoryToken = token } });
                await Invalid("CLI document capture rejects malformed inventory tokens", ["--capture", capture, "--offset", "1"]);
            }
            Capture(first with { Snapshot = first.Snapshot! with { Target = first.Snapshot.Target with { SessionId = "other-session" } } });
            await Invalid("CLI document capture rejects a different session", ["--capture", capture, "--offset", "1"]);
            Capture(first with { Outcome = Outcome.Rejected });
            await Invalid("CLI document capture rejects an unsuccessful result", ["--capture", capture, "--offset", "1"]);
            foreach (var bad in new string[][]
            {
                ["--offset", "-1"], ["--offset", "4097"], ["--offset", "invalid"], ["--offset", "999999999999"],
                ["--limit", "0"], ["--limit", "101"], ["--limit", "invalid"], ["--limit", "999999999999"],
                ["--request-id", Guid.Empty.ToString()], ["--request-id", "invalid"], ["--request-id"],
                ["--offset"], ["--limit"], ["--limit", "1", "--limit", "2"], ["--name", "forbidden"]
            }) await Invalid("CLI malformed document query fails before any native request", bad);

            Capture(first); Reset();
            client.Snapshot = client.Snapshot with { Target = client.Snapshot.Target with { Revision = 1 } };
            exit = await Run("--capture", capture, "--offset", "1");
            var rejected = JsonSerializer.Deserialize<OperationResult>(output.ToString(), Protocol.Json)!;
            check("CLI stale captured document context returns structured failure without a fresh inspection", exit == 1
                && rejected.Code == "stale_target" && error.ToString().Contains(rejected.Message)
                && client.Requests is [{ Operation: OperationKind.ListDocuments }]
                && client.Requests[0].Target == first.Snapshot!.Target);
            client.Snapshot = first.Snapshot!; client.FailInventory = true; Reset();
            exit = await Run("--capture", capture, "--offset", "1");
            rejected = JsonSerializer.Deserialize<OperationResult>(output.ToString(), Protocol.Json)!;
            check("CLI stale inventory returns the shared structured rejection", exit == 1 && rejected.Code == "stale_inventory"
                && client.Requests is [{ Operation: OperationKind.ListDocuments }] && client.Requests.All(r => !r.IsWrite));
            client.FailInventory = false; client.FailInspection = true; Reset();
            exit = await Run();
            rejected = JsonSerializer.Deserialize<OperationResult>(output.ToString(), Protocol.Json)!;
            check("CLI rejected initial inspection stops before document discovery", exit == 1 && rejected.Code == "host_stopped"
                && client.Requests is [{ Operation: OperationKind.Inspect }] && error.ToString().Contains(rejected.Message));
        }
        finally { File.Delete(session); File.Delete(capture); Directory.Delete(folder); }
    }

    private sealed class RecordingClient(SessionBinding binding) : IOperationClient
    {
        public const string Token = "ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789";
        public List<OperationRequest> Requests { get; } = [];
        public bool FailInventory { get; set; }
        public bool FailInspection { get; set; }
        public Snapshot Snapshot { get; set; } = new(new(binding.ProcessId, binding.ProcessStart, binding.SessionId, "", "", 0),
            "2027", "", "", false, false, "", "", 0, false, "No document.");
        public OpenDocumentInfo[] Documents { get; } = [new("opaque-native-id", "Unsaved Family", "", false, true, true, true, false, true)];
        public Task<OperationResult> ExecuteAsync(OperationRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            if (request.Operation == OperationKind.Inspect)
                return Task.FromResult(FailInspection ? OperationResult.Reject(request, "host_stopped", "Host stopped.")
                    : new(request.Id, Outcome.Inspected, "inspected", "Inspected.", Snapshot));
            if (request.Operation != OperationKind.ListDocuments || request.IsWrite) throw new InvalidOperationException("Unexpected native action.");
            if (request.Target != Snapshot.Target) return Task.FromResult(OperationResult.Reject(request, "stale_target", "Native context changed."));
            if (FailInventory) return Task.FromResult(OperationResult.Reject(request, "stale_inventory", "Inventory changed."));
            return Task.FromResult(new OperationResult(request.Id, Outcome.Inspected, "inspected", "Documents inspected.", Snapshot,
                Documents: new(Documents, 2, request.DocumentQuery!.Offset == 0 ? 1 : null, Token)));
        }
    }
}
