namespace RevitThyme.TimberFold.Managed;

// Independent of product receipts and hash helpers: compare every actual relative
// file and its bytes, then retain SHA256/length as a portable comparison inventory.
internal static class CanonicalFiles
{
    internal sealed record Entry(string Name, long Bytes, string Sha256);
    internal static Entry[] Compare(string first, string second)
    {
        var a = Inventory(first); var b = Inventory(second);
        if (!a.SequenceEqual(b)) throw new InvalidDataException("Independent canonical file/hash inventories differ between the two runs.");
        foreach (var entry in a)
            if (!File.ReadAllBytes(Path.Combine(first, entry.Name)).SequenceEqual(File.ReadAllBytes(Path.Combine(second, entry.Name))))
                throw new InvalidDataException("Canonical file bytes differ: " + entry.Name);
        if (a.Length == 0 || !a.Any(p => p.Name.EndsWith(".dxf", StringComparison.Ordinal)) || !a.Any(p => p.Name.EndsWith(".svg", StringComparison.Ordinal)))
            throw new InvalidDataException("Canonical comparison requires actual DXF and SVG outputs.");
        return a;
    }
    private static Entry[] Inventory(string root) => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
        .Select(p => new Entry(Path.GetRelativePath(root, p).Replace('\\', '/'), new FileInfo(p).Length, PayloadProof.Hash(p)))
        .OrderBy(p => p.Name, StringComparer.Ordinal).ToArray();
}
