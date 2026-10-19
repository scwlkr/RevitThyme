using System.Text.Json;

namespace RevitThyme.Core;

internal sealed partial class TimberRunStore
{
    internal void RequireSameReconciliation(Guid id, byte[] request)
    {
        string path = Path.Combine(DirectoryFor(id), "reconciliation-request.json");
        if (!File.Exists(path) || !ReadBounded(path, 16384).SequenceEqual(request))
            throw new OperationException("id_conflict", "Run ID was already used with different arguments.");
    }
    private TimberRunSummary ReadReconciliation(Guid id, bool verify)
    {
        string directory = DirectoryFor(id);
        try
        {
            var request = JsonSerializer.Deserialize<TimberReconciliationStart>(ReadBounded(Path.Combine(directory, "reconciliation-request.json"), 16384), Protocol.Json);
            var summary = JsonSerializer.Deserialize<TimberRunSummary>(ReadBounded(Path.Combine(directory, "run.json"), 65536), Protocol.Json);
            if (request?.Id != id || summary?.RunId != id || summary.Directory != directory || summary.Target != request.CurrentTarget ||
                summary.GenerationRunId != request.GenerationRunId || request.GenerationRunId == id)
                throw new InvalidDataException("Reconciliation identity differs from its ownership record.");
            if (summary.State == TimberRunState.KitReady && verify)
            {
                try { TimberReconciliationFiles.Verify(this, request, summary); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
                { return summary with { State = TimberRunState.Unconfirmed, Stage = "Reconciled kit evidence changed", Diagnostics = [new("run_evidence", TimberSeverity.Error, error.Message)] }; }
            }
            else if (!summary.Terminal || summary.State == TimberRunState.KitReady)
                return summary with { State = TimberRunState.Unconfirmed, Stage = "Reconciliation is not owned by this host; verify retained evidence" };
            return summary;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        { throw new OperationException("run_unconfirmed", "Reconciliation evidence could not be verified: " + error.Message); }
    }
}
