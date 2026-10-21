using System.Text.Json;
using RevitThyme.Core;

namespace RevitThyme.Mcp;

public sealed partial class RevitConnection
{
    public async Task<OperationResult> CheckedTool(string captureId, OperationRequest request, ResultCheckPlan plan, JsonElement inputs, CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            var chosen = RequireClient();
            if (!captures.TryGetValue(captureId, out var snapshot)) throw new OperationException("capture_missing", "Call get_context in this connection first.");
            var coordinator = new ResultCheckCoordinator(chosen, request.Id, plan, inputs, ResultChecks.Hash(request.Code!.Source), true, request.Code.BudgetMilliseconds);
            return await coordinator.Run(request with { Target = snapshot.Target }, token);
        }
        finally { gate.Release(); }
    }
    public async Task<ResultCheckPreparation> PrepareChecks(string captureId, Guid id, ResultCheckPlan plan, JsonElement inputs, string sourceSha256, bool trusted, int budget, CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            var chosen = RequireClient();
            if (!captures.TryGetValue(captureId, out var snapshot)) throw new OperationException("capture_missing", "Call get_context in this connection first.");
            return await new ResultCheckCoordinator(chosen, id, plan, inputs, sourceSha256, trusted, budget).Prepare(snapshot.Target, token);
        }
        finally { gate.Release(); }
    }
    public async Task<OperationResult> VerifyChecks(Guid id, ResultCheckPlan plan, JsonElement inputs, string sourceSha256, bool trusted, int budget, CancellationToken token)
    {
        await gate.WaitAsync(token);
        try { return await new ResultCheckCoordinator(RequireClient(), id, plan, inputs, sourceSha256, trusted, budget).Verify(token); }
        finally { gate.Release(); }
    }
}
