using System.Security.Cryptography;
using System.Text.Json;

namespace RevitThyme.Core;

internal static class TimberInspectionFiles
{
    public static TimberInspectionEvidence Write(string root, Guid requestId, TimberSnapshot snapshot,
        TimberFoldProfile profile, TimberScope scope, TimberInspectionSummary summary)
    {
        if (!Path.IsPathFullyQualified(root)) throw new OperationException("invalid_evidence_root", "Host inspection storage must be an absolute path.");
        string directory = Path.Combine(root, requestId.ToString("D"));
        if (Directory.Exists(directory) || File.Exists(directory))
            throw new OperationException("inspection_exists", "Inspection evidence already exists. Recover the original request outcome before retrying.");
        byte[] source = JsonSerializer.SerializeToUtf8Bytes(new { schema = 1, profile, scope, snapshot }, Protocol.Json);
        byte[] diagnostics = JsonSerializer.SerializeToUtf8Bytes(TimberInspection.GetDiagnostics(snapshot, profile, scope), Protocol.Json);
        if (source.Length > 134217728 || diagnostics.Length > 16777216)
            throw new OperationException("inspection_limit", "Inspection exceeds the evidence size limit. Use a smaller explicit source scope.");
        var evidence = new TimberInspectionEvidence(directory, Hash(source), Hash(diagnostics));
        Directory.CreateDirectory(directory);
        WriteNew(Path.Combine(directory, "snapshot.json"), source);
        WriteNew(Path.Combine(directory, "diagnostics.json"), diagnostics);
        WriteNew(Path.Combine(directory, "inspection.json"), JsonSerializer.SerializeToUtf8Bytes(new
        {
            schema = 1, requestId, state = "Inspected", manufacturingSuccess = false,
            sourceFingerprint = snapshot.DocumentFingerprint, summary, evidence
        }, Protocol.Json));
        return evidence;
    }

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static void WriteNew(string path, byte[] bytes)
    { using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read); stream.Write(bytes); }
}
