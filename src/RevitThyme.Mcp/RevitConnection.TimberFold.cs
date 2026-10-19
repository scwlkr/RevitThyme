using RevitThyme.Core;

namespace RevitThyme.Mcp;

public sealed partial class RevitConnection
{
    public Task<OperationResult> TimberRun(Guid id, bool cancel, CancellationToken token) =>
        TimberRunRequest(id, cancel ? OperationKind.CancelTimberFoldRun : OperationKind.GetTimberFoldRun, token);

    public Task<OperationResult> VerifyTimberRun(Guid id, CancellationToken token) =>
        TimberRunRequest(id, OperationKind.VerifyTimberFoldRun, token);

    private async Task<OperationResult> TimberRunRequest(Guid id, OperationKind operation, CancellationToken token)
    {
        if (id == Guid.Empty) throw new OperationException("invalid_id", "runId must be a non-empty GUID.");
        token.ThrowIfCancellationRequested();
        var chosen = RequireClient();
        return await chosen.ExecuteAsync(new(Guid.NewGuid(), operation, OperationId: id), token);
    }
}
