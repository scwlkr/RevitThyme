using System.Text.Json;

namespace RevitThyme.Core;

/// <summary>Coordinates retained receipts, never retries an uncertain action under another identity.</summary>
public sealed class ResultCheckCoordinator
{
    private readonly IOperationClient client;
    private readonly ResultCheckPlan plan;
    private readonly JsonElement inputs;
    private readonly int budget;
    private readonly Guid operationId;
    private readonly string sourceHash;
    public string PlanDigest { get; }
    public Guid PreparationId { get; }
    public Guid VerificationId { get; }

    public ResultCheckCoordinator(IOperationClient client, Guid operationId, ResultCheckPlan plan, JsonElement inputs,
        string sourceSha256, bool trusted, int budgetMilliseconds = 10000)
    {
        if (!trusted) throw new ArgumentException("Result observations require trusted=true; full process trust, no sandbox.");
        if (operationId == Guid.Empty || sourceSha256.Length != 64 || !sourceSha256.All(Uri.IsHexDigit)) throw new ArgumentException("Original request ID and source SHA-256 required.");
        ResultChecks.Validate(plan);
        this.client = client; this.operationId = operationId; sourceHash = sourceSha256.ToLowerInvariant(); budget = budgetMilliseconds;
        // Own all mutable arrays/JSON before the first asynchronous boundary.
        this.plan = JsonSerializer.Deserialize<ResultCheckPlan>(JsonSerializer.SerializeToUtf8Bytes(plan, Protocol.Json), Protocol.Json)!;
        this.inputs = ResultChecks.CanonicalInputs(inputs);
        PlanDigest = ResultChecks.Hash(JsonSerializer.Serialize(this.plan, Protocol.Json));
        string binding = operationId + ":" + sourceHash + ":" + PlanDigest + ":" + this.inputs.GetRawText();
        PreparationId = Identity(binding + ":prepare"); VerificationId = Identity(binding + ":verify");
        _ = PreparationCode(); // Fail invalid bounds before submitting any action.
        // Include the mandatory fixed-length data-digest header in preflight, before any action submission.
        _ = ResultChecks.Observation(ResultChecks.ObservationSource(this.plan), JsonSerializer.SerializeToElement(new { }), budget);
    }
    private static Guid Identity(string binding) => new(Convert.FromHexString(ResultChecks.Hash(binding))[..16]);
    private Task<OperationResult> Retained(Guid id, CancellationToken token) => client.ExecuteAsync(new(Guid.NewGuid(), OperationKind.GetOperation, OperationId: id), token);
    private CodeRequest PreparationCode() => ResultChecks.Observation(
        "var inputs = data.GetProperty(\"inputs\");\n#line 1 \"allowed-changes.cs\"\n" + plan.AllowedChangesSource,
        JsonSerializer.SerializeToElement(new { operationId, planDigest = PlanDigest, sourceSha256 = sourceHash, inputs }, Protocol.Json), budget);

    public async Task<ResultCheckPreparation> Prepare(Target target, CancellationToken token = default)
    {
        var code = PreparationCode();
        var receipt = await Retained(PreparationId, token);
        if (receipt.Code == "not_found") receipt = await client.ExecuteAsync(new(PreparationId, OperationKind.RunObservation, target, Code: code), token);
        if (receipt.Succeeded && (receipt.Id != PreparationId || receipt.Execution?.SourceSha256 != ResultChecks.Hash(code.Source)
            || receipt.Snapshot?.Target != target || receipt.Execution.BeforeTarget != target))
            throw new OperationException("preparation_mismatch", "Preparation receipt does not match this exact target and plan.");
        if (receipt.Succeeded) _ = Allowed(receipt);
        return new(operationId, PreparationId, VerificationId, PlanDigest, sourceHash, receipt);
    }

    public async Task<OperationResult> Run(OperationRequest action, CancellationToken token = default)
    {
        if (action.Id != operationId || action.Code is null || ResultChecks.Hash(action.Code.Source) != sourceHash || action.Target is null
            || action.Operation is not (OperationKind.RunModify or OperationKind.RunQuery)) throw new ArgumentException("Action binding mismatch.");
        var original = await Retained(operationId, token);
        if (original.Code != "not_found") return await VerifyCore(token, original);
        var preparation = await Prepare(action.Target, token);
        if (!preparation.Receipt.Succeeded)
            return new(operationId, Outcome.Rejected, "checks_not_prepared", "No action submitted: " + preparation.Receipt.Message,
                Verification: Report("error", "Pre-action policy observation failed."));
        try { original = await client.ExecuteAsync(action, token); }
        catch (Exception error) when (error is IOException or OperationCanceledException or InvalidOperationException)
        {
            // Recovery reads use a separate bounded token after transport cancellation, never resubmit the action.
            try { using var recovery = new CancellationTokenSource(TimeSpan.FromSeconds(5)); original = await Retained(operationId, recovery.Token); }
            catch (Exception) { return new(operationId, Outcome.Unconfirmed, "unconfirmed", "Action transport interrupted; recover the original request ID. No action repeated.", Verification: Report("unconfirmed", error.Message)); }
        }
        if (original.Code is "not_found" or "pending") return Unresolved(null, "Original action is not yet confirmed; recover the same ID.");
        // Known execution is retained even if the checker connection subsequently fails.
        try { return await VerifyCore(token, original); }
        catch (Exception error) { return original with { Verification = Report("unconfirmed", "Verification interrupted: " + Short(error.Message)) }; }
    }

    public Task<OperationResult> Verify(CancellationToken token = default) => VerifyCore(token, null);
    private async Task<OperationResult> VerifyCore(CancellationToken token, OperationResult? known)
    {
        OperationResult original;
        try { original = await Retained(operationId, token); }
        catch (Exception error) { return Unresolved(known, "Original receipt recovery interrupted: " + Short(error.Message)); }
        if (original.Id != operationId || original.Code is "not_found" or "pending" or "host_stopped")
            return Unresolved(known, "Original action receipt unavailable: " + original.Code + ". Recover the same action ID; no action repeated.");
        if (original.Id != operationId || !original.Succeeded || original.Execution?.SourceSha256 != sourceHash
            || original.Execution.BeforeTarget is null || original.Snapshot is null)
            return original with { Verification = Report("unconfirmed", "Successful original native receipt, exact source attribution and before/after targets required.") };
        OperationResult prep;
        try { prep = await Retained(PreparationId, token); }
        catch (Exception error) { return original with { Verification = Report("unconfirmed", "Pre-action receipt recovery interrupted: " + Short(error.Message)) }; }
        if (!prep.Succeeded || prep.Id != PreparationId || prep.Execution?.SourceSha256 != ResultChecks.Hash(PreparationCode().Source)
            || prep.Snapshot?.Target != original.Execution.BeforeTarget || prep.Execution.BeforeTarget != prep.Snapshot.Target)
            return original with { Verification = Report("unconfirmed", "Matching pre-action policy receipt is missing or does not precede this action at its exact target.") };
        CodeChanges allowed;
        try { allowed = Allowed(prep); }
        catch (Exception error) { return original with { Verification = Report("error", "Invalid pre-action policy: " + Short(error.Message)) }; }
        var changes = original.Execution.Changes;
        // Queries have no committed changes. Modifications must carry an authoritative bounded native change set.
        if (changes is null && original.Outcome == Outcome.Inspected) changes = new([], [], []);
        if (changes is null || !ValidChanges(changes)) return original with { Verification = Report("unconfirmed", "Original native change coverage is missing or invalid.") };
        try
        {
            var context = JsonSerializer.SerializeToElement(new { inputs, result = original.Execution.Value,
                changes, operationId, planDigest = PlanDigest, sourceSha256 = sourceHash }, Protocol.Json);
            var code = ResultChecks.Observation(ResultChecks.ObservationSource(plan), context, budget);
            var observation = await Retained(VerificationId, token);
            if (observation.Code == "not_found") observation = await client.ExecuteAsync(new(VerificationId, OperationKind.RunObservation, original.Snapshot.Target, Code: code), token);
            if (!observation.Succeeded || observation.Id != VerificationId || observation.Execution?.SourceSha256 != ResultChecks.Hash(code.Source)
                || observation.Execution.BeforeTarget != original.Snapshot.Target || observation.Snapshot?.Target != original.Snapshot.Target)
                return original with { Verification = Report(observation.Code == "stale_target" || observation.Outcome == Outcome.Unconfirmed || observation.Code == "pending" ? "unconfirmed" : "error",
                    "Native observation did not certify this exact post-action target: " + observation.Code + ": " + observation.Message) };
            return original with { Verification = Evaluate(observation, changes, allowed) };
        }
        catch (Exception error) { return original with { Verification = Report(error is IOException or OperationCanceledException ? "unconfirmed" : "error", Short(error.Message)) }; }
    }
    private ResultVerification Report(string status, string message) => new(status, operationId, PreparationId, VerificationId, PlanDigest, sourceHash, Short(message));
    private OperationResult Unresolved(OperationResult? known, string message) =>
        (known is { } receipt && receipt.Id == operationId && receipt.Code is not ("not_found" or "pending" or "host_stopped")
            ? receipt : new OperationResult(operationId, Outcome.Unconfirmed, "unconfirmed", Short(message)))
        with { Verification = Report("unconfirmed", message) };
    private static string Short(string value) => value[..Math.Min(500, value.Length)];
    private static bool ValidChanges(CodeChanges changes) => changes.Added is not null && changes.Modified is not null && changes.Deleted is not null
        && changes.Added.Length + changes.Modified.Length + changes.Deleted.Length <= 512
        && changes.Added.Concat(changes.Modified).Concat(changes.Deleted).All(id => id > 0);
    private static CodeChanges Allowed(OperationResult prep)
    {
        var value = prep.Execution?.Value ?? throw new InvalidDataException("Policy returned no JSON.");
        long[] Read(string name)
        {
            var array = value.GetProperty(name); if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() > 512) throw new InvalidDataException("Policy IDs must be bounded arrays.");
            var ids = array.EnumerateArray().Select(v => v.GetInt64()).ToArray();
            if (ids.Any(id => id <= 0) || ids.Distinct().Count() != ids.Length) throw new InvalidDataException("Policy IDs must be positive unique native IDs.");
            return ids.Order().ToArray();
        }
        var allowed = new CodeChanges([], Read("modified"), Read("deleted"));
        if (!ValidChanges(allowed)) throw new InvalidDataException("Policy exceeds 512 IDs."); return allowed;
    }
    private ResultVerification Evaluate(OperationResult observation, CodeChanges changes, CodeChanges allowed)
    {
        var value = observation.Execution?.Value ?? throw new InvalidDataException("Observation returned no JSON.");
        var rows = value.GetProperty("observations");
        if (rows.ValueKind != JsonValueKind.Array || rows.GetArrayLength() != plan.Checks.Length) throw new InvalidDataException("Missing check observations.");
        var verdicts = plan.Checks.Select((check, i) => EvaluateCheck(check, rows[i])).ToArray();
        var unexpectedAdded = new List<long>(); var violations = new List<string>();
        var categories = value.GetProperty("addedCategories");
        if (categories.ValueKind != JsonValueKind.Array || categories.GetArrayLength() != changes.Added.Length) throw new InvalidDataException("Missing added category coverage.");
        var observedIds = new HashSet<long>();
        foreach (var item in categories.EnumerateArray())
        {
            long id = item.GetProperty("id").GetInt64();
            if (!changes.Added.Contains(id) || !observedIds.Add(id)) throw new InvalidDataException("Added category identity mismatch.");
            var category = item.GetProperty("categoryId");
            if (category.ValueKind != JsonValueKind.Number || !(plan.AddedCategoryIds ?? []).Contains(category.GetInt64())) unexpectedAdded.Add(id);
        }
        if (changes.Added.Length > plan.MaxAdded) { violations.Add("Native added count exceeds the declared maximum; all added IDs require review."); unexpectedAdded.AddRange(changes.Added); }
        var unexpected = new CodeChanges(unexpectedAdded.Distinct().Order().ToArray(), changes.Modified.Except(allowed.Modified).Order().ToArray(), changes.Deleted.Except(allowed.Deleted).Order().ToArray());
        if (unexpected.Added.Length + unexpected.Modified.Length + unexpected.Deleted.Length > 0) violations.Add("Unexpected native changes were committed by the original action.");
        string status = verdicts.Any(v => v.Status == "error") ? "error" : verdicts.Any(v => v.Status != "passed") || violations.Count != 0 ? "failed" : "passed";
        return Report(status, status == "passed" ? "All intended-result checks and declared native-change limits passed." : "Review check diagnostics and native changes; the original action was not undone.")
            with { Checks = verdicts, ActualChanges = changes, AllowedChanges = allowed, UnexpectedChanges = unexpected, Violations = violations.ToArray() };
    }
    private ResultCheckVerdict EvaluateCheck(ResultCheck check, JsonElement row)
    {
        JsonElement? expected = check.ExpectedInput is { } name ? inputs.TryGetProperty(name, out var input) ? input : null : check.Expected;
        try
        {
            if (row.GetProperty("error") is var error && error.ValueKind != JsonValueKind.Null)
                return new(check.Name, "error", check.Comparison, expected, Error: error.GetString());
            var observed = row.GetProperty("observation"); bool exists = observed.GetProperty("exists").GetBoolean();
            JsonElement? actual = observed.TryGetProperty("value", out var v) ? v : null;
            bool passes = check.Comparison switch
            {
                "exists" => exists, "absent" => !exists,
                "equals" => exists && actual is { } a && expected is { } e && ResultChecks.Equal(a, e),
                "range" => exists && actual is { ValueKind: JsonValueKind.Number } number && number.TryGetDouble(out double n) && double.IsFinite(n)
                    && (check.Minimum is null || n >= check.Minimum) && (check.Maximum is null || n <= check.Maximum), _ => false
            };
            if (check.Comparison == "equals" && expected is null) throw new InvalidDataException("Expected input is missing.");
            return new(check.Name, passes ? "passed" : "failed", check.Comparison, expected, actual, exists);
        }
        catch (Exception error) { return new(check.Name, "error", check.Comparison, expected, Error: Short(error.Message)); }
    }
}
