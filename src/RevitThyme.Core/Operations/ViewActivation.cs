namespace RevitThyme.Core;

public interface IViewActivationModel : IModel { void ActivateView(string viewId); }

internal static class ViewActivationExecutor
{
    public static OperationResult Execute(IModel model, OperationRequest request)
    {
        if (request.DocumentLifecycle is not null || request.Name is not null || request.ExpectedName is not null || request.Query is not null || request.Edits is not null
            || request.OperationId is not null || request.ParameterOffset != 0 || request.ExpectedRange is not null || request.Range is not null
            || request.RoomOptions is not null || request.RoomPreview is not null || request.Code is not null || request.TimberFold is not null || request.ImageExport is not null
            || request.ElementIds is not [var viewId] || string.IsNullOrWhiteSpace(viewId) || viewId.Length > 200)
            return OperationResult.Reject(request, "invalid_request", "Supply exactly one native view UniqueId and the captured target.");
        var before = model.Capture();
        if (request.Target is null || request.Target != before.Target) return OperationResult.Reject(request, "stale_target", "Document/view/revision changed. Capture again.");
        if (!before.HasDocument || !before.CanModify || before.IsWorkshared) return OperationResult.Reject(request, "view_blocked", "Use an active editable nonworkshared project outside a transaction.");
        if (model is not IViewActivationModel native) return OperationResult.Reject(request, "unsupported", "This host does not support view activation.");
        bool returned = false;
        try
        {
            native.ActivateView(viewId); returned = true; var after = model.Capture();
            if (after.Target.DocumentId != before.Target.DocumentId || after.Target.ViewId != viewId || after.DocumentPath != before.DocumentPath || after.IsModified != before.IsModified)
                return new(request.Id, Outcome.Unconfirmed, "view_unconfirmed", "Native view/state did not match the requested activation. Inspect before continuing.", after);
            return new(request.Id, Outcome.Completed, "view_activated", "Exact native view activated without model transaction or save.", after);
        }
        catch (OperationException error) when (!returned && error.Code != "view_unconfirmed") { return OperationResult.Reject(request, error.Code, error.Message); }
        catch (Exception) { return new(request.Id, Outcome.Unconfirmed, "view_unconfirmed", "Native activation completion uncertain. Recover the original request ID."); }
    }
}
