namespace RevitThyme.Core;

public enum Completion { Started, Committed, RolledBack, Other }
// Implementations are invoked only inside a Revit API callback. Core has no Autodesk dependency.
public interface IModel
{
    Snapshot Capture();
    string ReadViewName();
    IChange BeginChange();
}
public interface IChange : IDisposable
{
    Completion StartGroup();
    Completion StartTransaction();
    void RenameView(string name);
    void Regenerate();
    Completion Commit();
    Completion Assimilate();
    Completion RollBack();
}
public sealed class OperationException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
public sealed class OperationExecutor
{
    public OperationResult Execute(IModel model, OperationRequest request)
    {
        try
        {
            if (request.Id == Guid.Empty) return OperationResult.Reject(request, "invalid_id", "A request ID is required.");
            if (request.Operation == OperationKind.InspectTimberFold) return TimberFoldExecutor.Execute(model, request);
            if (request.TimberFold is not null) return OperationResult.Reject(request, "invalid_request", "Unexpected TimberFold arguments.");
            bool dynamic = request.Operation is OperationKind.RunQuery or OperationKind.RunModify;
            if (dynamic) return DynamicExecutor.Execute(model, request);
            if (request.Code is not null) return OperationResult.Reject(request, "invalid_request", "Code is accepted only by run_query/run_modify.");
            bool utility = request.Operation is OperationKind.GetViewRange or OperationKind.SetViewRange
                or OperationKind.PreviewRoomDimensions or OperationKind.CreateRoomDimensions;
            if (!utility && (request.ExpectedRange is not null || request.Range is not null || request.RoomOptions is not null || request.RoomPreview is not null))
                return OperationResult.Reject(request, "invalid_request", "Unexpected utility arguments.");
            if (request.Query is not null || request.ElementIds is not null || request.Edits is not null || request.OperationId is not null || request.ParameterOffset != 0)
            {
                if (request.Operation is OperationKind.Inspect or OperationKind.RenameView)
                    return OperationResult.Reject(request, "invalid_request", "Unexpected arguments for this operation.");
            }
            var before = model.Capture();
            return request.Operation switch
            {
                OperationKind.Inspect when request.Target is null && request.ExpectedName is null && request.Name is null =>
                    new(request.Id, Outcome.Inspected, "inspected", "Active document and view inspected.", before),
                OperationKind.Inspect => OperationResult.Reject(request, "invalid_request", "Inspection takes no edit arguments."),
                OperationKind.RenameView => Rename(model, request, before),
                _ when utility => UtilityExecutor.Execute(model, request, before),
                _ => GenericExecutor.Execute(model, request, before)
            };
        }
        catch (OperationException error) { return OperationResult.Reject(request, error.Code, error.Message); }
        catch (Exception error)
        {
            if (request.Operation is OperationKind.RunQuery or OperationKind.RunModify)
                return new(request.Id, Outcome.Unconfirmed, "unconfirmed", (error.GetType().Name + ": " + error.Message)[..Math.Min(240, error.GetType().Name.Length + 2 + error.Message.Length)]);
            return OperationResult.Reject(request, "inspection_failed", "Revit context could not be inspected. Refresh.");
        }
    }
    private static OperationResult Rename(IModel model, OperationRequest request, Snapshot before)
    {
        if (request.Target is null || request.ExpectedName is null)
            return OperationResult.Reject(request, "target_required", "Inspect first and supply the exact captured target and name.");
        if (request.Target != before.Target || request.ExpectedName != before.ViewName)
            return OperationResult.Reject(request, "stale_target", "Document, view or model changed. Inspect again before editing.");
        if (!before.CanModify)
            return OperationResult.Reject(request, "write_blocked", before.WriteBlock ?? "The active view cannot be edited.");
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name != request.Name.Trim() || request.Name.Length > 200)
            return OperationResult.Reject(request, "invalid_name", "Use a name of 1–200 characters without leading or trailing spaces.");
        if (request.Name == before.ViewName)
            return new(request.Id, Outcome.Unchanged, "unchanged", "Name verified; no transaction opened.", before);

        IChange? change = null;
        bool started = false;
        try
        {
            change = model.BeginChange();
            started = true;
            Require(change.StartGroup(), Completion.Started);
            Require(change.StartTransaction(), Completion.Started);
            change.RenameView(request.Name);
            change.Regenerate();
            if (model.ReadViewName() != request.Name) throw new InvalidDataException("Pre-commit readback failed.");
            Require(change.Commit(), Completion.Committed);
            if (model.ReadViewName() != request.Name) throw new InvalidDataException("Post-commit readback failed.");
            // Capture while the group can still be rolled back. No fallible read after assimilation.
            var after = model.Capture();
            Require(change.Assimilate(), Completion.Committed);
            return new(request.Id, Outcome.Applied, "applied", "View name verified. One Revit undo item.",
                after, before.ViewName, [before.ViewElementId]);
        }
        catch (Exception error)
        {
            if (!started)
                return OperationResult.Reject(request, error is OperationException native ? native.Code : "write_blocked",
                    "Revit refused the edit before a transaction started. Inspect again.");
            try
            {
                if (change!.RollBack() == Completion.RolledBack && model.ReadViewName() == before.ViewName)
                    return new(request.Id, Outcome.RolledBack, "rolled_back", "Revit refused the edit; original name verified.", model.Capture());
            }
            catch (Exception) { }
            return new(request.Id, Outcome.Unconfirmed, "unconfirmed",
                "Completion or rollback could not be confirmed. Inspect Revit; do not repeat this edit.");
        }
        finally { try { change?.Dispose(); } catch (Exception) { } }
    }
    private static void Require(Completion actual, Completion expected)
    {
        if (actual != expected) throw new InvalidDataException("Unexpected Revit transaction status.");
    }
}
