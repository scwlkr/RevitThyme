using System.Security.Cryptography;
using System.Text.Json;

namespace RevitThyme.Addin;

public sealed record NativeTimberParameterStamp(long Id, string Storage, bool HasValue, string? Value);

public sealed record NativeTimberPreservationStamp(long Id, string UniqueId, string ContentHash, string RuntimeType,
    bool HasCategory, long TypeId, long OwnerViewId, bool ViewSpecific, int EntitySchemaCount);

// Pure boundary over actual native catalog/event observations; IDs are calibrated per run.
public static class NativeTimberKitPreservationCheck
{
    public static string ContentHash(string name, long typeId, IEnumerable<NativeTimberParameterStamp> parameters, string? ownershipJson = null)
    {
        var values = parameters.OrderBy(p => p.Id).Select(p => new { p.Id, p.Storage, p.HasValue, p.Value }).ToArray();
        // Keep untagged legacy source hashes stable. Tagged documentation additionally
        // binds the exact extensible-storage payload, including role/preview/hash fields.
        byte[] bytes = ownershipJson is null
            ? JsonSerializer.SerializeToUtf8Bytes(new { Name = name, TypeId = typeId, Parameters = values })
            : JsonSerializer.SerializeToUtf8Bytes(new { Name = name, TypeId = typeId, Parameters = values, TimberOwnership = ownershipJson });
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }
    public static bool Check(IEnumerable<NativeTimberPreservationStamp> before, IEnumerable<NativeTimberPreservationStamp> current,
        IEnumerable<long> modified, IEnumerable<long> deleted, IEnumerable<NativeTimberPreservationStamp> calibrated)
    {
        var original = before.ToArray(); var actual = current.ToArray(); var permitted = calibrated.ToArray();
        if (original.Select(e => e.Id).Distinct().Count() != original.Length || actual.Select(e => e.Id).Distinct().Count() != actual.Length ||
            permitted.Select(e => e.Id).Distinct().Count() != permitted.Length) return false;
        var now = actual.ToDictionary(e => e.Id); var old = original.ToDictionary(e => e.Id); var allowed = permitted.ToDictionary(e => e.Id);
        if (original.Any(e => !now.TryGetValue(e.Id, out var value) || e != value) || deleted.Any(old.ContainsKey)) return false;
        if (permitted.Any(e => !Eligible(e) || !old.TryGetValue(e.Id, out var value) || value != e)) return false;
        return modified.Where(old.ContainsKey).All(id => allowed.TryGetValue(id, out var value) && Eligible(now[id]) && value == now[id]);
    }
    public static bool Eligible(NativeTimberPreservationStamp stamp) => stamp.RuntimeType == "Autodesk.Revit.DB.Element" &&
        !stamp.HasCategory && stamp.TypeId == -1 && stamp.OwnerViewId == -1 && !stamp.ViewSpecific && stamp.EntitySchemaCount == 0;
}
