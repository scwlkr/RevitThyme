using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using RevitThyme.Core;

namespace RevitThyme.Unit;

internal static class ParameterInspectionCli
{
    public static async Task Run(Action<string, bool> check)
    {
        string folder = Path.Combine(Path.GetTempPath(), "RevitThyme-ParameterCli-" + Guid.NewGuid());
        Directory.CreateDirectory(folder);
        string session = Path.Combine(folder, "session.json");
        string capture = Path.Combine(folder, "page.json");
        try
        {
            using var process = Process.GetCurrentProcess(); string id = Guid.NewGuid().ToString();
            var binding = new SessionBinding(Protocol.Version, process.Id,
                process.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture),
                id, $"RevitThyme-Foundation-{process.Id}-{id}", "2027");
            File.WriteAllText(session, JsonSerializer.Serialize(binding, Protocol.Json));
            var output = new StringWriter(); var error = new StringWriter(); var client = new InspectionClient(binding);
            string[] Arguments(params string[] extra) => ["inspect-elements", "--session", session, "--element", "selected-element", .. extra];
            int exit = await Cli.Program.RunAsync(Arguments("--json"), output, error, _ => client);
            var result = JsonSerializer.Deserialize<OperationResult>(output.ToString(), Protocol.Json)!;
            var p = result.Details![0].Parameters[0];
            check("CLI parameter inspection sends fresh capture and exact element", exit == 0
                && client.Requests is [{ Operation: OperationKind.Inspect },
                    { Operation: OperationKind.InspectElements, ParameterOffset: 0, ElementIds: ["selected-element"] }]
                && client.Requests[1].Target == client.Snapshot.Target && client.Requests.All(r => !r.IsWrite));
            check("CLI JSON preserves parameter identity and native empty assigned state", p.Id == "9007199254740993"
                && p.IsShared && p.SharedGuid == InspectionClient.SharedGuid && p.BuiltInParameter is null
                && p.HasValue && p.Value.Text == "" && result.Details[0].NextParameterOffset == 1);
            using var json = JsonDocument.Parse(output.ToString());
            check("CLI JSON emits inapplicable identity as explicit null", json.RootElement.GetProperty("details")[0]
                .GetProperty("parameters")[0].GetProperty("builtInParameter").ValueKind == JsonValueKind.Null);
            File.WriteAllText(capture, output.ToString());
            output.GetStringBuilder().Clear(); client.Requests.Clear();
            check("CLI continuation preserves the previous captured revision and page offset", await Cli.Program.RunAsync(
                    Arguments("--json", "--capture", capture, "--parameter-offset", "20"), output, error, _ => client) == 0
                && client.Requests is [{ Operation: OperationKind.InspectElements, ParameterOffset: 20 }]
                && client.Requests[0].Target == result.Snapshot!.Target);
            client.Snapshot = client.Snapshot with { Target = client.Snapshot.Target with { Revision = 2 } };
            output.GetStringBuilder().Clear();
            check("CLI rejects changed revisions between parameter pages", await Cli.Program.RunAsync(
                    Arguments("--json", "--capture", capture, "--parameter-offset", "20"), output, error, _ => client) == 1
                && JsonSerializer.Deserialize<OperationResult>(output.ToString(), Protocol.Json)!.Code == "stale_target");
            try { await Cli.Program.RunAsync(Arguments("--parameter-offset", "20"), output, error, _ => client); check("CLI continuation requires a capture", false); }
            catch (ArgumentException) { check("CLI continuation requires a capture", true); }
            output.GetStringBuilder().Clear(); client.Requests.Clear();
            check("CLI default output includes identity and pagination", await Cli.Program.RunAsync(Arguments(), output, error, _ => client) == 0
                && output.ToString().Contains("\"sharedGuid\"") && output.ToString().Contains("\"nextParameterOffset\":1")
                && client.Requests[1].ParameterOffset == 0);
            client.FailContext = true; client.Requests.Clear(); output.GetStringBuilder().Clear();
            check("CLI does not inspect parameters after rejected context", await Cli.Program.RunAsync(Arguments("--json"), output, error, _ => client) == 1
                && client.Requests.Count == 1);
            client.FailContext = false;
            try { await Cli.Program.RunAsync(Arguments("--parameter-offset", "invalid"), output, error, _ => client); check("CLI invalid parameter offset rejected", false); }
            catch (FormatException) { check("CLI invalid parameter offset rejected", true); }
        }
        finally { File.Delete(session); File.Delete(capture); Directory.Delete(folder); }
    }

    private sealed class InspectionClient(SessionBinding binding) : IOperationClient
    {
        public static readonly Guid SharedGuid = Guid.Parse("87a96cdc-7f6a-44b6-b0a7-fd5faecfcb22");
        public List<OperationRequest> Requests { get; } = [];
        public bool FailContext { get; set; }
        public Snapshot Snapshot { get; set; } = new(new(binding.ProcessId, binding.ProcessStart, binding.SessionId, "document", "view", 1),
            "2027", "Fixture", "", false, false, "Plan", "FloorPlan", 42, true, null);
        public Task<OperationResult> ExecuteAsync(OperationRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            if (FailContext) return Task.FromResult(OperationResult.Reject(request, "no_document", "No document."));
            if (request.Operation == OperationKind.InspectElements && request.Target != Snapshot.Target)
                return Task.FromResult(OperationResult.Reject(request, "stale_target", "Document changed."));
            ElementDetail[]? details = request.Operation == OperationKind.InspectElements
                ? [new(new("selected-element", 100, "Fixture", "Walls", "-2000011", "Wall", false),
                    [new("9007199254740993", "Comments", "text", false, new("String", Text: ""),
                        IsShared: true, SharedGuid: SharedGuid, HasValue: true)], request.ParameterOffset + 1)] : null;
            return Task.FromResult(new OperationResult(request.Id, Outcome.Inspected, "inspected", "Inspected.", Snapshot, Details: details));
        }
    }
}
