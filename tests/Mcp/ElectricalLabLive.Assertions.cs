using System.Text.Json;
using RevitThyme.Core;

namespace RevitThyme.McpTests;

internal sealed partial class ElectricalLabLive
{
    private void ValidateReference(JsonElement actual)
    {
        foreach (var expected in reference.GetProperty("views").EnumerateArray())
        {
            var view = actual.GetProperty("views").EnumerateArray().Single(v => v.GetProperty("id").GetInt64() == expected.GetProperty("viewId").GetInt64());
            var template = actual.GetProperty("templates").EnumerateArray().Single(v => v.GetProperty("id").GetInt64() == expected.GetProperty("templateId").GetInt64());
            Program.Check("native working view and independent display template match retained electrical reference", view.GetProperty("name").GetString() == expected.GetProperty("name").GetString()
                && !view.GetProperty("isTemplate").GetBoolean() && view.GetProperty("templateId").GetInt64() == -1
                && view.GetProperty("detail").GetString() == expected.GetProperty("detail").GetString() && view.GetProperty("scale").GetInt32() == expected.GetProperty("scale").GetInt32()
                && template.GetProperty("isTemplate").GetBoolean() && template.GetProperty("name").GetString() == expected.GetProperty("templateName").GetString());
            Program.Check("canonical owned annotation category counts match retained electrical reference", SameCounts(view.GetProperty("annotations"), expected.GetProperty("annotations")));
        }
        foreach (var expected in reference.GetProperty("counts").EnumerateArray())
            Program.Check("native electrical category quantity matches retained reference", actual.GetProperty("counts").EnumerateArray().Single(c => c.GetProperty("category").GetString() == expected.GetProperty("category").GetString())
                .GetProperty("count").GetInt32() == expected.GetProperty("expected").GetInt32());
        foreach (var expected in reference.GetProperty("schedules").EnumerateArray())
        {
            var schedule = actual.GetProperty("schedules").EnumerateArray().Single(s => s.GetProperty("id").GetInt64() == expected.GetProperty("id").GetInt64());
            Program.Check("native schedule identity and footer match retained reference", schedule.GetProperty("name").GetString() == expected.GetProperty("name").GetString()
                && schedule.GetProperty("footer").EnumerateArray().Select(c => c.GetString()).SequenceEqual(expected.GetProperty("footer").EnumerateArray().Select(c => c.GetString())));
        }
        Program.Check("native wires and original lighting annotation inventory match retained reference", actual.GetProperty("wireCount").GetInt32() == reference.GetProperty("wireCount").GetInt32()
            && actual.GetProperty("sourceL1LightingAnnotationCount").GetInt32() == reference.GetProperty("sourceL1LightingAnnotationCount").GetInt32());
        Log(new { name = "historical element-count comparison", historicalNonTypeElements = reference.GetProperty("elements"), actualNonTypeElements = actual.GetProperty("nonTypeElements"),
            actualIncludingTypes = actual.GetProperty("elements"), boundary = "Membership delta uses fresh native all-ID fingerprint; historical non-type count is recorded separately." });
    }
    private static bool SameCounts(JsonElement left, JsonElement right) => left.EnumerateObject().Count() == right.EnumerateObject().Count()
        && left.EnumerateObject().All(p => right.TryGetProperty(p.Name, out var expected) && p.Value.GetInt32() == expected.GetInt32());
    private void AssertCanonical(JsonElement baseline, JsonElement actual)
    {
        foreach (string key in new[] { "views", "templates", "counts", "wireCount", "sourceL1LightingAnnotationCount", "schedules", "sheet", "sourceUid", "sourceAnnotations" })
            Program.Check("canonical electrical " + key + " remains unchanged", actual.GetProperty(key).GetRawText() == baseline.GetProperty(key).GetRawText());
        Program.Check("native element membership changes are exactly the reported additions", actual.GetProperty("idFingerprint").GetString() == baseline.GetProperty("idFingerprint").GetString()
            && actual.GetProperty("elements").GetInt32() == baseline.GetProperty("elements").GetInt32() + added.Count);
    }
    private void CheckStudy(JsonElement actual, JsonElement baseline, int count)
    {
        var study = actual.GetProperty("study"); var source = actual.GetProperty("views")[0];
        Program.Check("owned study has exact identity, independent display and source settings", study.GetProperty("uid").GetString() == studyUid && study.GetProperty("name").GetString() == qaName
            && study.GetProperty("templateId").GetInt64() == -1 && study.GetProperty("detail").GetString() == source.GetProperty("detail").GetString()
            && study.GetProperty("scale").GetInt32() == source.GetProperty("scale").GetInt32() && study.GetProperty("settings").GetString() == source.GetProperty("settings").GetString());
        var owned = study.GetProperty("annotations").EnumerateArray().ToArray();
        Program.Check("study contains exactly bounded independent native annotations", owned.Length == count && owned.All(e => e.GetProperty("owner").GetInt64() == study.GetProperty("id").GetInt64()
            && e.GetProperty("group").GetInt64() == -1 && e.GetProperty("dependencies").GetArrayLength() == 0));
        if (count == 0) return;
        foreach (var original in baseline.GetProperty("sourceAnnotations").EnumerateArray())
            Program.Check("study annotation retains exact source type, position and native basis orientation", owned.Count(e => e.GetProperty("type").GetString() == original.GetProperty("type").GetString()
                && SameComponents(e.GetProperty("position"), original.GetProperty("position"), 3)
                && SameComponents(e.GetProperty("orientation"), original.GetProperty("orientation"), 9)) == 1);
    }
    private static bool SameComponents(JsonElement left, JsonElement right, int count) => left.ValueKind == JsonValueKind.Array && right.ValueKind == JsonValueKind.Array
        && left.GetArrayLength() == count && right.GetArrayLength() == count
        && left.EnumerateArray().Select(v => v.GetDouble()).Zip(right.EnumerateArray().Select(v => v.GetDouble())).All(p => Math.Abs(p.First - p.Second) < 1e-9);
    private async Task ActivateView(string uid)
    {
        var context = await Context(); await Call("activate_view", new() { ["captureId"] = context.Id, ["requestId"] = Guid.NewGuid().ToString(), ["viewId"] = uid }, "view_activated");
        Program.Check("typed view activation selects exact native UniqueId in A", (await Context()).Snapshot.Target.ViewId == uid);
    }
    private async Task Export(string uid, string label)
    {
        await ActivateView(uid); var context = await Context(); var result = await Call("export_view_image", new() { ["captureId"] = context.Id,
            ["requestId"] = Guid.NewGuid().ToString(), ["viewId"] = uid, ["pixelSize"] = 1600 }, "image_exported");
        Program.Check("electrical export verifies requested native view without model changes", result.GetProperty("imageExport").GetProperty("viewId").GetString() == uid
            && (await Context()).Snapshot == context.Snapshot); Save("preview-" + label + ".json", result);
    }
    private async Task RecoverExact(JsonElement original)
    {
        var result = await Call("get_operation", new() { ["requestId"] = original.GetProperty("id").GetString() }, original.GetProperty("code").GetString());
        Program.Check("completed electrical edit/save recovery is byte-exact without replay", result.GetRawText() == original.GetRawText());
    }
    private async Task Inventory(string capture, string[] ids)
    {
        var data = await Call("list_documents", new() { ["captureId"] = capture, ["requestId"] = Guid.NewGuid().ToString() }, "inspected");
        var page = data.GetProperty("documents").Deserialize<DocumentPage>(Protocol.Json)!;
        Program.Check("native session contains only exact owned A/B identities", page.Total == ids.Length && page.NextOffset is null
            && page.Items.Select(i => i.DocumentId).Order().SequenceEqual(ids.Order()) && page.Items.All(i => i.Path == Text("a") || i.Path == Text("b")));
    }
}
