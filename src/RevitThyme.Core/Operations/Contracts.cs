using System.Text.Json;
using System.Text.Json.Serialization;

namespace RevitThyme.Core;

public enum OperationKind { Inspect, RenameView }
public enum Outcome { Inspected, Applied, Unchanged, Rejected, RolledBack, Unconfirmed }
public sealed record Target(int ProcessId, string ProcessStart, string SessionId,
    string DocumentId, string ViewId, long Revision);
public sealed record Snapshot(Target Target, string RevitVersion, string DocumentTitle,
    string DocumentPath, bool IsWorkshared, bool IsModified, string ViewName,
    string ViewType, long ViewElementId, bool CanModify, string? WriteBlock);
public sealed record OperationRequest(Guid Id, OperationKind Operation,
    Target? Target = null, string? ExpectedName = null, string? Name = null);
public sealed record OperationResult(Guid Id, Outcome Outcome, string Code, string Message,
    Snapshot? Snapshot = null, string? PreviousName = null, long[]? ChangedIds = null)
{
    public bool Succeeded => Outcome is Outcome.Inspected or Outcome.Applied or Outcome.Unchanged;
    public static OperationResult Reject(OperationRequest request, string code, string message) =>
        new(request.Id, Outcome.Rejected, code, message);
}
public interface IOperationClient
{
    Task<OperationResult> ExecuteAsync(OperationRequest request, CancellationToken cancellationToken = default);
}
public static class Protocol
{
    public const int Version = 1;
    public const int MaxBytes = 65536;
    public static readonly JsonSerializerOptions Json = CreateOptions();
    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        return options;
    }
}
