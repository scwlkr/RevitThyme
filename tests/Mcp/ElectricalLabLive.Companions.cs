using System.Text.Json;
using RevitThyme.Core;

namespace RevitThyme.McpTests;

internal sealed partial class ElectricalLabLive
{
    private string? duplicateIdentity;
    private readonly List<JsonElement> duplicateInspections = [];
    private async Task ValidateCommittedDuplicate(JsonElement original)
    {
        var details = Value(original); var changes = original.GetProperty("execution").GetProperty("changes").Deserialize<CodeChanges>(Protocol.Json)!;
        Program.Check("committed duplicate native additions are distinct, bounded and deletion-free", changes.Added.Length is > 0 and <= 512
            && changes.Added.Distinct().Count() == changes.Added.Length && changes.Deleted.Length == 0);
        studyUid = details.GetProperty("uid").GetString()!; qaName = details.GetProperty("name").GetString()!;
        inspectionRootId = details.GetProperty("id").GetInt64(); inspectionNativeAdded = changes.Added;
        inspectionKnownAdded = [inspectionRootId, .. details.GetProperty("companions").EnumerateArray().Select(e => e.GetProperty("id").GetInt64())];
        var inspection = await Code("inspect-applied.csx"); var data = Value(inspection); var roles = ClassifyCommitted(data);
        foreach (var known in details.GetProperty("companions").EnumerateArray())
        {
            var current = data.GetProperty("added").EnumerateArray().Single(e => e.GetProperty("id").GetInt64() == known.GetProperty("id").GetInt64());
            Program.Check("postcommit companion preserves original classified UID and role", current.GetProperty("uid").GetString() == known.GetProperty("uid").GetString()
                && roles[known.GetProperty("id").GetInt64()] == known.GetProperty("role").GetString());
        }
        if (details.TryGetProperty("pendingPrecommitDependencies", out var pending))
            Program.Check("pending precommit dependencies are accounted by actual native additions", pending.EnumerateArray().All(e => changes.Added.Contains(e.GetInt64())));
        // No native Added identity is admitted to either set until every direct postcommit object has a bounded role.
        foreach (var row in data.GetProperty("added").EnumerateArray())
        {
            long value = row.GetProperty("id").GetInt64(); Program.Check("postcommit classified native identity is fresh", added.Add(value));
            if (row.GetProperty("collectorMembership").GetBoolean()) Program.Check("postcommit collected identity is fresh", collectedAdded.Add(value));
        }
        inspectionKnownAdded = collectedAdded.Order().ToArray(); duplicateIdentity = CompanionIdentity(data, roles); duplicateInspections.Add(inspection);
        Save("committed-duplicate-classification-" + original.GetProperty("id").GetString() + ".json", new { original, inspection, roles, duplicateIdentity, nativeAddedIds = added.Order(), collectedAddedIds = collectedAdded.Order() });
    }
    private Dictionary<long, string> ClassifyCommitted(JsonElement data)
    {
        var rows = data.GetProperty("added").EnumerateArray().ToArray();
        Program.Check("postcommit direct read covers every native Added identity exactly once", rows.Select(e => e.GetProperty("id").GetInt64()).Order().SequenceEqual(inspectionNativeAdded.Order()));
        long Number(JsonElement row, string key) => row.GetProperty(key).ValueKind == JsonValueKind.Number ? row.GetProperty(key).GetInt64() : long.MinValue;
        bool Flag(JsonElement row, string key) => row.GetProperty(key).GetBoolean();
        string? TextOf(JsonElement row, string key) => row.GetProperty(key).GetString();
        var sun = rows.Single(e => Number(e, "id") == Number(data, "sunSettingsId"));
        var roles = new Dictionary<long, string>();
        foreach (var row in rows)
        {
            Program.Check("every native Added object is accessible, persistent and non-type", row.TryGetProperty("exists", out var exists) && exists.GetBoolean()
                && !Flag(row, "isElementType") && !Flag(row, "isTransient") && row.GetProperty("nameError").ValueKind == JsonValueKind.Null);
            long id = Number(row, "id"), owner = Number(row, "ownerViewId"), type = Number(row, "typeId"); string? kind = TextOf(row, "kind"), role = null;
            bool dependent = Flag(row, "studyDependent"), owned = owner == inspectionRootId, generic = kind == "Autodesk.Revit.DB.Element", specific = Flag(row, "viewSpecific");
            if (id == inspectionRootId && kind == "Autodesk.Revit.DB.ViewPlan" && TextOf(row, "uid") == studyUid && TextOf(row, "name") == qaName && Flag(row, "collectorMembership")) role = "study view";
            else if (kind == "Autodesk.Revit.DB.SketchPlane" && id == Number(data, "sketchPlaneId")) role = "exact study sketch plane";
            else if (kind == "Autodesk.Revit.DB.SketchPlane" && owned && dependent) role = "owned dependent sketch plane";
            else if (generic && row.GetProperty("categoryId").ValueKind == JsonValueKind.Null && dependent && owner == -1 && !specific && TextOf(row, "name") == qaName
                && (type == -1 || type == Number(data, "sourceTypeId"))) role = "named dependent view structure";
            else if (generic && row.GetProperty("categoryId").ValueKind == JsonValueKind.Null && owned && dependent && specific && type == -1 && TextOf(row, "name") == "ExtentElem") role = "owned dependent view extent";
            else if (kind == "Autodesk.Revit.DB.SunAndShadowSettings" && id == Number(data, "sunSettingsId") && owned && dependent && specific && type == -1) role = "exact study sun and shadow settings";
            else if (generic && Number(row, "categoryId") == Number(sun, "categoryId") && TextOf(row, "categoryType") == "Model" && owned && dependent && specific && type == -1) role = "owned dependent sun path structure";
            else if (generic && Number(row, "categoryId") == -2000055 && TextOf(row, "categoryType") == "Internal" && dependent && owner == -1 && !specific && type == -1) role = "dependent internal work plane grid";
            else if (kind == "Autodesk.Revit.DB.Viewport" && Number(row, "viewportViewId") == inspectionRootId && Number(row, "viewportSheetId") == -1
                && owner == -1 && !specific && dependent && TextOf(row, "categoryType") == "Annotation" && type != -1 && Flag(row, "typeIsElementType")) role = "unplaced viewport for exact study";
            Program.Check("native Added identity has an independently bounded postcommit role", role != null); roles.Add(id, role!);
        }
        return roles;
    }
    private static string CompanionIdentity(JsonElement data, Dictionary<long, string> roles) => JsonSerializer.Serialize(data.GetProperty("added").EnumerateArray().OrderBy(e => e.GetProperty("id").GetInt64())
        .Select(e => new { role = roles[e.GetProperty("id").GetInt64()], metadata = e.EnumerateObject().Where(p => p.Name != "dependentIds").ToDictionary(p => p.Name, p => p.Value) }), Protocol.Json);
    private async Task VerifyDuplicateIdentity(string stage)
    {
        inspectionKnownAdded = collectedAdded.Order().ToArray();
        var inspection = await Code("inspect-applied.csx"); var data = Value(inspection); var roles = ClassifyCommitted(data);
        Program.Check("all direct duplicate IDs, UIDs, roles and viewport associations persist " + stage, duplicateIdentity != null && CompanionIdentity(data, roles) == duplicateIdentity);
        duplicateInspections.Add(inspection);
    }
}
