namespace RevitThyme.Core;

public sealed record DocumentLifecycleRequest(string Path, string? DocumentId = null, string? ExpectedSha256 = null);
public sealed record DocumentLifecycleResult(string DocumentId, string Path, string Title, bool IsActive,
    bool IsModified, string Sha256, bool Performed);
public interface IDocumentLifecycleModel : IModel
{
    DocumentLifecycleResult ChangeDocument(OperationKind operation, DocumentLifecycleRequest options);
}

internal static class DocumentLifecycleExecutor
{
    public static bool Handles(OperationKind kind) => kind is OperationKind.OpenDocument or OperationKind.ActivateDocument or OperationKind.SaveDocument;
    public static OperationResult Execute(IModel model, OperationRequest request)
    {
        if (request.Target is null) return OperationResult.Reject(request, "target_required", "Capture the current document before a lifecycle request.");
        if (request.Name is not null || request.ExpectedName is not null || request.Query is not null || request.ElementIds is not null
            || request.Edits is not null || request.OperationId is not null || request.ParameterOffset != 0 || request.ExpectedRange is not null
            || request.Range is not null || request.RoomOptions is not null || request.RoomPreview is not null || request.Code is not null
            || request.TimberFold is not null || request.ImageExport is not null)
            return OperationResult.Reject(request, "invalid_request", "Only captured target and documentLifecycle arguments are accepted.");
        if (request.DocumentLifecycle is not { } options || !ValidPath(options.Path)
            || (request.Operation == OperationKind.OpenDocument ? options.DocumentId is not null : string.IsNullOrWhiteSpace(options.DocumentId) || options.DocumentId.Length > 200)
            || (request.Operation == OperationKind.ActivateDocument ? options.ExpectedSha256 is not null : !ValidHash(options.ExpectedSha256)))
            return OperationResult.Reject(request, "invalid_lifecycle", "Use a canonical absolute local RVT path; open/save require its expected SHA-256, activate/save require its exact document ID.");
        var before = model.Capture();
        if (before.Target != request.Target) return OperationResult.Reject(request, "stale_target", "Active document, view or revision changed. Capture again.");
        if (!before.CanModify || before.IsWorkshared) return OperationResult.Reject(request, "lifecycle_blocked", "Use an editable nonworkshared project outside a transaction/edit mode.");
        if (request.Operation == OperationKind.SaveDocument && (options.DocumentId != before.Target.DocumentId || !SamePath(options.Path, before.DocumentPath)))
            return OperationResult.Reject(request, "wrong_document", "Save accepts only the exact captured active document ID and saved path.");
        if (model is not IDocumentLifecycleModel native) return OperationResult.Reject(request, "unsupported", "This host does not support document lifecycle operations.");
        bool returned = false;
        try
        {
            var result = native.ChangeDocument(request.Operation, options); returned = true;
            var after = model.Capture();
            bool verified = SamePath(result.Path, options.Path) && ValidHash(result.Sha256) && !string.IsNullOrWhiteSpace(result.DocumentId)
                && (options.DocumentId is null || result.DocumentId == options.DocumentId)
                && (request.Operation == OperationKind.OpenDocument
                    ? before == after && !result.IsModified && string.Equals(result.Sha256, options.ExpectedSha256, StringComparison.OrdinalIgnoreCase)
                    : after.Target.DocumentId == result.DocumentId && SamePath(after.DocumentPath, result.Path) && result.IsActive
                      && (request.Operation != OperationKind.SaveDocument || !after.IsModified && !result.IsModified));
            if (!verified) return new(request.Id, Outcome.Unconfirmed, "lifecycle_unconfirmed", "Native lifecycle state did not match the request. Inspect and recover this ID before continuing.", after, DocumentLifecycle: result);
            return new(request.Id, Outcome.Completed, "document_" + (request.Operation switch { OperationKind.OpenDocument => "opened", OperationKind.ActivateDocument => "activated", _ => "saved" }),
                "Native lifecycle completion verified. File saves and document/UI lifecycle are outside model Undo; no implicit save or close.", after, DocumentLifecycle: result);
        }
        catch (OperationException error) when (!returned && error.Code != "lifecycle_unconfirmed")
        { return OperationResult.Reject(request, error.Code, error.Message); }
        catch (Exception)
        { return new(request.Id, Outcome.Unconfirmed, "lifecycle_unconfirmed", "Lifecycle completion is uncertain. Recover the original ID and inspect native state; do not submit a replacement."); }
    }
    internal static bool ValidHash(string? hash) => hash is { Length: 64 } && hash.All(Uri.IsHexDigit);
    internal static bool SamePath(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    private static bool ValidPath(string? path)
    {
        try { return path is { Length: <= 1024 } && Path.IsPathFullyQualified(path) && !path.StartsWith("\\\\", StringComparison.Ordinal)
            && SamePath(Path.GetFullPath(path), path) && Path.GetExtension(path).Equals(".rvt", StringComparison.OrdinalIgnoreCase); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException) { return false; }
    }
}
