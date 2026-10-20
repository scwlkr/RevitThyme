using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RevitThyme.Core;

public sealed record SessionBinding(int ProtocolVersion, int ProcessId, string ProcessStart,
    string SessionId, string PipeName, string RevitVersion)
{
    public string PipeFor(OperationKind operation) => PipeName + (operation switch
    { OperationKind.GetTimberFoldRun => "-status", OperationKind.CancelTimberFoldRun => "-cancel", OperationKind.GetOperation => "-outcome", _ => "" });
    public static SessionBinding Load(string path)
    {
        if (!Path.IsPathFullyQualified(path) || new FileInfo(path).Length > 4096)
            throw new InvalidDataException("Use an absolute session file (maximum 4 KiB).");
        var binding = JsonSerializer.Deserialize<SessionBinding>(File.ReadAllText(path), Protocol.Json)
            ?? throw new InvalidDataException("Session binding missing.");
        if (binding.ProtocolVersion != Protocol.Version || binding.ProcessId <= 0
            || !Guid.TryParseExact(binding.SessionId, "D", out _)
            || binding.PipeName != $"RevitThyme-Foundation-{binding.ProcessId}-{binding.SessionId}")
            throw new InvalidDataException("Invalid Foundation session binding.");
        binding.AssertProcess(); return binding;
    }
    public void AssertProcess()
    {
        using var process = Process.GetProcessById(ProcessId);
        if (process.HasExited || process.StartTime.ToUniversalTime().Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture) != ProcessStart)
            throw new InvalidDataException("Revit process changed or exited. Select a current session.");
    }
}
public sealed record BridgeRequest(int ProtocolVersion, string SessionId, OperationRequest Request,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool IncludeParameterIdentity = false);
