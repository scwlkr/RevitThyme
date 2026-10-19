using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace RevitThyme.Core;

internal sealed record TimberRunProducer(int Schema, string ProductVersion, int Protocol, string CoreSha256,
    string CoreModuleVersionId, string? BundleManifestSha256, string CreatedUtc);

internal static class TimberRunProvenance
{
    internal static byte[] Capture()
    {
        var assembly = typeof(TimberRunCoordinator).Assembly;
        string path = assembly.Location;
        string hash = Hash(path);
        string version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString() ?? throw new InvalidDataException("Producer version is unavailable.");
        var producer = new TimberRunProducer(1, version, Protocol.Version, hash,
            assembly.ManifestModule.ModuleVersionId.ToString("D"), ManifestHash(path, hash),
            DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        Validate(producer);
        return JsonSerializer.SerializeToUtf8Bytes(producer, Protocol.Json);
    }

    internal static TimberRunProducer Read(string directory)
    {
        string path = Path.Combine(directory, "producer.json");
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Producer record is redirected.");
        using var stream = File.OpenRead(path);
        if (stream.Length > 4096) throw new InvalidDataException("Producer record exceeds its size limit.");
        var producer = JsonSerializer.Deserialize<TimberRunProducer>(stream, Protocol.Json)
            ?? throw new InvalidDataException("Producer record is missing.");
        Validate(producer);
        return producer;
    }

    private static void Validate(TimberRunProducer producer)
    {
        if (producer.Schema != 1 || string.IsNullOrWhiteSpace(producer.ProductVersion) || producer.ProductVersion.Length > 256 ||
            producer.Protocol <= 0 || producer.Protocol > Protocol.Version || !ValidHash(producer.CoreSha256) ||
            !Guid.TryParseExact(producer.CoreModuleVersionId, "D", out var module) || module == Guid.Empty ||
            producer.BundleManifestSha256 is not null && !ValidHash(producer.BundleManifestSha256) ||
            !DateTimeOffset.TryParseExact(producer.CreatedUtc, "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out var created) ||
            created.Offset != TimeSpan.Zero) throw new InvalidDataException("Producer identity or creation time is invalid.");
    }

    private static string? ManifestHash(string assemblyPath, string coreHash)
    {
        string? folder = Path.GetDirectoryName(assemblyPath);
        for (int depth = 0; depth < 2 && folder is not null; depth++, folder = Path.GetDirectoryName(folder))
        {
            string path = Path.Combine(folder, "manifest.json");
            if (!File.Exists(path)) continue;
            using var stream = File.OpenRead(path);
            if (stream.Length > 1048576) throw new InvalidDataException("Installed producer manifest exceeds its size limit.");
            using var manifest = JsonDocument.Parse(stream);
            string relative = Path.GetRelativePath(folder, assemblyPath).Replace('\\', '/');
            if (!manifest.RootElement.TryGetProperty("files", out var files) || !files.TryGetProperty(relative, out var expected) ||
                expected.GetString() != coreHash) throw new InvalidDataException("Producer binary differs from its installed package manifest.");
            return Hash(path);
        }
        return null;
    }

    private static bool ValidHash(string value) => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static string Hash(string path)
    { using var stream = File.OpenRead(path); return Convert.ToHexStringLower(SHA256.HashData(stream)); }
}
