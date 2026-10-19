using System.Text.Json;

namespace RevitThyme.Core;

internal sealed partial class TimberRunStore
{
    private readonly string root;
    internal TimberRunStore(string root)
    {
        if (!Path.IsPathFullyQualified(root)) throw new ArgumentException("Run storage needs an absolute path.", nameof(root));
        this.root = Path.GetFullPath(root);
        Directory.CreateDirectory(this.root);
    }
    internal string DirectoryFor(Guid id) => Path.Combine(root, id.ToString("D"));
    internal bool Exists(Guid id) => Directory.Exists(DirectoryFor(id)) || File.Exists(DirectoryFor(id));
    internal TimberRunStart ReadRequest(Guid id) => JsonSerializer.Deserialize<TimberRunStart>(
        ReadBounded(Path.Combine(DirectoryFor(id), "request.json"), 16384), Protocol.Json)
        ?? throw new InvalidDataException("Retained request is missing.");
    internal void Claim(Guid id, byte[] request, TimberRunSummary summary, string requestName = "request.json")
    {
        if (requestName is not ("request.json" or "reconciliation-request.json")) throw new InvalidDataException("Invalid owned request name.");
        string temporary = Path.Combine(root, ".run-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            WriteNewFile(Path.Combine(temporary, requestName), request);
            WriteNewFile(Path.Combine(temporary, "run.json"), JsonSerializer.SerializeToUtf8Bytes(summary, Protocol.Json));
            WriteNewFile(Path.Combine(temporary, "producer.json"), TimberRunProvenance.Capture());
            Directory.Move(temporary, DirectoryFor(id));
        }
        finally
        {
            // Only these three exact files can exist in this privately owned sibling.
            if (Directory.Exists(temporary))
            {
                foreach (string file in new[] { requestName, "run.json", "producer.json" }) File.Delete(Path.Combine(temporary, file));
                Directory.Delete(temporary);
            }
        }
    }
    internal void RequireSameRequest(Guid id, byte[] request)
    {
        if (File.Exists(Path.Combine(DirectoryFor(id), "reconciliation-request.json")))
            throw new OperationException("id_conflict", "Run ID belongs to a reconciliation request.");
        byte[] retained = ReadBounded(Path.Combine(DirectoryFor(id), "request.json"), 16384);
        if (!retained.SequenceEqual(request)) throw new OperationException("id_conflict", "Run ID was already used with different arguments.");
    }
    internal TimberRunSummary Read(Guid id, bool verifyPreview = true)
    {
        string directory = DirectoryFor(id);
        if (!Exists(id)) throw new OperationException("not_found", "Run ID is not retained.");
        if (File.Exists(Path.Combine(directory, "reconciliation-request.json"))) return ReadReconciliation(id, verifyPreview);
        try
        {
            var request = JsonSerializer.Deserialize<TimberRunStart>(ReadBounded(Path.Combine(directory, "request.json"), 16384), Protocol.Json);
            var summary = JsonSerializer.Deserialize<TimberRunSummary>(ReadBounded(Path.Combine(directory, "run.json"), 65536), Protocol.Json);
            if (request?.Id != id || summary?.RunId != id || summary.Directory != directory || summary.Target != request.Target)
                throw new InvalidDataException("Retained run identity differs from its ownership record.");
            if (summary.State == TimberRunState.PreviewReady)
            {
                if (!verifyPreview) return summary with { State = TimberRunState.Unconfirmed,
                    Stage = "Run is not owned by this host; inspect status before retrying" };
                try { TimberPreviewPipeline.CheckRecovery(directory, request, summary); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
                { return summary with { State = TimberRunState.Unconfirmed, Stage = "Preview evidence changed",
                    Diagnostics = [new("run_evidence", TimberSeverity.Error, error.Message)] }; }
            }
            if (summary.State == TimberRunState.KitReady)
            {
                if (!verifyPreview) return summary with { State = TimberRunState.Unconfirmed,
                    Stage = "Run is not owned by this host; inspect status before retrying" };
                try { TimberGenerationFiles.CheckRecovery(directory, request, summary); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
                { return summary with { State = TimberRunState.Unconfirmed, Stage = "Digital kit evidence changed",
                    Diagnostics = [new("run_evidence", TimberSeverity.Error, error.Message)] }; }
            }
            return summary.Terminal ? summary : summary with { State = TimberRunState.Unconfirmed,
                Stage = "Run is not owned by this host; inspect retained evidence before retrying" };
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        { throw new OperationException("run_unconfirmed", "Retained run evidence could not be verified: " + error.Message); }
    }
    internal void WriteStatus(TimberRunSummary summary) => WriteContext(summary.RunId, "run.json", JsonSerializer.SerializeToUtf8Bytes(summary, Protocol.Json));
    internal void WriteMeasurements(Guid id, byte[] bytes) => WriteContext(id, "run-timings.json", bytes);
    private void WriteContext(Guid id, string name, byte[] bytes)
    {
        string directory = DirectoryFor(id), temporary = Path.Combine(directory, ".context-" + Guid.NewGuid().ToString("N"));
        try
        {
            WriteNewFile(temporary, bytes);
            File.Move(temporary, Path.Combine(directory, name), true);
        }
        finally { File.Delete(temporary); }
    }
    internal void WriteNew(Guid id, string name, ReadOnlySpan<byte> bytes) => WriteNewFile(Path.Combine(DirectoryFor(id), name), bytes);
    internal void PublishNew(Guid id, string name, ReadOnlySpan<byte> bytes)
    {
        string temporary = Path.Combine(DirectoryFor(id), ".publish-" + Guid.NewGuid().ToString("N"));
        try
        {
            WriteNewFile(temporary, bytes); // Flush before the success filename becomes visible.
            File.Move(temporary, Path.Combine(DirectoryFor(id), name), false);
        }
        finally { File.Delete(temporary); }
    }
    private static void WriteNewFile(string path, ReadOnlySpan<byte> bytes)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        stream.Write(bytes); stream.Flush(true);
    }
    private static byte[] ReadBounded(string path, int maximum)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length > maximum) throw new InvalidDataException("Retained run record exceeds its size limit.");
        byte[] bytes = new byte[(int)stream.Length]; stream.ReadExactly(bytes); return bytes;
    }
}
