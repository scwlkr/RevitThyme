using System.Text;
using System.Text.Json;
using RevitThyme.Core;

namespace RevitThyme.McpTests;

internal static class ResultCheckBoundaryIntegration
{
    public static async Task Run(SessionBinding binding)
    {
        // Exercise the real serializer/framer, not a fake client's interpretation of source length.
        var data = JsonSerializer.SerializeToElement(new
        {
            inputs = new { text = new string('x', 8100) }, result = new string('y', 8100),
            changes = new CodeChanges(Enumerable.Range(0, 512).Select(i => long.MaxValue - i).ToArray(), [], [])
        }, Protocol.Json);
        string body = "/*" + new string('<', 24000) + "*/return new {exists=true,value=1};";
        var code = ResultChecks.Observation(body, data);
        var model = new ObservationBoundaryModel(binding);
        var request = new OperationRequest(Guid.NewGuid(), OperationKind.RunObservation, model.Capture().Target, Code: code);
        var envelope = new BridgeRequest(Protocol.Version, binding.SessionId, request, true);
        int bytes = JsonSerializer.SerializeToUtf8Bytes(envelope, Protocol.Json).Length;
        Program.Check("result checks: escaped 24 KiB source and full input/output/change context exceed old frame but fit advertised frame", bytes > 65536 && bytes <= Protocol.MaxBytes
            && Encoding.UTF8.GetByteCount(code.Source) <= DynamicExecutor.MaxSourceBytes && JsonSerializer.SerializeToUtf8Bytes(data, Protocol.Json).Length <= DynamicExecutor.MaxObservationDataBytes);
        using var stream = new MemoryStream(); await Frames.WriteAsync(stream, envelope, default); stream.Position = 0;
        var received = await Frames.ReadAsync<BridgeRequest>(stream, default);
        var result = new OperationExecutor().Execute(model, received.Request);
        Program.Check("result checks: actual Frames roundtrip preserves independent source/context and valid native digest", received.Request.EquivalentTo(request) && result.Succeeded && model.Executions == 1);
        var tampered = received.Request with { Id = Guid.NewGuid(), Code = received.Request.Code! with { ObservationData = JsonSerializer.SerializeToElement(new { replaced = true }) } };
        Program.Check("result checks: changed separate data with unchanged source digest cannot execute", new OperationExecutor().Execute(model, tampered).Code == "observation_digest_mismatch" && model.Executions == 1);
        bool oversized = false;
        try { _ = ResultChecks.Observation("return true;", JsonDocument.Parse("{\"text\":\"" + new string('<', 8000) + "\"}").RootElement); }
        catch (ArgumentException) { oversized = true; }
        Program.Check("result checks: data limit measures serialized JSON escaping consistently", oversized);
        using var tooLarge = new MemoryStream(); bool rejected = false;
        try { await Frames.WriteAsync(tooLarge, new string('x', Protocol.MaxBytes), default); }
        catch (InvalidDataException) { rejected = true; }
        Program.Check("result checks: real Frames still enforce bounded 256 KiB ceiling", rejected && tooLarge.Length == 0);
    }
    private sealed class ObservationBoundaryModel(SessionBinding binding) : IDynamicModel
    {
        public int Executions { get; private set; }
        public bool TrustedCodeEnabled => true;
        public Snapshot Capture() => new(new(binding.ProcessId, binding.ProcessStart, binding.SessionId, "fixture", "view", 1), "2027", "Fixture", "", false, false, "View", "FloorPlan", 1, true, null);
        public string ReadViewName() => "View";
        public IChange BeginChange() => throw new InvalidOperationException("Observation must not start the regular writable path.");
        public OperationResult ExecuteCode(OperationRequest request) { Executions++; return new(request.Id, Outcome.Inspected, "observed", "Native seam accepted validated observation.", Capture()); }
    }
}
