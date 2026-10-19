namespace RevitThyme.Core;

public sealed record ViewImageRequest(string ViewId, int PixelSize = 1600);
public sealed record ViewImageResult(string ViewId, long ViewElementId, string ViewName, string ViewType,
    string Path, string Sha256, long Bytes, int Width, int Height);
public interface IViewImageModel : IModel
{
    ViewImageResult ExportViewImage(Guid requestId, ViewImageRequest options);
}

internal static class ViewImageExecutor
{
    public static OperationResult Execute(IModel model, OperationRequest request)
    {
        if (request.Target is null) return OperationResult.Reject(request, "target_required", "Capture the exact document/view/revision first.");
        if (request.Name is not null || request.ExpectedName is not null || request.Query is not null || request.ElementIds is not null
            || request.Edits is not null || request.OperationId is not null || request.ParameterOffset != 0 || request.ExpectedRange is not null
            || request.Range is not null || request.RoomOptions is not null || request.RoomPreview is not null || request.Code is not null || request.TimberFold is not null)
            return OperationResult.Reject(request, "invalid_request", "Only captured target and imageExport arguments are accepted.");
        if (request.ImageExport is not { ViewId: { } id } options || string.IsNullOrWhiteSpace(id) || id.Length > 200 || options.PixelSize is < 128 or > 4096)
            return OperationResult.Reject(request, "invalid_image_export", "Use an explicit native view UniqueId and pixelSize 128–4096.");
        var before = model.Capture();
        if (request.Target != before.Target) return OperationResult.Reject(request, "stale_target", "Document, active view or revision changed. Capture again.");
        if (model is not IViewImageModel native) return OperationResult.Reject(request, "unsupported", "This host does not support view image export.");
        bool exported = false;
        try
        {
            var image = native.ExportViewImage(request.Id, options); exported = true;
            var after = model.Capture();
            if (before != after) return new(request.Id, Outcome.Unconfirmed, "export_unconfirmed", "Native state changed during export. Inspect before continuing.", after);
            return new(request.Id, Outcome.Unchanged, "image_exported", "PNG decoded and hashed; native state unchanged. No model transaction or save.", after, ImageExport: image);
        }
        catch (OperationException error) when (!exported && error.Code != "export_incomplete")
        { return OperationResult.Reject(request, error.Code, error.Message); }
        catch (Exception)
        { return new(request.Id, Outcome.Unconfirmed, "export_incomplete", "Export completion could not be verified. Partial files may remain. Recover this request ID; do not submit a replacement."); }
    }
}
