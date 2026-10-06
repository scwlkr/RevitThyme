using System.Text.Json;
using System.Text.Json.Serialization;

namespace RevitThyme.Native;
public enum Operation { Capture, Geometry, Validate, Apply, Outcome, Cancel }
public enum Status { Queued, Executing, Captured, Validated, AppliedVerified, UnchangedVerified, RollbackConfirmed, OutcomeUnconfirmed, Rejected, Cancelled }
public record Target(int ProcessId, string ProcessStartTicks, string SessionId, string DocumentId, string ViewId, uint Revision);
public record Plane(string LevelId, string LevelName, double BaseFeet, double OffsetFeet, bool Unlimited, bool AllowUnlimited);
public record Range(Plane Top, Plane Cut, Plane Bottom, Plane Depth)
{
    [JsonIgnore] public Plane[] Planes => [Top, Cut, Bottom, Depth];
}
public record RawPlane(string LevelId, double OffsetFeet);
public record RawRange(RawPlane Top, RawPlane Cut, RawPlane Bottom, RawPlane Depth);
public record Capture(string SnapshotId, Target Target, string DocumentName, string ViewName, string ViewKind,
    Range Original, double[] BoundsFeet, uint TriangleCount, bool Partial, string[] Diagnostics, uint ExpiresInSeconds);
public record Facts(Capture Capture, RawRange Original, double[][][] Triangles, DateTime Created);
public record Request(int Protocol, string RequestId, Operation Operation, Target? Target, string SnapshotId,
    Range? Range, int ChunkIndex, string OutcomeId, bool Confirmed);
public record Hello(int Protocol, int ProcessId, string ProcessStartTicks, string SessionId, string Credential, string ConnectionId);
public record Reply(int Protocol, string RequestId, Status Status, string Code, string Message, bool RefreshRequired,
    Capture? Capture, double[][][]? Triangles, RawRange? NativeValues, string[] ChangedIds, string[] SkippedIds)
{
    public static Reply Result(Request r, Status status, string code, string message, bool refresh = false,
        RawRange? values = null, string[]? changed = null) => new(1, r.RequestId, status, code, message, refresh,
            null, null, values, changed ?? [], []);
}
public static class Wire
{
    public const int MaxBytes = 65_536;
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, false) }
    };
    public static void Validate(Request r)
    {
        if (r.Protocol != 1 || !Guid.TryParse(r.RequestId, out var id) || id == Guid.Empty || !Enum.IsDefined(r.Operation))
            throw new InvalidDataException("Protocol or request identity.");
        if (r.Operation is Operation.Geometry or Operation.Validate or Operation.Apply && r.Target is null) throw new InvalidDataException("Target required.");
        if (r.Operation is Operation.Apply or Operation.Validate)
        {
            if (r.Range is null || string.IsNullOrEmpty(r.SnapshotId)) throw new InvalidDataException("Proposal required.");
            foreach (var p in r.Range.Planes)
                if (p is null || !long.TryParse(p.LevelId, out _) || !double.IsFinite(p.OffsetFeet) || !double.IsFinite(p.BaseFeet)
                    || Math.Abs(p.OffsetFeet) > 1_000_000) throw new InvalidDataException("Invalid feet/reference.");
        }
        if (r.Operation == Operation.Apply && !r.Confirmed) throw new InvalidDataException("Explicit confirmation required.");
        if (r.Operation is Operation.Outcome or Operation.Cancel && !Guid.TryParse(r.OutcomeId, out _))
            throw new InvalidDataException("Original request identity required.");
        if (r.ChunkIndex is < 0 or > 390) throw new InvalidDataException("Chunk limit.");
    }
}
