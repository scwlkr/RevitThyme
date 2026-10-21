using System.Text.Json;
using RevitThyme.Core;

namespace RevitThyme.McpTests;

// Retained native transport seam: this is deliberately not native Revit proof.
internal sealed class ResultCheckFakeClient(SessionBinding binding) : IOperationClient
{
    public Dictionary<Guid, OperationResult> History { get; } = [];
    public List<OperationRequest> Requests { get; } = [];
    private readonly Dictionary<Guid, OperationRequest> arguments = [];
    public string Observed { get; set; } = "wanted";
    public bool Exists { get; set; } = true;
    public bool ExtraModified { get; set; }
    public bool TimeoutAfterCommit { get; set; }
    public bool ReturnTransportLostAfterCommit { get; set; }
    public string? ActionLookupFault { get; set; }
    public JsonElement? ObservedJson { get; set; }
    public bool FailVerificationTransport { get; set; }
    public bool InterveningRevision { get; set; }
    public string? ObservationError { get; set; }
    public bool MissingObservation { get; set; }
    public int Actions { get; private set; }
    public Snapshot Snapshot { get; private set; } = new(new(binding.ProcessId, binding.ProcessStart, binding.SessionId, "fixture", "view", 1),
        "2027", "Fixture", "", false, false, "View", "FloorPlan", 1, true, null);
    public Task<OperationResult> ExecuteAsync(OperationRequest request, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        if (request.Operation == OperationKind.GetOperation)
        {
            if (Actions > 0 && request.OperationId is { } lookup && arguments.TryGetValue(lookup, out var action) && action.Operation == OperationKind.RunModify && ActionLookupFault is { } fault)
                return Task.FromResult(OperationResult.Reject(request, fault, "Injected retained-history lookup failure."));
            return Task.FromResult(History.TryGetValue(request.OperationId!.Value, out var old) ? old : OperationResult.Reject(request, "not_found", "Not retained."));
        }
        if (request.Operation == OperationKind.Inspect) return Task.FromResult(new OperationResult(request.Id, Outcome.Inspected, "inspected", "Fake transport snapshot.", Snapshot));
        if (arguments.TryGetValue(request.Id, out var previous))
            return Task.FromResult(previous.EquivalentTo(request) ? History[request.Id] : OperationResult.Reject(request, "id_conflict", "Changed arguments."));
        arguments.Add(request.Id, request);
        if (request.Target != Snapshot.Target)
            return Save(OperationResult.Reject(request, "stale_target", "Intervening revision."));
        if (request.Operation == OperationKind.RunModify)
        {
            Actions++; var before = Snapshot.Target; Snapshot = Snapshot with { Target = Snapshot.Target with { Revision = before.Revision + 1 }, IsModified = true };
            var result = new OperationResult(request.Id, Outcome.Applied, "applied", "Action claimed success.", Snapshot,
                Execution: new(ResultChecks.Hash(request.Code!.Source), 1, JsonSerializer.SerializeToElement(new { success = true }),
                    Changes: new([], ExtraModified ? [1, 2] : [1], []), BeforeTarget: before));
            History.Add(request.Id, result);
            if (InterveningRevision) Snapshot = Snapshot with { Target = Snapshot.Target with { Revision = Snapshot.Target.Revision + 1 } };
            if (TimeoutAfterCommit) { TimeoutAfterCommit = false; throw new IOException("Simulated lost committed response."); }
            if (ReturnTransportLostAfterCommit) return Task.FromResult(new OperationResult(request.Id, Outcome.Unconfirmed, "transport_lost", "Actual PipeClient-style returned uncertain submission."));
            return Task.FromResult(result);
        }
        if (request.Operation != OperationKind.RunObservation) throw new InvalidOperationException("Unexpected fake operation.");
        bool preparation = !request.Code!.Source.Contains("addedCategories", StringComparison.Ordinal);
        if (!preparation && FailVerificationTransport) throw new IOException("Simulated checker disconnect.");
        if (!preparation && ObservationError is not null)
            return Save(new(request.Id, Outcome.RolledBack, ObservationError, "Injected native checker error.", Snapshot,
                Execution: new(ResultChecks.Hash(request.Code.Source), 1, BeforeTarget: request.Target)));
        var value = preparation ? JsonSerializer.SerializeToElement(new { modified = new long[] { 1 }, deleted = Array.Empty<long>() })
            : MissingObservation ? JsonSerializer.SerializeToElement(new { observations = Array.Empty<object>(), addedCategories = Array.Empty<object>() })
            : JsonSerializer.SerializeToElement(new { observations = new[] { new { observation = new { exists = Exists, value = (object?)ObservedJson ?? Observed }, error = (string?)null } }, addedCategories = Array.Empty<object>() });
        return Save(new(request.Id, Outcome.Inspected, "observed", "Fake observation.", Snapshot,
            Execution: new(ResultChecks.Hash(request.Code.Source), 1, value, BeforeTarget: request.Target)));
    }
    private Task<OperationResult> Save(OperationResult result) { History.Add(result.Id, result); return Task.FromResult(result); }
}
