using System.Security.Cryptography;
using System.Text.Json;

namespace RevitThyme.Core;

public sealed record DocumentQuery(int Offset = 0, int Limit = 50, string? InventoryToken = null);
public sealed record OpenDocumentInfo(string DocumentId, string Title, string Path, bool IsActive, bool IsModified,
    bool IsFamilyDocument, bool IsLinked, bool IsWorkshared, bool IsReadOnly)
{
    public bool HasSavedPath => Path.Length > 0;
}
public sealed record DocumentPage(OpenDocumentInfo[] Items, int Total, int? NextOffset, string InventoryToken);
public interface IDocumentDiscoveryModel : IModel
{
    IEnumerable<OpenDocumentInfo> ReadDocuments();
}

internal static class DocumentDiscoveryExecutor
{
    public static OperationResult Execute(IModel model, OperationRequest request)
    {
        if (request.Target is null) return OperationResult.Reject(request, "target_required", "Capture the selected session, including Home, first.");
        if (request.Name is not null || request.ExpectedName is not null || request.Query is not null || request.ElementIds is not null
            || request.Edits is not null || request.OperationId is not null || request.ParameterOffset != 0 || request.ExpectedRange is not null
            || request.Range is not null || request.RoomOptions is not null || request.RoomPreview is not null || request.Code is not null
            || request.TimberFold is not null || request.ImageExport is not null || request.DocumentLifecycle is not null)
            return OperationResult.Reject(request, "invalid_request", "Only the captured target and documentQuery are accepted.");
        var before = model.Capture();
        if (before.Target != request.Target) return OperationResult.Reject(request, "stale_target", "Session, active context or revision changed. Capture again.");
        if (model is not IDocumentDiscoveryModel native) return OperationResult.Reject(request, "unsupported", "This host cannot enumerate documents.");
        var query = request.DocumentQuery ?? new();
        if (query.Offset is < 0 or > 4096 || query.Limit is < 1 or > 100 || query.Offset > 0 && query.InventoryToken is null
            || query.InventoryToken is { } supplied && (supplied.Length != 64 || !supplied.All(Uri.IsHexDigit)))
            return OperationResult.Reject(request, "invalid_query", "Use offset 0–4096, limit 1–100 and the returned inventoryToken for every continuation.");
        var all = native.ReadDocuments().Take(4097).ToArray();
        if (all.Length > 4096) return OperationResult.Reject(request, "inventory_too_large", "More than 4096 visible documents; no partial inventory returned.");
        all = all.OrderBy(d => d.DocumentId, StringComparer.Ordinal).ToArray();
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        digest.AppendData(JsonSerializer.SerializeToUtf8Bytes(new { before.Target.ProcessId, before.Target.ProcessStart, before.Target.SessionId }, Protocol.Json));
        int totalBytes = 0;
        foreach (var document in all)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(document, Protocol.Json); totalBytes += bytes.Length;
            if (bytes.Length > 16384 || totalBytes > 1048576)
                return OperationResult.Reject(request, "inventory_too_large", "Document metadata exceeds 16 KiB per record or 1 MiB total; identities were not truncated.");
            digest.AppendData(bytes);
        }
        string token = Convert.ToHexString(digest.GetHashAndReset()).ToLowerInvariant();
        if (query.InventoryToken is not null && !string.Equals(query.InventoryToken, token, StringComparison.OrdinalIgnoreCase))
            return OperationResult.Reject(request, "stale_inventory", "Visible documents or their metadata changed. Start at offset zero with a fresh capture and no token.");
        if (query.Offset > all.Length) return OperationResult.Reject(request, "invalid_query", "Offset exceeds the current visible inventory.");
        var items = all.Skip(query.Offset).Take(query.Limit).ToArray();
        OperationResult Result() => new(request.Id, Outcome.Inspected, "inspected", "Visible native document identities inspected; no lifecycle action or file hash.", before,
            Documents: new(items, all.Length, query.Offset + items.Length < all.Length ? query.Offset + items.Length : null, token));
        while (items.Length > 0 && JsonSerializer.SerializeToUtf8Bytes(Result(), Protocol.Json).Length > Protocol.MaxOperationBytes / 2)
            Array.Resize(ref items, items.Length - 1);
        if (query.Offset < all.Length && items.Length == 0 || JsonSerializer.SerializeToUtf8Bytes(Result(), Protocol.Json).Length > Protocol.MaxOperationBytes / 2)
            return OperationResult.Reject(request, "result_too_large", "A complete document identity cannot fit; no fields were truncated.");
        if (model.Capture() != before) return OperationResult.Reject(request, "stale_target", "Native context changed during document inspection. Capture again.");
        return Result();
    }
}
