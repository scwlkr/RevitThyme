using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RevitThyme.ContractChecks;
// Execution DTOs only. No Revit references, add-in registration, listener or model writes in M1.
public enum Operation { Capture, Validate, Apply, Outcome, Cancel }
public record NativeTarget(int ProcessId, long ProcessStartTicks, Guid SessionId, Guid DocumentId, string ViewUniqueId, long Revision);
public record NativePlane(string LevelId, double OffsetFeet, bool Unlimited);
public record NativeRange(NativePlane Top, NativePlane Cut, NativePlane Bottom, NativePlane Depth);
public record NativeRequest(int Protocol, Guid RequestId, Operation Operation, NativeTarget Target, string SnapshotId, NativeRange? Range);
public static class Protocol
{
    public const int Version = 1;
    public const int MaxFrameBytes = 65_536;
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
        Converters = { new JsonStringEnumConverter<Operation>(JsonNamingPolicy.CamelCase, false) }
    };
    public static byte[] Frame(NativeRequest request)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(request, Json);
        if (body.Length > MaxFrameBytes) throw new InvalidDataException("Frame limit.");
        var result = new byte[body.Length + 4];
        BinaryPrimitives.WriteInt32LittleEndian(result, body.Length);
        body.CopyTo(result, 4);
        return result;
    }
    public static NativeRequest Read(ReadOnlySpan<byte> bytes, NativeTarget expected)
    {
        if (bytes.Length < 4) throw new InvalidDataException("Incomplete frame.");
        int length = BinaryPrimitives.ReadInt32LittleEndian(bytes);
        if (length is < 1 or > MaxFrameBytes || bytes.Length != length + 4) throw new InvalidDataException("Invalid frame length.");
        var request = JsonSerializer.Deserialize<NativeRequest>(bytes[4..], Json) ?? throw new InvalidDataException("Empty request.");
        if (request.Protocol != Version || request.Target != expected || request.RequestId == Guid.Empty || string.IsNullOrWhiteSpace(request.SnapshotId))
            throw new InvalidDataException("Protocol/session/document/view/revision mismatch.");
        if (request.Operation is Operation.Apply or Operation.Validate && request.Range is null) throw new InvalidDataException("Range required.");
        if (request.Range is {} range)
            foreach (var plane in new[] { range.Top, range.Cut, range.Bottom, range.Depth })
                if (plane is null || !double.IsFinite(plane.OffsetFeet) || !long.TryParse(plane.LevelId, out _)) throw new InvalidDataException("Invalid native values.");
        return request;
    }
}
