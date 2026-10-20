using System.Text.Json;
using RevitThyme.Core;

namespace RevitThyme.McpTests;

internal sealed partial class ElectricalLabLive
{
    private long[] inspectionNativeAdded = [], inspectionKnownAdded = [];
    private long inspectionRootId;
    public static async Task InspectApplied(string folder, string request)
    {
        var driver = new ElectricalLabLive(folder); string id = OriginalGuid(request); var intent = driver.OriginalIntent(id);
        var evidence = Directory.GetFiles(folder, "resume-transcript-*.json").Append(Path.Combine(folder, "transcript.json"))
            .Append(Path.Combine(folder, "initial-inventory.json")).Append(Path.Combine(folder, "intent-" + id + ".json")).ToDictionary(path => path, Hash);
        var entries = evidence.Keys.Where(p => Path.GetFileName(p).Contains("transcript", StringComparison.Ordinal)).SelectMany(p => Read(p).EnumerateArray().Select(e => e.Clone())).ToArray();
        var original = entries.Single(e => e.TryGetProperty("phase", out var phase) && phase.GetString() == "returned" && e.GetProperty("name").GetString() == "run_modify"
            && e.GetProperty("args").GetProperty("requestId").GetString() == id).GetProperty("data");
        string body = intent.GetProperty("args").GetProperty("source").GetString()!;
        Program.Check("applied diagnostic pins exact original committed duplicate intent/source/result", intent.GetProperty("name").GetString() == "run_modify"
            && original.GetProperty("id").GetString() == id && original.GetProperty("outcome").GetString() == "Applied" && original.GetProperty("code").GetString() == "applied"
            && Digest(body) == original.GetProperty("execution").GetProperty("sourceSha256").GetString() && File.ReadAllText(Path.Combine(folder, "body-" + id + ".csx")) == body);
        var details = Value(original); var changes = original.GetProperty("execution").GetProperty("changes").Deserialize<CodeChanges>(Protocol.Json)!;
        driver.inspectionNativeAdded = changes.Added; driver.inspectionRootId = details.GetProperty("id").GetInt64();
        driver.inspectionKnownAdded = [driver.inspectionRootId, .. details.GetProperty("companions").EnumerateArray().Select(e => e.GetProperty("id").GetInt64())];
        Program.Check("applied inspection has bounded additions and no modified/deleted originals", changes.Added.Length is > 0 and <= 512 && changes.Modified.Length == 0 && changes.Deleted.Length == 0
            && changes.Added.Distinct().Count() == changes.Added.Length && driver.inspectionKnownAdded.Distinct().Count() == driver.inspectionKnownAdded.Length
            && driver.inspectionKnownAdded.All(changes.Added.Contains) && details.GetProperty("annotations").GetInt32() == 0
            && details.GetProperty("companions").EnumerateArray().All(e => !string.IsNullOrWhiteSpace(e.GetProperty("role").GetString())));
        driver.studyUid = details.GetProperty("uid").GetString()!; driver.qaName = details.GetProperty("name").GetString()!;
        driver.transcriptName = "inspect-applied-transcript-" + id + "-" + Guid.NewGuid() + ".json";
        string receipt = "inspect-applied-" + id + "-" + Guid.NewGuid() + ".json";
        try
        {
            var hashesBefore = driver.ValidatePreInventory(); var baseline = Read(Path.Combine(folder, "initial-inventory.json")).GetProperty("baseline");
            driver.observer = await driver.Client("original applied duplicate recovery"); var retained = await driver.Observe(id);
            Program.Check("original Applied duplicate recovers byte-exact without replay", retained.GetRawText() == original.GetRawText());
            driver.Save(receipt, new { diagnosticOnly = true, originalGuid = id, intent, original, retained, hashesBefore, evidence });
            driver.client = await driver.Client("read-only applied element inspection"); var before = await driver.Context();
            var originalSnapshot = original.GetProperty("snapshot").Deserialize<Snapshot>(Protocol.Json)!;
            Program.Check("applied inspection pins original modified document and active source view", before.Snapshot.IsModified && originalSnapshot.IsModified
                && before.Snapshot.Target.DocumentId == originalSnapshot.Target.DocumentId && before.Snapshot.Target.ViewId == baseline.GetProperty("sourceUid").GetString()
                && originalSnapshot.DocumentPath == driver.Text("a") && details.GetProperty("sourceUid").GetString() == baseline.GetProperty("sourceUid").GetString()
                && originalSnapshot.Target.ProcessId == driver.binding.ProcessId && originalSnapshot.Target.ProcessStart == driver.binding.ProcessStart && originalSnapshot.Target.SessionId == driver.binding.SessionId);
            var beforeInventory = await driver.Code("inventory.csx"); CheckOriginals(Value(beforeInventory)); driver.CheckStudy(Value(beforeInventory), baseline, 0);
            var inspection = await driver.Code("inspect-applied.csx");
            // Persist the evidence before any accounting assertion: an invisible/unknown Added ID is never accepted here.
            driver.Save(receipt, new { diagnosticOnly = true, originalGuid = id, intent, original, retained, beforeInventory, inspection, hashesBefore, evidence });
            var observed = Value(inspection);
            Program.Check("direct inspection reports every original native Added identity exactly once", observed.GetProperty("added").EnumerateArray().Select(e => e.GetProperty("id").GetInt64()).Order().SequenceEqual(changes.Added.Order()));
            Program.Check("collected original membership matches frozen baseline after excluding only previously classified identities", observed.GetProperty("originalCollectorFingerprint").GetString() == baseline.GetProperty("idFingerprint").GetString()
                && observed.GetProperty("originalCollectorCount").GetInt32() == baseline.GetProperty("elements").GetInt32());
            var afterInventory = await driver.Code("inventory.csx"); CheckOriginals(Value(afterInventory));
            Program.Check("read-only applied inspection preserves complete current inventory", Value(afterInventory).GetRawText() == Value(beforeInventory).GetRawText());
            var after = await driver.Context(); Program.Check("inspection retains exact modified state, document and active view", after.Snapshot.IsModified
                && after.Snapshot.Target.DocumentId == before.Snapshot.Target.DocumentId && after.Snapshot.Target.ViewId == before.Snapshot.Target.ViewId);
            var hashesAfter = driver.ValidatePreInventory(); Program.Check("applied inspection preserves original evidence", evidence.All(p => Hash(p.Key) == p.Value));
            driver.Save(receipt, new { diagnosticOnly = true, passed = true, originalGuid = id, intent, original, retained, beforeInventory, inspection, afterInventory,
                before = before.Snapshot, after = after.Snapshot, hashesBefore, hashesAfter, evidence, driver.transcriptName,
                boundary = "No duplicate replay, copy, save, activation or lifecycle continuation. Unclassified native Added identities remain pending review." });
            void CheckOriginals(JsonElement actual)
            {
                foreach (string key in new[] { "views", "templates", "counts", "wireCount", "sourceL1LightingAnnotationCount", "schedules", "sheet", "sourceUid", "sourceAnnotations" })
                    Program.Check("applied diagnostic preserves original canonical " + key, actual.GetProperty(key).GetRawText() == baseline.GetProperty(key).GetRawText());
            }
            Console.WriteLine("Electrical applied inspection receipt: " + Path.Combine(folder, receipt));
        }
        finally { if (driver.client is not null) await driver.client.DisposeAsync(); if (driver.observer is not null) await driver.observer.DisposeAsync(); }
    }
}
