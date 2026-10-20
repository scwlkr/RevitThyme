using System.Text.Json;
using RevitThyme.Core;

namespace RevitThyme.McpTests;

internal sealed partial class ElectricalLabLive
{
    private Dictionary<string, string>? continuationEvidence;
    public static async Task ContinueApplied(string folder, string request)
    {
        var driver = new ElectricalLabLive(folder); string id = OriginalGuid(request); var intent = driver.OriginalIntent(id);
        if (File.Exists(Path.Combine(folder, "complete.json"))) throw new InvalidDataException("Completed workflow cannot be continued.");
        var evidence = Directory.GetFiles(folder, "*transcript*.json").Append(Path.Combine(folder, "initial-inventory.json"))
            .Append(Path.Combine(folder, "intent-" + id + ".json")).Append(Path.Combine(folder, "body-" + id + ".csx")).ToDictionary(path => path, Hash);
        var entries = evidence.Keys.Where(p => Path.GetFileName(p).Contains("transcript", StringComparison.Ordinal)).SelectMany(p => Read(p).EnumerateArray().Select(e => e.Clone())).ToArray();
        bool Submitted(JsonElement e) => e.TryGetProperty("phase", out var p) && p.GetString() == "submitted";
        var submission = entries.Single(e => Submitted(e) && e.GetProperty("name").GetString() == "run_modify" && e.GetProperty("args").GetProperty("requestId").GetString() == id);
        DateTime boundary = submission.GetProperty("utc").GetDateTime();
        if (entries.Where(Submitted).Count(e => e.GetProperty("name").GetString() == "run_modify") != 1
            || entries.Where(Submitted).Any(e => e.GetProperty("name").GetString() is not ("get_context" or "get_operation" or "list_documents" or "list_operations" or "open_document" or "activate_document" or "activate_view" or "export_view_image" or "run_query" or "run_modify"))
            || entries.Where(Submitted).Any(e => e.GetProperty("utc").GetDateTime() > boundary && e.GetProperty("name").GetString() is not ("get_context" or "get_operation" or "list_documents" or "run_query")))
            throw new InvalidDataException("Continue accepts only the first Applied duplicate with no subsequent model/view/file/lifecycle writes in any transcript.");
        var original = entries.Single(e => e.TryGetProperty("phase", out var p) && p.GetString() == "returned" && e.GetProperty("name").GetString() == "run_modify"
            && e.GetProperty("args").GetProperty("requestId").GetString() == id).GetProperty("data");
        string source = intent.GetProperty("args").GetProperty("source").GetString()!;
        Program.Check("continuation pins original Applied duplicate intent/body/source and bounded exact result", intent.GetProperty("name").GetString() == "run_modify"
            && original.GetProperty("id").GetString() == id && original.GetProperty("outcome").GetString() == "Applied" && original.GetProperty("code").GetString() == "applied"
            && Digest(source) == original.GetProperty("execution").GetProperty("sourceSha256").GetString() && File.ReadAllText(Path.Combine(folder, "body-" + id + ".csx")) == source
            && JsonSerializer.Serialize(submission.GetProperty("args")) == JsonSerializer.Serialize(intent.GetProperty("args")));
        var changes = original.GetProperty("execution").GetProperty("changes").Deserialize<CodeChanges>(Protocol.Json)!;
        Program.Check("existing duplicate changed no original native IDs", changes.Modified.Length == 0 && changes.Deleted.Length == 0);
        var baseline = Read(Path.Combine(folder, "initial-inventory.json")).GetProperty("baseline");
        Program.Check("Applied continuation retains the frozen initial historical reference", JsonSerializer.Serialize(Read(Path.Combine(folder, "initial-inventory.json")).GetProperty("reference")) == JsonSerializer.Serialize(driver.reference));
        var initial = entries.Where(e => e.TryGetProperty("phase", out var p) && p.GetString() == "returned" && e.GetProperty("name").GetString() == "run_query"
            && e.GetProperty("data").GetProperty("code").GetString() == "inspected" && Value(e.GetProperty("data")).GetRawText() == baseline.GetRawText())
            .OrderBy(e => entries.Single(s => Submitted(s) && s.GetProperty("name").GetString() == "run_query"
                && s.GetProperty("args").GetProperty("requestId").GetString() == e.GetProperty("args").GetProperty("requestId").GetString()).GetProperty("utc").GetDateTime()).First().GetProperty("data");
        string initialId = initial.GetProperty("id").GetString()!; var initialIntent = driver.OriginalIntent(initialId);
        string initialSource = initialIntent.GetProperty("args").GetProperty("source").GetString()!;
        Program.Check("initial inventory remains bound to its exact successful original query source", initialIntent.GetProperty("name").GetString() == "run_query"
            && initial.GetProperty("outcome").GetString() == "Inspected" && Digest(initialSource) == initial.GetProperty("execution").GetProperty("sourceSha256").GetString()
            && File.ReadAllText(Path.Combine(folder, "body-" + initialId + ".csx")) == initialSource && Read(Path.Combine(folder, "result-" + initialId + ".json")).GetRawText() == initial.GetRawText());
        foreach (string file in new[] { "intent-" + initialId + ".json", "body-" + initialId + ".csx", "result-" + initialId + ".json" }) evidence.Add(Path.Combine(folder, file), Hash(Path.Combine(folder, file)));
        JsonElement Opened(string path) => entries.Single(e => e.TryGetProperty("phase", out var p) && p.GetString() == "returned" && e.GetProperty("name").GetString() == "open_document"
            && e.GetProperty("args").GetProperty("path").GetString() == path && e.GetProperty("data").GetProperty("code").GetString() == "document_opened").GetProperty("data");
        var openA = Opened(driver.Text("a")); var openB = Opened(driver.Text("b")); string aid = DocumentId(openA), bid = DocumentId(openB);
        driver.transcriptName = "continue-applied-transcript-" + id + "-" + Guid.NewGuid() + ".json";
        driver.continuationEvidence = evidence;
        string receipt = "continue-applied-" + id + "-" + Guid.NewGuid() + ".json";
        try
        {
            var hashesBefore = driver.ValidatePreInventory(); driver.observer = await driver.Client("Applied duplicate continuation recovery");
            var retained = await driver.Observe(id); Program.Check("continuation recovers the original Applied duplicate byte-exact without replay", retained.GetRawText() == original.GetRawText());
            Program.Check("continuation recovers frozen first native inventory byte-exact", (await driver.Observe(initialId)).GetRawText() == initial.GetRawText());
            foreach (var open in new[] { openA, openB })
            {
                string path = open.GetProperty("documentLifecycle").GetProperty("path").GetString()!;
                Program.Check("original open pins completed owned path and original file hash", open.GetProperty("outcome").GetString() == "Completed" && (path == driver.Text("a") || path == driver.Text("b"))
                    && open.GetProperty("documentLifecycle").GetProperty("sha256").GetString() == driver.Text(path == driver.Text("a") ? "sourceSha256" : "bSourceSha256"));
                Program.Check("continuation recovers original typed opens byte-exact", (await driver.Observe(open.GetProperty("id").GetString()!)).GetRawText() == open.GetRawText());
            }
            driver.client = await driver.Client("continue existing Applied duplicate"); var context = await driver.Context();
            var old = original.GetProperty("snapshot").Deserialize<Snapshot>(Protocol.Json)!;
            Program.Check("continuation pins original modified A at exact source view/process/session", context.Snapshot.IsModified && old.IsModified && context.Snapshot.Target.DocumentId == aid
                && context.Snapshot.Target.ViewId == baseline.GetProperty("sourceUid").GetString() && old.Target.DocumentId == aid && old.DocumentPath == driver.Text("a")
                && old.Target.ProcessId == driver.binding.ProcessId && old.Target.ProcessStart == driver.binding.ProcessStart && old.Target.SessionId == driver.binding.SessionId);
            await driver.Inventory(context.Id, [aid, bid]); driver.ValidateReference(baseline);
            await driver.ValidateCommittedDuplicate(original);
            var beforeProbe = await driver.Code("inventory.csx"); driver.AssertCanonical(baseline, Value(beforeProbe)); driver.CheckStudy(Value(beforeProbe), baseline, 0);
            string existingName = driver.qaName; JsonElement probe;
            try { driver.qaName = "RevitThyme Electrical QA query " + Guid.NewGuid().ToString("N"); probe = await driver.Code("duplicate.csx"); }
            finally { driver.qaName = existingName; }
            Program.Check("updated duplicate body produced a distinct query-only temporary view", probe.GetProperty("outcome").GetString() == "Inspected"
                && Value(probe).GetProperty("uid").GetString() != driver.studyUid && Value(probe).GetProperty("annotations").GetInt32() == 0);
            var afterProbe = await driver.Code("inventory.csx"); driver.AssertCanonical(baseline, Value(afterProbe));
            Program.Check("temporary duplicate query rolls back and leaves existing Applied study inventory byte-identical", Value(afterProbe).GetRawText() == Value(beforeProbe).GetRawText());
            await driver.VerifyDuplicateIdentity("after rollback metadata probe");
            driver.Preserve();
            driver.resumeSource = new { originalAppliedGuid = id, original, retained, originalAppliedSourceSha256 = Digest(source), originalEvidence = evidence,
                currentHarnessScripts = driver.scripts.ToDictionary(p => p.Key, p => Digest(p.Value)), rollbackMetadataProbe = probe,
                boundary = "Existing committed study continued after direct postcommit classification. Temporary query clone is metadata evidence only; no postcommit qualification claim for that clone." };
            driver.Save(receipt, new { original, retained, beforeProbe, probe, afterProbe, hashesBefore, evidence, driver.resumeSource });
            await driver.FinishDuplicate(baseline, aid, bid, original);
        }
        finally
        {
            if (driver.client is not null) await driver.client.DisposeAsync(); if (driver.observer is not null) await driver.observer.DisposeAsync();
            Program.Check("Applied continuation preserves original transcripts, first inventory, intent and body", evidence.All(p => Hash(p.Key) == p.Value));
        }
    }
}
