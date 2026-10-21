using System.Security.Cryptography;
using System.Text.Json;

namespace RevitThyme.Core;

public sealed class SavedToolStore
{
    private readonly string root;
    public SavedToolStore(string? directory = null)
    {
        root = directory ?? Environment.GetEnvironmentVariable("REVITTHYME_TOOLS_DIRECTORY")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RevitThyme", "tools");
        if (!Path.IsPathFullyQualified(root)) throw new ArgumentException("Tool directory must be absolute.");
        root = Path.GetFullPath(root);
    }
    public SavedToolArtifact Save(string toolId, SavedToolDefinition definition)
    {
        string path = FilePath(toolId); SavedTools.Validate(definition);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(definition, Protocol.Json);
        Directory.CreateDirectory(root);
        string temporary = Path.Combine(root, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(bytes); stream.Flush(true); }
            try { File.Move(temporary, path, false); }
            catch (IOException) when (File.Exists(path))
            {
                var existing = Get(toolId);
                if (existing.Digest != Digest(bytes)) throw new IOException("Tool ID already exists with different content. Use a new ID.");
                return existing;
            }
            return new(toolId, Digest(bytes), definition);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public SavedToolArtifact Get(string toolId)
    {
        using var stream = new FileStream(FilePath(toolId), FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > SavedTools.MaxDefinitionBytes) throw new InvalidDataException("Saved tool exceeds 64 KiB.");
        byte[] bytes = new byte[(int)stream.Length]; stream.ReadExactly(bytes);
        var definition = JsonSerializer.Deserialize<SavedToolDefinition>(bytes, Protocol.Json) ?? throw new InvalidDataException("Tool definition missing.");
        SavedTools.Validate(definition);
        return new(toolId, Digest(bytes), definition);
    }
    private string FilePath(string toolId)
    {
        if (!Guid.TryParseExact(toolId, "D", out var id) || id == Guid.Empty || id.ToString() != toolId)
            throw new ArgumentException("toolId must be a lowercase nonempty GUID in D format.");
        return Path.Combine(root, toolId + ".json");
    }
    private static string Digest(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
