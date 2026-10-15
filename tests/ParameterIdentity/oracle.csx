// Read-only direct native oracle. Replace ELEMENT_UNIQUE_ID and OFFSET before dev-query.
// Re-run pages with nextOffset until null. Each page includes the same full raw-state digest.
var elementUniqueId = "ELEMENT_UNIQUE_ID";
const int offset = 0; // OFFSET
const int pageSize = 10;
var element = doc.GetElement(elementUniqueId) ?? throw new Exception("Fixture element missing.");
var ordered = element.Parameters.Cast<Parameter>().OrderBy(p => p.Id.Value).ToArray();
if (offset < 0 || offset > ordered.Length) throw new Exception("Invalid oracle page offset.");
object Read(Parameter p, bool clipped)
{
    var raw = p.StorageType == StorageType.String ? p.AsString() ?? "" : null;
    var storage = p.StorageType.ToString();
    var text = raw == null ? null : clipped && raw.Length > 512 ? raw.Substring(0, 512) : raw;
    long? integer = p.StorageType == StorageType.Integer ? p.AsInteger() :
        p.StorageType == StorageType.ElementId ? p.AsElementId().Value : null;
    double? number = p.StorageType == StorageType.Double ? p.AsDouble() : null;
    return new
    {
        id = p.Id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), name = p.Definition.Name,
        dataType = p.Definition.GetDataType().TypeId,
        isReadOnly = p.IsReadOnly || p.StorageType == StorageType.ElementId || p.StorageType == StorageType.None,
        value = new { storage, text, integer, number }, truncated = raw != null && raw.Length > 512,
        isShared = p.IsShared, sharedGuid = p.IsShared ? (Guid?)p.GUID : null,
        builtInParameter = p.Definition is InternalDefinition definition && definition.BuiltInParameter != BuiltInParameter.INVALID
            ? definition.BuiltInParameter.ToString() : null,
        hasValue = p.HasValue
    };
}
var full = System.Text.Json.JsonSerializer.Serialize(ordered.Select(p => Read(p, false)).ToArray());
var digest = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(full))).ToLowerInvariant();
var count = new FilteredElementCollector(doc).WherePasses(new LogicalOrFilter(new ElementIsElementTypeFilter(), new ElementIsElementTypeFilter(true))).GetElementCount();
var executable = System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName;
return new
{
    elementUniqueId, parameterCount = ordered.Length, offset,
    nextOffset = offset + pageSize < ordered.Length ? (int?)(offset + pageSize) : null,
    parameters = ordered.Skip(offset).Take(pageSize).Select(p => Read(p, true)).ToArray(),
    allParameterIds = ordered.Select(p => p.Id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray(),
    nativeRawDigest = digest, elementCount = count, isModified = doc.IsModified,
    processId = Environment.ProcessId, documentPath = doc.PathName, documentTitle = doc.Title,
    revitVersion = uiapp.Application.VersionNumber, revitBuild = uiapp.Application.VersionBuild,
    executableBuild = System.Diagnostics.FileVersionInfo.GetVersionInfo(executable).FileVersion
};
