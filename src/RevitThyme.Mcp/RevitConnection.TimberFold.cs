using RevitThyme.Core;

namespace RevitThyme.Mcp;

public sealed partial class RevitConnection
{
    public async Task<OperationResult> TimberRun(Guid id, bool cancel, CancellationToken token)
    {
        if (id == Guid.Empty) throw new OperationException("invalid_id", "runId must be a non-empty GUID.");
        token.ThrowIfCancellationRequested();
        var chosen = RequireClient();
        return await chosen.ExecuteAsync(new(Guid.NewGuid(), cancel ? OperationKind.CancelTimberFoldRun : OperationKind.GetTimberFoldRun,
            OperationId: id), token);
    }
}
