using System.Collections.Immutable;
using System.Text.Json;

namespace RevitThyme.Core;

public sealed record TimberCaptureRequest(Guid RunId, Target Target, TimberFoldProfile Profile, TimberScope? Scope = null);
// The queue returns owned immutable bytes, never mutable source arrays or native objects.
public sealed record TimberCapturedSnapshot(Snapshot Context, ImmutableArray<byte> SnapshotJson)
{
    public TimberSnapshot Decode() => JsonSerializer.Deserialize<TimberSnapshot>(SnapshotJson.AsSpan(), Protocol.Json)
        ?? throw new OperationException("invalid_capture", "Native snapshot is missing.");
}
public interface ITimberCaptureQueue
{
    Task<TimberCapturedSnapshot> CaptureTimberAsync(TimberCaptureRequest request, CancellationToken cancellationToken = default);
}

internal static class TimberCaptureExecutor
{
    internal static TimberCapturedSnapshot Execute(IModel model, TimberCaptureRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var before = model.Capture();
        if (before.Target != request.Target) throw new OperationException("stale_target", "Document, view or revision changed before extraction.");
        if (model is not ITimberFoldModel native) throw new OperationException("unsupported", "Native TimberFold extraction is unavailable.");
        if (request.Profile.Validate().FirstOrDefault(d => d.Severity == TimberSeverity.Error) is { } invalid)
            throw new OperationException("invalid_profile", invalid.Message);
        var snapshot = native.ExtractTimberFold(request.Profile, request.Scope);
        cancellationToken.ThrowIfCancellationRequested();
        var after = model.Capture();
        if (snapshot.Target != before.Target || after.Target != before.Target || after.IsModified != before.IsModified)
            throw new OperationException("stale_target", "Source state changed during extraction.");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(snapshot, Protocol.Json);
        if (bytes.Length > 134217728) throw new OperationException("capture_limit", "Native snapshot exceeds the 128 MiB run limit.");
        return new(before, bytes.ToImmutableArray());
    }
}
