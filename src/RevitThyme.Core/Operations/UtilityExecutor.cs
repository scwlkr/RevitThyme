using System.Text.Json;

namespace RevitThyme.Core;

internal static class UtilityExecutor
{
    public static OperationResult Execute(IModel model, OperationRequest request, Snapshot before)
    {
        if (model is not IUtilityModel native) return OperationResult.Reject(request, "unsupported", "Host does not support native utilities.");
        if (request.Target != before.Target) return OperationResult.Reject(request, "stale_target", "Document, view or revision changed. Refresh before editing.");
        if (request.Name is not null || request.ExpectedName is not null || request.Query is not null || request.Edits is not null
            || request.OperationId is not null || request.ParameterOffset != 0)
            return OperationResult.Reject(request, "invalid_request", "Unexpected operation arguments.");
        bool range = request.Operation is OperationKind.GetViewRange or OperationKind.SetViewRange;
        if (range && (request.ElementIds is not null || request.RoomOptions is not null || request.RoomPreview is not null)
            || !range && (request.ExpectedRange is not null || request.Range is not null))
            return OperationResult.Reject(request, "invalid_request", "Unexpected utility arguments.");
        if (range)
        {
            if (request.Operation == OperationKind.GetViewRange && (request.ExpectedRange is not null || request.Range is not null))
                return OperationResult.Reject(request, "invalid_request", "Range inspection takes no edit values.");
            var info = native.ReadViewRange();
            if (request.Operation == OperationKind.GetViewRange)
                return new(request.Id, Outcome.Inspected, "inspected", "Native view range inspected.", before, ViewRange: info);
            if (!Valid(request.Range) || !Valid(request.ExpectedRange))
                return OperationResult.Reject(request, "invalid_range", "Supply all four level/offset planes; offsets must be finite feet within ±1000000.");
            if (!info.CanEdit || !before.CanModify) return OperationResult.Reject(request, "write_blocked", info.EditBlock ?? before.WriteBlock!);
            if (request.ExpectedRange != info.Settings) return OperationResult.Reject(request, "stale_range", "Native range changed. Refresh.");
            if (request.Range == info.Settings) return new(request.Id, Outcome.Unchanged, "unchanged", "Range already matches; no transaction.", before, ViewRange: info);
            return Write(native, request, "RevitThyme View Range", c => c.SetViewRange(request.Range!),
                () => native.ReadViewRange().Settings == request.Range, () => native.ReadViewRange().Settings == info.Settings,
                () => new(request.Id, Outcome.Applied, "applied", "View range verified; one Revit Undo item.", native.Capture(),
                    ChangedIds: [before.ViewElementId], ViewRange: native.ReadViewRange()));
        }
        if (request.ElementIds is not { Length: 1 } ids || string.IsNullOrWhiteSpace(ids[0]) || ids[0].Length > 200)
            return OperationResult.Reject(request, "invalid_elements", "Supply one native room UniqueId.");
        var options = request.RoomOptions ?? new();
        if (!double.IsFinite(options.InsetFeet) || options.InsetFeet is < 0.25 or > 20
            || !double.IsFinite(options.TagClearanceFeet) || options.TagClearanceFeet is < 0.25 or > 20)
            return OperationResult.Reject(request, "invalid_options", "Inset and tag clearance must be 0.25–20 feet.");
        if (request.Operation == OperationKind.PreviewRoomDimensions && request.RoomPreview is not null)
            return OperationResult.Reject(request, "invalid_request", "Preview takes no expected plan.");
        var preview = native.PlanRoomDimensions(ids[0], options);
        if (request.Operation == OperationKind.PreviewRoomDimensions)
            return new(request.Id, Outcome.Inspected, "inspected", "Two native face dimensions planned; no elements created.", before, RoomDimensions: preview);
        if (!before.CanModify) return OperationResult.Reject(request, "write_blocked", before.WriteBlock!);
        if (request.RoomPreview is null || JsonSerializer.Serialize(request.RoomPreview, Protocol.Json) != JsonSerializer.Serialize(preview, Protocol.Json))
            return OperationResult.Reject(request, "stale_room", "Room boundaries, tags or dimensions changed. Preview again.");
        if (preview.ExistingIds.Length == 2)
            return new(request.Id, Outcome.Unchanged, "unchanged", "Both face dimensions already exist; no transaction.", before,
                ChangedIds: preview.ExistingIds, RoomDimensions: preview);
        if (preview.ExistingIds.Length != 0) return OperationResult.Reject(request, "partial_dimensions", "One face dimension already exists. Resolve it before creating a pair.");
        var created = new List<long>();
        return Write(native, request, "RevitThyme Room Dimensions", c => c.CreateRoomDimensions(preview, created),
            () => native.VerifyDimensions(created.ToArray(), preview), () => native.ElementsAbsent(created.ToArray()),
            () => new(request.Id, Outcome.Applied, "applied", "Two native face dimensions verified; one Revit Undo item.", native.Capture(),
                ChangedIds: created.ToArray(), RoomDimensions: preview));
    }
    private static bool Valid(ViewRangeSettings? s) => s is not null && new[] { s.Top, s.Cut, s.Bottom, s.Depth }.All(p =>
        p is not null && !string.IsNullOrWhiteSpace(p.Level) && p.Level.Length <= 200 && double.IsFinite(p.OffsetFeet) && Math.Abs(p.OffsetFeet) <= 1000000);
    private static OperationResult Write(IUtilityModel model, OperationRequest request, string label, Action<IUtilityChange> apply,
        Func<bool> verify, Func<bool> restored, Func<OperationResult> result)
    {
        IUtilityChange? change = null;
        try
        {
            change = model.BeginUtilityChange(label);
            Require(change.StartGroup(), Completion.Started); Require(change.StartTransaction(), Completion.Started);
            apply(change); change.Regenerate();
            if (!verify()) throw new InvalidDataException("Pre-commit native readback failed.");
            Require(change.Commit(), Completion.Committed);
            if (!verify()) throw new InvalidDataException("Post-commit native readback failed.");
            var success = result(); Require(change.Assimilate(), Completion.Committed); return success;
        }
        catch (Exception error)
        {
            if (change is null) return OperationResult.Reject(request, "write_blocked", "Revit refused the transaction.");
            try
            {
                Require(change.RollBack(), Completion.RolledBack);
                if (!restored()) throw new InvalidDataException("Rollback readback failed.");
                return new(request.Id, Outcome.RolledBack, error is OperationException op ? op.Code : "rolled_back",
                    error is OperationException native ? native.Message + " Original state verified." : "Original native state verified after rollback.", model.Capture());
            }
            catch (Exception) { return new(request.Id, Outcome.Unconfirmed, "unconfirmed", "Completion or rollback is uncertain. Inspect Revit and recover this request ID before another edit."); }
        }
        finally { try { change?.Dispose(); } catch (Exception) { } }
    }
    private static void Require(Completion actual, Completion expected)
    { if (actual != expected) throw new InvalidDataException("Unexpected native transaction status."); }
}
