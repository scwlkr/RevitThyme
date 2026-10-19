using RevitThyme.Core;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace RevitThyme.Unit;

internal static class TimberFoldOperations
{
    public static void Run(Action<string, bool> check)
    {
        var model = new FakeModel();
        var result = new OperationExecutor().Execute(model,
            new(Guid.NewGuid(), OperationKind.Inspect, TimberFold: new()));
        check("unrelated operations reject TimberFold arguments before model work",
            result.Code == "invalid_request" && model.Transactions == 0);
        result = new OperationExecutor().Execute(model, new(Guid.NewGuid(), OperationKind.InspectTimberFold));
        check("TimberFold inspection requires a captured target", result.Code == "target_required");
        using var native = new TimberModel();
        var request = new OperationRequest(Guid.NewGuid(), OperationKind.InspectTimberFold, native.Target);
        var executor = new OperationExecutor();
        result = executor.Execute(native, request);
        check("TimberFold native snapshot produces accounted bounded inspection without a transaction",
            result.Succeeded && result.TimberFold?.Inspection is { ReadyForPreview: true, FaceCount: 1, SourceCount: 1 }
            && result.TimberFold.SourceFingerprint == "fixture-sha256" && native.Extractions == 1 && native.Base.Transactions == 0);
        check("TimberFold inspection retains full private source and diagnostic evidence",
            result.TimberFold?.Evidence is { } evidence && File.Exists(Path.Combine(evidence.Directory, "snapshot.json"))
            && File.Exists(Path.Combine(evidence.Directory, "diagnostics.json"))
            && JsonDocument.Parse(File.ReadAllText(Path.Combine(evidence.Directory, "snapshot.json"))).RootElement
                .GetProperty("snapshot").GetProperty("sources")[0].GetProperty("id").GetString() == "1");
    }

    public static async Task Cli(Action<string, bool> check)
    {
        string folder = Path.Combine(Path.GetTempPath(), "RevitThyme-TimberCli-" + Guid.NewGuid());
        Directory.CreateDirectory(folder);
        string session = Path.Combine(folder, "session.json");
        try
        {
            using var process = Process.GetCurrentProcess();
            string id = Guid.NewGuid().ToString();
            var binding = new SessionBinding(Protocol.Version, process.Id,
                process.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture), id,
                $"RevitThyme-Foundation-{process.Id}-{id}", "2027");
            File.WriteAllText(session, JsonSerializer.Serialize(binding, Protocol.Json));
            using var client = new TimberClient();
            client.Model.Base.Target = new(binding.ProcessId, binding.ProcessStart, id, "document", "view", 1);
            var output = new StringWriter(); var error = new StringWriter();
            int exit = await RevitThyme.Cli.Program.RunAsync(["timberfold", "inspect", "--session", session, "--json"], output, error, _ => client);
            var result = JsonSerializer.Deserialize<OperationResult>(output.ToString(), Protocol.Json)!;
            check("CLI TimberFold inspection shares captured operation and recovers evidence writes without a model transaction", exit == 0
                && result.TimberFold?.Inspection.ReadyForPreview == true
                && client.Requests is [{ Operation: OperationKind.Inspect }, { Operation: OperationKind.InspectTimberFold }]
                && client.Requests[1].Target == client.Model.Target && !client.Requests[0].IsWrite
                && client.Requests[1].IsWrite && client.Model.Base.Transactions == 0);
        }
        finally { File.Delete(session); Directory.Delete(folder); }
    }

    private sealed class TimberClient : IOperationClient, IDisposable
    {
        public TimberModel Model { get; } = new();
        public List<OperationRequest> Requests { get; } = [];
        public Task<OperationResult> ExecuteAsync(OperationRequest request, CancellationToken cancellationToken = default)
        { Requests.Add(request); return Task.FromResult(new OperationExecutor().Execute(Model, request)); }
        public void Dispose() => Model.Dispose();
    }

    private sealed class TimberModel : ITimberFoldModel, IDisposable
    {
        public FakeModel Base { get; } = new();
        public Target Target => Base.Target;
        public int Extractions { get; private set; }
        public string TimberFoldEvidenceDirectory { get; } = Path.Combine(Path.GetTempPath(), "RevitThyme-TimberEvidence-" + Guid.NewGuid());
        public Snapshot Capture() => Base.Capture();
        public string ReadViewName() => Base.ReadViewName();
        public IChange BeginChange() => Base.BeginChange();
        public void Dispose() { if (Directory.Exists(TimberFoldEvidenceDirectory)) Directory.Delete(TimberFoldEvidenceDirectory, true); }
        public TimberSnapshot ExtractTimberFold(TimberFoldProfile profile, TimberScope? scope)
        {
            Extractions++;
            return new(Target, "fixture-sha256", "phase-1", [],
                [new("1", "wall-unique", TimberCategory.Wall, TimberDisposition.Included, "Exterior wall", ["W-1-0"], [], "wall-sha256")],
                [new("W-1-0", "1", TimberFaceKind.Wall, new(0, 0, 0), new(1, 0, 0), new(0, 0, 1), new(0, -1, 0),
                    [[new(0, 0), new(4, 0), new(4, 3), new(0, 3)]])], [], []);
        }
    }
}
