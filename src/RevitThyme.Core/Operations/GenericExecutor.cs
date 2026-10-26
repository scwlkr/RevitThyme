using System.Text.Json;
using System.Diagnostics.CodeAnalysis;

namespace RevitThyme.Core;

internal static class GenericExecutor
{
    public static OperationResult Execute(IModel model, OperationRequest request, Snapshot before)
    {
        if (!Handles(request.Operation)) return OperationResult.Reject(request, "unknown_operation", "Unknown typed operation.");
        if (model is not IGenericModel generic) return OperationResult.Reject(request, "unsupported", "Host does not support generic operations.");
        if (request.Target != before.Target) return OperationResult.Reject(request, "stale_target", "Inspect again: document, view or revision changed.");
        if (request.Name is not null || request.ExpectedName is not null || request.OperationId is not null)
            return OperationResult.Reject(request, "invalid_request", "Unexpected arguments.");
        if (UnexpectedArguments(request)) return OperationResult.Reject(request, "invalid_request", "Unexpected operation arguments.");
        return Route(generic, request, before);
    }

    private static bool Handles(OperationKind operation) => operation is OperationKind.ListViews or OperationKind.QueryElements
        or OperationKind.InspectElements or OperationKind.GetSelection or OperationKind.SetSelection or OperationKind.SetParameters;
    private static bool IsQuery(OperationKind operation) => operation is OperationKind.ListViews or OperationKind.QueryElements;
    private static bool UsesIds(OperationKind operation) => operation is OperationKind.InspectElements or OperationKind.SetSelection;
    private static bool UnexpectedArguments(OperationRequest request)
    {
        bool query = IsQuery(request.Operation), ids = UsesIds(request.Operation);
        return (!query && request.Query is not null) || (!ids && request.ElementIds is not null)
            || (request.Operation != OperationKind.SetParameters && request.Edits is not null)
            || (request.Operation != OperationKind.InspectElements && request.ParameterOffset != 0);
    }

    private static OperationResult Route(IGenericModel model, OperationRequest request, Snapshot before)
    {
        if (IsQuery(request.Operation)) return Query(model, request, before);
        if (request.Operation == OperationKind.GetSelection)
            return new(request.Id, Outcome.Inspected, "inspected", "Native selection read.", before, Selection: model.GetSelection());
        if (UsesIds(request.Operation)) return WithIds(model, request, before);
        return Parameters(model, request, before);
    }

    private static OperationResult Query(IGenericModel model, OperationRequest request, Snapshot before)
    {
        var query = request.Query ?? new();
        if (!WithinQueryBounds(query) || !ValidCategory(query.CategoryId) || InvalidViewFilter(request.Operation, query))
            return OperationResult.Reject(request, "invalid_query", "Use offset 0–1000000, limit 1–100 and a negative category ID.");
        return new(request.Id, Outcome.Inspected, "inspected", "Native elements queried.", before,
            Elements: request.Operation == OperationKind.ListViews ? model.ListViews(query) : model.QueryElements(query));
    }

    private static bool WithinQueryBounds(ElementQuery query) => query.Offset is >= 0 and <= 1000000
        && query.Limit is >= 1 and <= 100 && !(query.NameContains?.Length > 200);
    private static bool ValidCategory(string? category) => category is null || long.TryParse(category, out long id) && id < 0;
    private static bool InvalidViewFilter(OperationKind operation, ElementQuery query) => operation == OperationKind.ListViews
        && (query.CategoryId is not null || query.IncludeTypes);

    private static OperationResult WithIds(IGenericModel model, OperationRequest request, Snapshot before)
    {
        if (!ValidIds(request.Operation, request.ElementIds))
            return OperationResult.Reject(request, "invalid_elements", "Use distinct native unique IDs; inspect 1–5, select 0–100.");
        if (request.Operation == OperationKind.InspectElements)
        {
            if (request.ParameterOffset is < 0 or > 10000) return OperationResult.Reject(request, "invalid_query", "Invalid parameter offset.");
            return new(request.Id, Outcome.Inspected, "inspected", "Native properties and parameters inspected.", before,
                Details: model.InspectElements(request.ElementIds, request.ParameterOffset));
        }
        return Select(model, request, before, request.ElementIds);
    }

    private static bool ValidIds(OperationKind operation, [NotNullWhen(true)] string[]? ids)
    {
        if (ids is null) return false;
        var (minimum, maximum) = operation == OperationKind.InspectElements ? (1, 5) : (0, 100);
        return ids.Length >= minimum && ids.Length <= maximum && ValidIdValues(ids);
    }
    private static bool ValidIdValues(string[] ids) => ids.All(id => !string.IsNullOrWhiteSpace(id) && id.Length <= 200)
        && ids.Distinct().Count() == ids.Length;

    private static OperationResult Select(IGenericModel model, OperationRequest request, Snapshot before, string[] wanted)
    {
        var original = model.GetSelection();
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
        if (request.Edits is not { Length: >= 1 and <= 20 } edits || !edits.All(ValidEdit)
            || edits.Select(e => (e.ElementId, e.ParameterId)).Distinct().Count() != edits.Length)
            return OperationResult.Reject(request, "invalid_edits", "Use 1–20 distinct parameters and matching String/Integer/Double typed values.");
        if (JsonSerializer.SerializeToUtf8Bytes(edits, Protocol.Json).Length > Protocol.MaxOperationBytes / 2)
            return OperationResult.Reject(request, "request_too_large", "Split the batch into smaller reviewed operations; values exceed the result budget.");
        var rejection = ValidateParameters(model, request, edits);
        if (rejection is not null) return rejection;
        if (edits.All(e => e.Expected == e.Value)) return new(request.Id, Outcome.Unchanged, "unchanged", "Values already match; no transaction.", before);
        return WriteParameters(model, request, edits);
    }

    private static OperationResult? ValidateParameters(IGenericModel model, OperationRequest request, ParameterEdit[] edits)
    {
        foreach (var edit in edits)
        {
            var native = model.ReadParameter(edit.ElementId, edit.ParameterId);
            if (native.IsReadOnly || native.Truncated || native.Value.Storage != edit.Value.Storage || edit.Expected.Storage != edit.Value.Storage)
                return OperationResult.Reject(request, "parameter_blocked", "Parameter is read-only, truncated or has a different storage type.");
            if (native.Value != edit.Expected) return OperationResult.Reject(request, "stale_parameter", "Parameter changed. Inspect again.");
        }
        return null;
    }

    private static OperationResult WriteParameters(IGenericModel model, OperationRequest request, ParameterEdit[] edits)
    {
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
        catch (Exception) { return RecoverParameters(model, request, edits, change); }
        finally { try { change?.Dispose(); } catch (Exception) { } }
    }
    private static OperationResult RecoverParameters(IGenericModel model, OperationRequest request, ParameterEdit[] edits, IParameterChange? change)
    {
        if (change is null) return OperationResult.Reject(request, "write_blocked", "Revit refused the parameter transaction.");
        try
        {
            Require(change.RollBack(), Completion.RolledBack); Verify(model, edits, true);
            return new(request.Id, Outcome.RolledBack, "rolled_back", "Original parameter values verified.", model.Capture());
        }
        catch (Exception) { return new(request.Id, Outcome.Unconfirmed, "unconfirmed", "Completion or rollback is uncertain. Poll the request ID and inspect before another edit."); }
    }

    private static bool ValidEdit(ParameterEdit? edit) => edit is not null && !string.IsNullOrWhiteSpace(edit.ElementId)
        && edit.ElementId.Length <= 200 && long.TryParse(edit.ParameterId, out _) && Valid(edit.Expected) && Valid(edit.Value);
    private static bool Valid(ParameterValue? value) => value is not null && value.Storage switch
    {
        "String" => ValidString(value),
        "Integer" => ValidInteger(value),
        "Double" => ValidDouble(value),
        _ => false
    };
    private static bool ValidString(ParameterValue value) => value.Text is { Length: <= 512 } && value.Integer is null && value.Number is null;
    private static bool ValidInteger(ParameterValue value) => value.Integer is >= int.MinValue and <= int.MaxValue && value.Text is null && value.Number is null;
    private static bool ValidDouble(ParameterValue value) => value.Number is { } number && double.IsFinite(number) && value.Text is null && value.Integer is null;
    private static void Verify(IGenericModel model, ParameterEdit[] edits, bool original)
    {
        foreach (var edit in edits)
            if (model.ReadParameter(edit.ElementId, edit.ParameterId).Value != (original ? edit.Expected : edit.Value))
                throw new InvalidDataException("Native parameter readback failed.");
    }
    private static void Require(Completion actual, Completion expected)
    { if (actual != expected) throw new InvalidDataException("Unexpected native transaction status."); }
}
