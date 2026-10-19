using System.Text.Json;
using System.IO;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using RevitThyme.Core;

namespace RevitThyme.Addin;

internal static class NativeTimberKitOwnership
{
    private static readonly Guid SchemaId = new("4ca4f8c4-0121-4d3b-807a-e0200485fb55");
    private static readonly Guid ApplicationId = new("B09CCF69-B916-40B7-8A57-5923E45915D6");
    private sealed record Tag(int Schema, Guid RunId, Guid PreviewRunId, string Role, string UniqueId, string ContentHash);
    private sealed record Evidence(TimberNativeKitReceipt Receipt, string SourcePath);

    internal static void TagElement(Element element, TimberNativeKitPlan plan, string role, string hash)
    {
        var schema = Schema.Lookup(SchemaId) ?? CreateSchema();
        var entity = new Entity(schema);
        entity.Set("Ownership", JsonSerializer.Serialize(new Tag(1, plan.RunId, plan.PreviewRunId, role, element.UniqueId, hash), Protocol.Json));
        element.SetEntity(entity);
    }
    internal static bool Matches(Element element, Guid runId)
    {
        var tag = Read(element);
        return tag is { Schema: 1, ContentHash.Length: 64 } && tag.RunId == runId && tag.UniqueId == element.UniqueId;
    }
    // Only exact tagged, view-specific CAD backed by a committed receipt is omitted.
    // Unknown/missing evidence remains visible to normal unsupported-geometry diagnostics.
    internal static HashSet<long> VerifiedAnnotationIds(Document doc, CancellationToken cancellationToken = default)
    {
        var result = new HashSet<long>(); var retained = new Dictionary<Guid, Evidence?>();
        foreach (var import in new FilteredElementCollector(doc).OfClass(typeof(ImportInstance)).Cast<ImportInstance>().Where(i => i.ViewSpecific))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var tag = Read(import);
            if (tag is not { Schema: 1 } || tag.UniqueId != import.UniqueId || tag.Role is not ("CutImport" or "ReferenceImport") ||
                tag.ContentHash is not { Length: 64 } || doc.GetElement(import.OwnerViewId) is not ViewDrafting owner || !Matches(owner, tag.RunId)) continue;
            if (!retained.TryGetValue(tag.RunId, out var evidence)) retained.Add(tag.RunId, evidence = LoadEvidence(tag.RunId, cancellationToken));
            if (evidence is null || !string.Equals(evidence.SourcePath, doc.PathName, StringComparison.OrdinalIgnoreCase)) continue;
            var receipt = evidence.Receipt;
            if (receipt.CreatedElements.Any(e => e.Id == import.Id.Value && e.UniqueId == import.UniqueId && e.Role == tag.Role) &&
                receipt.CreatedElements.Any(e => e.Id == owner.Id.Value && e.UniqueId == owner.UniqueId && e.Role == (tag.Role == "CutImport" ? "CutView" : "ReferenceView")))
                result.Add(import.Id.Value);
        }
        return result;
    }
    private static Evidence? LoadEvidence(Guid runId, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            string directory = NativeTimberKitFiles.RunDirectory(runId);
            NativeTimberKitFiles.RequirePlain(directory);
            var receipt = JsonSerializer.Deserialize<TimberNativeKitReceipt>(ReadBounded(Path.Combine(directory, "native-receipt.json")), Protocol.Json);
            if (receipt is not { State: TimberNativeKitState.Committed, SourcePreserved: true, NativeReadbackPassed: true } ||
                receipt.RunId != runId || receipt.CreatedElements.IsDefaultOrEmpty || receipt.PreparedReceiptSha256 is null) return null;
            string preparedPath = Path.Combine(directory, "native-prepared.json");
            if (NativeTimberKitFiles.Hash(preparedPath, cancellationToken) != receipt.PreparedReceiptSha256) return null;
            using var prepared = JsonDocument.Parse(ReadBounded(preparedPath));
            cancellationToken.ThrowIfCancellationRequested();
            return prepared.RootElement.GetProperty("runId").GetGuid() == runId && prepared.RootElement.GetProperty("sourcePath").GetString() is { } path
                ? new(receipt, path) : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or KeyNotFoundException)
        { return null; }
    }
    private static Tag? Read(Element element)
    {
        try
        {
            string? json = ReadOwnershipText(element);
            return json is null ? null : JsonSerializer.Deserialize<Tag>(json, Protocol.Json);
        }
        catch (Exception error) when (error is JsonException or InvalidDataException or Autodesk.Revit.Exceptions.ArgumentException or Autodesk.Revit.Exceptions.InvalidOperationException)
        { return null; }
    }
    internal static string? ReadOwnershipText(Element element)
    {
        var schema = Schema.Lookup(SchemaId);
        if (schema is null) return null;
        var entity = element.GetEntity(schema);
        if (!entity.IsValid()) return null;
        string json = entity.Get<string>("Ownership");
        if (json.Length > 16384) throw new InvalidDataException("Native ownership payload exceeds its size limit.");
        return json;
    }
    private static Schema CreateSchema()
    {
        var builder = new SchemaBuilder(SchemaId);
        builder.SetSchemaName("RevitThymeTimberKitOwnershipV1"); builder.SetVendorId("WLKR");
        builder.SetApplicationGUID(ApplicationId); builder.SetReadAccessLevel(AccessLevel.Public); builder.SetWriteAccessLevel(AccessLevel.Application);
        builder.AddSimpleField("Ownership", typeof(string));
        return builder.Finish();
    }
    private static byte[] ReadBounded(string path)
    {
        if ((System.IO.File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Ownership receipt uses a reparse point.");
        using var stream = System.IO.File.OpenRead(path);
        if (stream.Length > 33554432) throw new InvalidDataException("Ownership receipt exceeds its size limit.");
        var bytes = new byte[checked((int)stream.Length)]; stream.ReadExactly(bytes); return bytes;
    }
}
