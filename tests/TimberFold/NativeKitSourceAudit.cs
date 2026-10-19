using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;

namespace RevitThyme.TimberFold.Fixtures;

// Independent observer: reconstructs retained catalog JSON from native values, without product calls.
internal static class NativeKitSourceAudit
{
    internal static bool ContentUnchanged(Document doc, JsonElement catalog, CancellationToken ct)
    {
        foreach (var retained in catalog.EnumerateArray())
        {
            ct.ThrowIfCancellationRequested();
            var element = doc.GetElement(new ElementId(Get(retained, "id").GetInt64()));
            if (element is null || element.UniqueId != Get(retained, "uniqueId").GetString() || ContentHash(element) != Get(retained, "contentHash").GetString()) return false;
        }
        return true;
    }
    internal static bool Calibration(Document doc, JsonElement proof, JsonElement catalog)
    {
        if (!Get(proof, "rollbackVerified").GetBoolean() || Get(proof, "transaction").GetString() != "TimberFold documentation bookkeeping calibration" ||
            Get(proof, "method").GetString() != "Controlled ViewDrafting.Create+Scale1; committed inner transaction; checked TransactionGroup rollback") return false;
        var before = catalog.EnumerateArray().ToDictionary(e => Get(e, "id").GetInt64());
        var allowed = Get(proof, "allowed").EnumerateArray().ToArray();
        if (allowed.Select(e => Get(e, "id").GetInt64()).Distinct().Count() != allowed.Length) return false;
        foreach (var item in allowed)
        {
            long id = Get(item, "id").GetInt64(); var e = doc.GetElement(new ElementId(id));
            if (e is null || !before.TryGetValue(id, out var original) || e.UniqueId != Get(item, "uniqueId").GetString() ||
                Get(original, "uniqueId").GetString() != e.UniqueId || Get(original, "contentHash").GetString() != Get(item, "contentHash").GetString() ||
                ContentHash(e) != Get(item, "contentHash").GetString() || e.GetType() != typeof(Element) || e.Category is not null ||
                e.GetTypeId() != ElementId.InvalidElementId || e.OwnerViewId != ElementId.InvalidElementId || e.ViewSpecific || e.GetEntitySchemaGuids().Count != 0 ||
                Get(item, "runtimeType").GetString() != "Autodesk.Revit.DB.Element" || Get(item, "hasCategory").GetBoolean() ||
                Get(item, "typeId").GetInt64() != -1 || Get(item, "ownerViewId").GetInt64() != -1 || Get(item, "viewSpecific").GetBoolean() ||
                Get(item, "entitySchemaCount").GetInt32() != 0) return false;
        }
        return true;
    }
    private static string ContentHash(Element element)
    {
        var parameters = element.Parameters.Cast<Parameter>().OrderBy(p => p.Id.Value).Select(p => new
        {
            Id = p.Id.Value, Storage = p.StorageType.ToString(), p.HasValue, Value = p.StorageType switch
            {
                StorageType.Double => p.AsDouble().ToString("R", CultureInfo.InvariantCulture),
                StorageType.Integer => p.AsInteger().ToString(CultureInfo.InvariantCulture),
                StorageType.ElementId => p.AsElementId().Value.ToString(CultureInfo.InvariantCulture),
                StorageType.String => p.AsString(), _ => null
            }
        }).ToArray();
        string? ownership = null;
        var schema = Schema.Lookup(new Guid("4ca4f8c4-0121-4d3b-807a-e0200485fb55"));
        if (schema is not null)
        {
            var entity = element.GetEntity(schema);
            if (entity.IsValid()) ownership = entity.Get<string>("Ownership");
            if (ownership is { Length: > 16384 }) throw new InvalidDataException("Native ownership payload exceeds its independent observation bound.");
        }
        byte[] bytes = ownership is null
            ? JsonSerializer.SerializeToUtf8Bytes(new { element.Name, TypeId = element.GetTypeId().Value, Parameters = parameters })
            : JsonSerializer.SerializeToUtf8Bytes(new { element.Name, TypeId = element.GetTypeId().Value, Parameters = parameters, TimberOwnership = ownership });
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }
    private static JsonElement Get(JsonElement value, string name) => value.EnumerateObject()
        .Single(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)).Value;
}
