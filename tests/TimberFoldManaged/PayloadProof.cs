using System.Security.Cryptography;
using System.Text.Json;

namespace RevitThyme.TimberFold.Managed;

internal static class PayloadProof
{
    internal sealed record ManagedIdentity(string File, string Sha256, string Version);
    internal sealed record Proof(string InstalledDirectory, string PackageDirectory, string ManifestSha256, string Version,
        int FilesVerifiedPerRoot, ManagedIdentity[] ManagedAssemblies);
    internal static Proof Validate(string installed, string package, string expectedManifestSha256)
    {
        if (expectedManifestSha256.Length != 64 || !expectedManifestSha256.All(Uri.IsHexDigit)) throw new InvalidDataException("Expected manifest SHA256 must contain 64 hexadecimal digits.");
        installed = AbsoluteDirectory(installed); package = AbsoluteDirectory(package);
        if (string.Equals(installed, package, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Installed and package roots must be distinct.");
        string manifestPath = Path.Combine(installed, "manifest.json");
        if (!Hash(manifestPath).Equals(expectedManifestSha256, StringComparison.OrdinalIgnoreCase) ||
            !Hash(Path.Combine(package, "manifest.json")).Equals(expectedManifestSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Installed/package manifest does not match the explicitly pinned SHA256.");
        using var document = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
        var manifest = document.RootElement;
        var files = manifest.GetProperty("files").EnumerateObject().ToArray();
        if (files.Length == 0 || files.Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != files.Length)
            throw new InvalidDataException("Manifest file inventory is empty or has duplicate paths.");
        foreach (string root in new[] { installed, package })
        {
            var expected = files.Select(p => p.Name).Append("manifest.json").Order(StringComparer.Ordinal).ToArray();
            var actual = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Select(p => Path.GetRelativePath(root, p).Replace('\\', '/')).Order(StringComparer.Ordinal).ToArray();
            if (!actual.SequenceEqual(expected)) throw new InvalidDataException("Exact package file inventory differs: " + root);
            foreach (var entry in files)
            {
                if (entry.Name.Contains('\\') || entry.Name.StartsWith('/') || entry.Name.Contains(':') ||
                    entry.Name.Split('/').Any(p => p is "" or "." or "..")) throw new InvalidDataException("Unsafe manifest path: " + entry.Name);
                string hash = entry.Value.GetString() ?? "";
                if (hash.Length != 64 || !hash.All(Uri.IsHexDigit) || !Hash(Path.Combine(root, entry.Name)).Equals(hash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Package payload hash mismatch: " + Path.Combine(root, entry.Name));
            }
        }
        foreach (string name in new[] { "RevitThyme.Core.dll", "Clipper2Lib.dll", "IxMilia.Dxf.dll" })
        {
            if (!files.Any(p => p.Name == name)) throw new InvalidDataException("Required managed payload absent: " + name);
            foreach (string prefix in new[] { "cli/", "mcp/" })
                if (!Hash(Path.Combine(installed, name)).Equals(Hash(Path.Combine(installed, prefix + name)), StringComparison.Ordinal))
                    throw new InvalidDataException("Managed adapter payload differs from root binary: " + prefix + name);
        }
        var managed = new[]
        {
            new ManagedIdentity("Clipper2Lib.dll", manifest.GetProperty("files").GetProperty("Clipper2Lib.dll").GetString()!, manifest.GetProperty("clipper2").GetProperty("version").GetString()!),
            new ManagedIdentity("IxMilia.Dxf.dll", manifest.GetProperty("files").GetProperty("IxMilia.Dxf.dll").GetString()!, manifest.GetProperty("ixmilia_dxf").GetProperty("version").GetString()!),
            new ManagedIdentity("RevitThyme.Core.dll", manifest.GetProperty("files").GetProperty("RevitThyme.Core.dll").GetString()!, manifest.GetProperty("product_version").GetString()!)
        };
        return new(installed, package, expectedManifestSha256.ToUpperInvariant(), manifest.GetProperty("version").GetString()!, files.Length, managed);
    }
    internal static string Hash(string path)
    { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
    private static string AbsoluteDirectory(string path)
    {
        if (!Path.IsPathFullyQualified(path) || !Directory.Exists(path)) throw new DirectoryNotFoundException("Use an existing absolute payload directory: " + path);
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }
}
