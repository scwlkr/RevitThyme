using System.Text.Json;

namespace RevitThyme.Core;

internal static class GenericExecutor
{
    public static OperationResult Execute(IModel model, OperationRequest request, Snapshot before)
    {
        if (request.Operation is not (OperationKind.ListViews or OperationKind.QueryElements or OperationKind.InspectElements
            or OperationKind.GetSelection or OperationKind.SetSelection or OperationKind.SetParameters))
            return OperationResult.Reject(request, "unknown_operation", "Unknown typed operation.");
        if (model is not IGenericModel generic) return OperationResult.Reject(request, "unsupported", "Host does not support generic operations.");
        if (request.Target != before.Target) return OperationResult.Reject(request, "stale_target", "Inspect again: document, view or revision changed.");
        if (request.Name is not null || request.ExpectedName is not null || request.OperationId is not null)
            return OperationResult.Reject(request, "invalid_request", "Unexpected arguments.");
        bool query = request.Operation is OperationKind.ListViews or OperationKind.QueryElements;
        bool ids = request.Operation is OperationKind.InspectElements or OperationKind.SetSelection;
        if ((!query && request.Query is not null) || (!ids && request.ElementIds is not null)
            || (request.Operation != OperationKind.SetParameters && request.Edits is not null)
            || (request.Operation != OperationKind.InspectElements && request.ParameterOffset != 0))
            return OperationResult.Reject(request, "invalid_request", "Unexpected operation arguments.");
        if (query)
        {
            var q = request.Query ?? new();
            if (q.Offset < 0 || q.Offset > 1000000 || q.Limit is < 1 or > 100 || q.NameContains?.Length > 200
                || (q.CategoryId is not null && (!long.TryParse(q.CategoryId, out long category) || category >= 0))
                || (request.Operation == OperationKind.ListViews && (q.CategoryId is not null || q.IncludeTypes)))
                return OperationResult.Reject(request, "invalid_query", "Use offset 0–1000000, limit 1–100 and a negative category ID.");
            return new(request.Id, Outcome.Inspected, "inspected", "Native elements queried.", before,
                Elements: request.Operation == OperationKind.ListViews ? generic.ListViews(q) : generic.QueryElements(q));
        }
        if (request.Operation == OperationKind.GetSelection)
            return new(request.Id, Outcome.Inspected, "inspected", "Native selection read.", before, Selection: generic.GetSelection());
        if (ids && (request.ElementIds is not { } items || items.Length > (request.Operation == OperationKind.InspectElements ? 5 : 100)
            || (request.Operation == OperationKind.InspectElements && items.Length == 0) || items.Any(string.IsNullOrWhiteSpace)
            || items.Any(id => id.Length > 200) || items.Distinct().Count() != items.Length))
            return OperationResult.Reject(request, "invalid_elements", "Use distinct native unique IDs; inspect 1–5, select 0–100.");
        if (request.Operation == OperationKind.InspectElements)
        {
            if (request.ParameterOffset is < 0 or > 10000) return OperationResult.Reject(request, "invalid_query", "Invalid parameter offset.");
            return new(request.Id, Outcome.Inspected, "inspected", "Native properties and parameters inspected.", before,
                Details: generic.InspectElements(request.ElementIds!, request.ParameterOffset));
        }
        if (request.Operation == OperationKind.SetSelection) return Select(generic, request, before);
        return Parameters(generic, request, before);
    }
    private static OperationResult Select(IGenericModel model, OperationRequest request, Snapshot before)
    {
        var original = model.GetSelection(); var wanted = request.ElementIds!;
        if (original.Order().SequenceEqual(wanted.Order())) return new(request.Id, Outcome.Unchanged, "unchanged", "Selection already matches.", before, Selection: original);
        // Resolve every element before touching UI state.
        foreach (string id in wanted) model.InspectElements([id], 0);
        try
        {
            model.SetSelection(wanted);
            if (!model.GetSelection().Order().SequenceEqual(wanted.Order())) throw new InvalidDataException("Selection readback failed.");
            return new(request.Id, Outcome.Applied, "applied", "Selection verified; no model transaction.", model.Capture(), Selection: wanted);
        }
        catch (Exception)
        {
            try
            {
                model.SetSelection(original);
                if (model.GetSelection().Order().SequenceEqual(original.Order()))
                    return new(request.Id, Outcome.RolledBack, "rolled_back", "Original selection restored.", model.Capture(), Selection: original);
            }
            catch (Exception) { }
            return new(request.Id, Outcome.Unconfirmed, "unconfirmed", "Selection restoration could not be confirmed.");
        }
    }
    private static OperationResult Parameters(IGenericModel model, OperationRequest request, Snapshot before)
    {
        if (!before.CanModify) return OperationResult.Reject(request, "write_blocked", before.WriteBlock!);
        if (request.Edits is not { Length: >= 1 and <= 20 } edits || edits.Any(e => e is null || string.IsNullOrWhiteSpace(e.ElementId)
            || e.ElementId.Length > 200 || !long.TryParse(e.ParameterId, out _) || !Valid(e.Expected) || !Valid(e.Value))
            || edits.Select(e => (e.ElementId, e.ParameterId)).Distinct().Count() != edits.Length)
            return OperationResult.Reject(request, "invalid_edits", "Use 1–20 distinct parameters and matching String/Integer/Double typed values.");
        if (JsonSerializer.SerializeToUtf8Bytes(edits, Protocol.Json).Length > Protocol.MaxBytes / 2)
            return OperationResult.Reject(request, "request_too_large", "Split the batch into smaller reviewed operations; values exceed the result budget.");
        foreach (var edit in edits)
        {
            var native = model.ReadParameter(edit.ElementId, edit.ParameterId);
            if (native.IsReadOnly || native.Truncated || native.Value.Storage != edit.Value.Storage || edit.Expected.Storage != edit.Value.Storage)
                return OperationResult.Reject(request, "parameter_blocked", "Parameter is read-only, truncated or has a different storage type.");
            if (native.Value != edit.Expected) return OperationResult.Reject(request, "stale_parameter", "Parameter changed. Inspect again.");
        }
        if (edits.All(e => e.Expected == e.Value)) return new(request.Id, Outcome.Unchanged, "unchanged", "Values already match; no transaction.", before);
        IParameterChange? change = null;
        try
        {
            change = model.BeginParameterChange(); Require(change.StartGroup(), Completion.Started); Require(change.StartTransaction(), Completion.Started);
            foreach (var edit in edits) change.SetParameter(edit);
            change.Regenerate(); Verify(model, edits, false); Require(change.Commit(), Completion.Committed); Verify(model, edits, false);
            var after = model.Capture(); var changed = edits.Select(e => model.InspectElements([e.ElementId], 0)[0].Element.Id).Distinct().ToArray();
            Require(change.Assimilate(), Completion.Committed);
            return new(request.Id, Outcome.Applied, "applied", "Parameter batch verified; one Revit Undo item.", after,
                ChangedIds: changed, ParameterChanges: edits);
        }
        catch (Exception)
        {
            if (change is null) return OperationResult.Reject(request, "write_blocked", "Revit refused the parameter transaction.");
            try
            {
                Require(change.RollBack(), Completion.RolledBack); Verify(model, edits, true);
                return new(request.Id, Outcome.RolledBack, "rolled_back", "Original parameter values verified.", model.Capture());
            }
            catch (Exception) { return new(request.Id, Outcome.Unconfirmed, "unconfirmed", "Completion or rollback is uncertain. Poll the request ID and inspect before another edit."); }
        }
        finally { try { change?.Dispose(); } catch (Exception) { } }
    }
    private static bool Valid(ParameterValue? v) => v is not null && v.Storage switch
    {
        "String" => v.Text is { Length: <= 512 } && v.Integer is null && v.Number is null,
        "Integer" => v.Integer is >= int.MinValue and <= int.MaxValue && v.Text is null && v.Number is null,
        "Double" => v.Number is { } n && double.IsFinite(n) && v.Text is null && v.Integer is null,
        _ => false
    };
    private static void Verify(IGenericModel model, ParameterEdit[] edits, bool original)
    {
        foreach (var edit in edits)
            if (model.ReadParameter(edit.ElementId, edit.ParameterId).Value != (original ? edit.Expected : edit.Value))
                throw new InvalidDataException("Native parameter readback failed.");
    }
    private static void Require(Completion actual, Completion expected)
    { if (actual != expected) throw new InvalidDataException("Unexpected native transaction status."); }
}
