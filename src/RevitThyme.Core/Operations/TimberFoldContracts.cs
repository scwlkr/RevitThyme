namespace RevitThyme.Core;

public sealed record TimberFoldRequest(TimberFoldProfile? Profile = null, TimberScope? Scope = null);
public sealed record TimberInspectionEvidence(string Directory, string SnapshotSha256, string DiagnosticsSha256);
public sealed record TimberFoldReply(TimberFoldProfile Profile, TimberInspectionSummary Inspection, string SourceFingerprint,
    TimberInspectionEvidence? Evidence = null);

public interface ITimberFoldModel : IModel
{
    TimberSnapshot ExtractTimberFold(TimberFoldProfile profile, TimberScope? scope);
    TimberSnapshot ExtractTimberFold(TimberFoldProfile profile, TimberScope? scope, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = ExtractTimberFold(profile, scope);
        cancellationToken.ThrowIfCancellationRequested();
        return snapshot;
    }
    string TimberFoldEvidenceDirectory { get; }
}

internal static class TimberFoldExecutor
{
    public static OperationResult Execute(IModel model, OperationRequest request)
    {
        if (request.Target is null)
            return OperationResult.Reject(request, "target_required", "Inspect first and supply the exact captured target.");
        if (request.Name is not null || request.ExpectedName is not null || request.Query is not null || request.ElementIds is not null
            || request.Edits is not null || request.OperationId is not null || request.ParameterOffset != 0
            || request.ExpectedRange is not null || request.Range is not null || request.RoomOptions is not null
            || request.RoomPreview is not null || request.Code is not null)
            return OperationResult.Reject(request, "invalid_request", "TimberFold inspection accepts only target, profile and scope.");
        var before = model.Capture();
        if (request.Target != before.Target)
            return OperationResult.Reject(request, "stale_target", "Document, view or revision changed. Inspect again.");
        if (model is not ITimberFoldModel native)
            return OperationResult.Reject(request, "unsupported", "Host does not support TimberFold inspection.");
        var profile = request.TimberFold?.Profile ?? new();
        if (profile.Validate().FirstOrDefault(d => d.Severity == TimberSeverity.Error) is { } invalid)
            return OperationResult.Reject(request, "invalid_profile", invalid.Message);
        var snapshot = native.ExtractTimberFold(profile, request.TimberFold?.Scope);
        if (snapshot.Target != before.Target || model.Capture().Target != before.Target)
            return OperationResult.Reject(request, "stale_target", "Document changed during TimberFold extraction. Inspect again.");
        var scope = request.TimberFold?.Scope ?? new(snapshot.PhaseId, snapshot.DesignOptionIds);
        var inspection = TimberInspection.Inspect(snapshot, profile, scope);
        var evidence = TimberInspectionFiles.Write(native.TimberFoldEvidenceDirectory, request.Id, snapshot, profile, scope, inspection);
        return new(request.Id, Outcome.Inspected, "inspected", inspection.ReadyForPreview
            ? "TimberFold scope inspected. Physical fabrication checks remain outstanding."
            : "TimberFold scope inspected; resolve blocking geometry or profile diagnostics before preview.",
            before, TimberFold: new(profile, inspection, snapshot.DocumentFingerprint, evidence));
    }
}
