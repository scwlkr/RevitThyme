using System.Buffers.Binary;
using System.Diagnostics;
using System.Text.Json;
using RevitThyme.Core;

namespace RevitThyme.Unit;

internal static class Boundaries
{
    public static async Task Run(Action<string, bool> check)
    {
        var request = new OperationRequest(Guid.NewGuid(), OperationKind.Inspect);
        using var stream = new MemoryStream();
        await Frames.WriteAsync(stream, request, default); stream.Position = 0;
        check("framed request round-trip", await Frames.ReadAsync<OperationRequest>(stream, default) == request);
        foreach (int size in new[] { -1, 0, Protocol.MaxBytes + 1 })
        {
            byte[] bytes = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(bytes, size);
            try { await Frames.ReadAsync<OperationRequest>(new MemoryStream(bytes), default); check("invalid frame rejected", false); }
            catch (InvalidDataException) { check("invalid frame rejected", true); }
        }
        try { await Frames.ReadAsync<OperationRequest>(new MemoryStream([1, 0]), default); check("truncated frame rejected", false); }
        catch (EndOfStreamException) { check("truncated frame rejected", true); }
        foreach (string json in new[] { "{\"id\":\"" + request.Id + "\",\"operation\":5}",
            "{\"id\":\"" + request.Id + "\",\"operation\":\"Inspect\",\"surprise\":1}" })
        {
            try { JsonSerializer.Deserialize<OperationRequest>(json, Protocol.Json); check("unknown schema rejected", false); }
            catch (JsonException) { check("unknown schema rejected", true); }
        }
        string temp = Path.Combine(Path.GetTempPath(), "RevitThyme-Unit-" + Guid.NewGuid()); Directory.CreateDirectory(temp);
        try
        {
            using var process = Process.GetCurrentProcess(); string session = Guid.NewGuid().ToString();
            var binding = new SessionBinding(Protocol.Version, process.Id,
                process.StartTime.ToUniversalTime().Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture),
                session, $"RevitThyme-Foundation-{process.Id}-{session}", "2027");
            string path = Path.Combine(temp, "binding.json"); File.WriteAllText(path, JsonSerializer.Serialize(binding, Protocol.Json));
            check("current process binding verified", SessionBinding.Load(path) == binding);
            File.WriteAllText(path, JsonSerializer.Serialize(binding with { ProcessStart = "old" }, Protocol.Json));
            try { SessionBinding.Load(path); check("reused PID rejected", false); }
            catch (InvalidDataException) { check("reused PID rejected", true); }
            File.WriteAllText(path, JsonSerializer.Serialize(binding, Protocol.Json));
            var output = new StringWriter(); var error = new StringWriter(); var client = new FixtureClient(binding);
            check("CLI help works offline", await Cli.Program.RunAsync(["--help"], output, error) == 0 && output.ToString().Contains("rename-view"));
            output.GetStringBuilder().Clear();
            check("CLI inspect uses shared operation", await Cli.Program.RunAsync(["inspect", "--session", path, "--json"], output, error, _ => client) == 0);
            string capture = Path.Combine(temp, "capture.json"); File.WriteAllText(capture, output.ToString());
            output.GetStringBuilder().Clear();
            check("CLI edit sends captured target", await Cli.Program.RunAsync(["rename-view", "--session", path, "--capture", capture, "--name", "CLI Renamed", "--json"], output, error, _ => client) == 0
                && client.Model.Name == "CLI Renamed" && client.Model.Transactions == 1);
            check("CLI failed operation returns exit 1", await Cli.Program.RunAsync(["rename-view", "--session", path, "--capture", capture, "--name", "Another"], output, error, _ => client) == 1);
            try { await Cli.Program.RunAsync(["inspect", "--session", path, "--bogus"], output, error); check("CLI malformed options rejected", false); }
            catch (ArgumentException) { check("CLI malformed options rejected", true); }
            try { await Cli.Program.RunAsync(["inspect", "--session", path, "--name", "oops"], output, error); check("CLI unrelated options rejected", false); }
            catch (ArgumentException) { check("CLI unrelated options rejected", true); }
        }
        finally
        {
            // Delete only files created in this exact unique test directory, with no recursion.
            foreach (string file in Directory.GetFiles(temp)) File.Delete(file); Directory.Delete(temp);
        }
    }
    private sealed class FixtureClient : IOperationClient
    {
        public FakeModel Model { get; } = new();
        public FixtureClient(SessionBinding binding) => Model.Target = Model.Target with
        { ProcessId = binding.ProcessId, ProcessStart = binding.ProcessStart, SessionId = binding.SessionId };
        public Task<OperationResult> ExecuteAsync(OperationRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new OperationExecutor().Execute(Model, request));
    }
}
