using System.Text.Json;

namespace RevitThyme.Core;

public sealed record CodeRequest(string Source, bool Trusted = false, int BudgetMilliseconds = 10000);
public sealed record CodeDiagnostic(string Id, string Severity, int Line, int Column, string Message);
public sealed record CodeChanges(long[] Added, long[] Modified, long[] Deleted);
public sealed record CodeResult(string SourceSha256, long ElapsedMilliseconds, JsonElement? Value = null,
    CodeDiagnostic[]? Diagnostics = null, CodeChanges? Changes = null);
public interface IDynamicModel : IModel
{
    bool TrustedCodeEnabled { get; }
    OperationResult ExecuteCode(OperationRequest request);
}
public static class DynamicExecutor
{
    public const int MaxSourceBytes = 24576;
    public const int MaxValueBytes = 8192;
    public static OperationResult Execute(IModel model, OperationRequest request)
    {
        if (request.Target is null) return OperationResult.Reject(request, "target_required", "Inspect first; supply the exact captured target.");
        if (request.Name is not null || request.ExpectedName is not null || request.Query is not null || request.ElementIds is not null
            || request.Edits is not null || request.OperationId is not null || request.ParameterOffset != 0 || request.ExpectedRange is not null
            || request.Range is not null || request.RoomOptions is not null || request.RoomPreview is not null)
            return OperationResult.Reject(request, "invalid_request", "Only captured target and code arguments are accepted.");
        var before = model.Capture();
        if (request.Target != before.Target) return OperationResult.Reject(request, "stale_target", "Document, view or revision changed. Inspect again.");
        if (model is not IDynamicModel native || !native.TrustedCodeEnabled || request.Code?.Trusted != true)
            return OperationResult.Reject(request, "trusted_code_disabled", "Trusted code requires host opt-in and trusted=true. It has full process permissions; it is not sandboxed.");
        if (request.Code is not { Source: { } source } code || string.IsNullOrWhiteSpace(source)
            || System.Text.Encoding.UTF8.GetByteCount(source) > MaxSourceBytes || code.BudgetMilliseconds is < 100 or > 30000)
            return OperationResult.Reject(request, "invalid_code", "Use a C# method body up to 24 KiB and a cooperative budget of 100–30000 ms.");
        if (!before.CanModify) return OperationResult.Reject(request, "write_blocked", before.WriteBlock ?? "A rollback transaction is required, including for queries.");
        return native.ExecuteCode(request);
    }
}
