using System.Text.Json;
using RevitThyme.Core;

namespace RevitThyme.McpTests;

internal sealed partial class ElectricalLabLive
{
    public static async Task FinishReopen(string folder, string request)
    {
        var driver = new ElectricalLabLive(folder); string activationId = OriginalGuid(request);
        if (File.Exists(Path.Combine(folder, "complete.json"))) throw new InvalidDataException("Completed workflow cannot be finished again.");
        string latest = Directory.GetFiles(folder, "continue-applied-transcript-*.json").OrderByDescending(File.GetLastWriteTimeUtc).First();
        var phase = Read(latest).EnumerateArray().ToArray();
        var files = Directory.GetFiles(folder); var evidence = files.ToDictionary(p => p, Hash);
        var entries = files.Where(p => Path.GetFileName(p).Contains("transcript", StringComparison.Ordinal) && Path.GetExtension(p) == ".json")
            .SelectMany(p => Read(p).EnumerateArray().Select(e => e.Clone())).ToArray();
        bool Submitted(JsonElement e) => e.TryGetProperty("phase", out var p) && p.GetString() == "submitted";
        bool Returned(JsonElement e) => e.TryGetProperty("phase", out var p) && p.GetString() == "returned";
        string Name(JsonElement e) => e.GetProperty("name").GetString()!;
        string Id(JsonElement e) => e.GetProperty("args").GetProperty("requestId").GetString()!;
        var activationSubmission = phase.Single(e => Submitted(e) && Name(e) == "activate_document" && Id(e) == activationId);
        var originalActivationResponse = phase.Single(e => Returned(e) && Name(e) == "activate_document" && Id(e) == activationId).GetProperty("data");
        var activationIntent = driver.OriginalIntent(activationId); DateTime boundary = activationSubmission.GetProperty("utc").GetDateTime();
        if (entries.Where(Submitted).Any(e => Name(e) is "save_document" or "close_document" && e.GetProperty("args").GetProperty("path").GetString() != driver.Text("a"))
            || entries.Where(Submitted).Count(e => Name(e) == "run_modify") != 2 || entries.Where(Submitted).Count(e => Name(e) == "save_document") != 1
            || entries.Where(Submitted).Count(e => Name(e) == "close_document") != 1
            || entries.Where(Submitted).Any(e => Name(e) is not ("get_context" or "get_operation" or "list_documents" or "list_operations" or "open_document" or "activate_document" or "activate_view" or "export_view_image" or "run_query" or "run_modify" or "save_document" or "close_document"))
            || entries.Where(Submitted).Any(e => e.GetProperty("utc").GetDateTime() > boundary && Name(e) is not ("get_context" or "get_operation" or "list_documents" or "run_query")))
            throw new InvalidDataException("Finish-reopen accepts only the saved duplicate/copy workflow before any B close or later writes.");
        JsonElement Result(string name, string code) => phase.Single(e => Returned(e) && Name(e) == name && e.GetProperty("data").GetProperty("code").GetString() == code).GetProperty("data");
        var saved = Result("save_document", "document_saved"); var closed = Result("close_document", "document_closed"); var reopened = Result("open_document", "document_opened");
        var copy = Result("run_modify", "applied"); string reopenedId = DocumentId(reopened), oldId = DocumentId(saved);
        string saveId = saved.GetProperty("id").GetString()!; driver.savedA = saved.GetProperty("documentLifecycle").GetProperty("sha256").GetString()!;
        var baseline = Read(Path.Combine(folder, "initial-inventory.json")).GetProperty("baseline");
        var duplicateEntry = entries.Single(e => Returned(e) && Name(e) == "run_modify" && e.GetProperty("data").GetProperty("id").GetString() != copy.GetProperty("id").GetString());
        var duplicate = duplicateEntry.GetProperty("data"); string duplicateId = duplicate.GetProperty("id").GetString()!;
        string classificationPath = Path.Combine(folder, "committed-duplicate-classification-" + duplicateId + ".json"); var classification = Read(classificationPath);
        Program.Check("saved-reopen pins sealed original duplicate classification", classification.GetProperty("original").GetRawText() == duplicate.GetRawText());
        var details = Value(duplicate); driver.studyUid = details.GetProperty("uid").GetString()!; driver.qaName = details.GetProperty("name").GetString()!;
        driver.inspectionRootId = details.GetProperty("id").GetInt64();
        var native = duplicate.GetProperty("execution").GetProperty("changes").Deserialize<CodeChanges>(Protocol.Json)!;
        driver.inspectionNativeAdded = native.Added;
        var sealedInspection = classification.GetProperty("inspection"); var roles = driver.ClassifyCommitted(Value(sealedInspection));
        driver.duplicateIdentity = CompanionIdentity(Value(sealedInspection), roles);
        Program.Check("sealed companion identity exactly matches independently classified native evidence", driver.duplicateIdentity == classification.GetProperty("duplicateIdentity").GetString());
        foreach (long value in native.Added) driver.added.Add(value);
        foreach (var row in Value(sealedInspection).GetProperty("added").EnumerateArray()) if (row.GetProperty("collectorMembership").GetBoolean()) driver.collectedAdded.Add(row.GetProperty("id").GetInt64());
        Program.Check("sealed native and collector sets remain exact", driver.added.Order().SequenceEqual(classification.GetProperty("nativeAddedIds").EnumerateArray().Select(e => e.GetInt64()).Order())
            && driver.collectedAdded.Order().SequenceEqual(classification.GetProperty("collectedAddedIds").EnumerateArray().Select(e => e.GetInt64()).Order()));
        var copied = copy.GetProperty("execution").GetProperty("changes").Deserialize<CodeChanges>(Protocol.Json)!; var copiedRows = Value(copy).GetProperty("copied").EnumerateArray().ToArray();
        Program.Check("restored copy receipt contains exactly two collected additions and no original modifications/deletions", copied.Added.Length == 2 && copied.Modified.Length == 0 && copied.Deleted.Length == 0
            && copiedRows.Length == 2 && copiedRows.All(e => e.GetProperty("collectorMembership").GetBoolean()) && copied.Added.Order().SequenceEqual(copiedRows.Select(e => e.GetProperty("id").GetInt64()).Order()));
        foreach (long value in copied.Added) Program.Check("saved annotation identity remains distinct", driver.added.Add(value) && driver.collectedAdded.Add(value));
        Program.Check("saved native and collected accounting restores twelve and eleven IDs", driver.added.Count == 12 && driver.collectedAdded.Count == 11);
        var annotated = phase.First(e => Returned(e) && Name(e) == "run_query" && e.GetProperty("data").GetProperty("code").GetString() == "inspected"
            && Value(e.GetProperty("data")).TryGetProperty("study", out var study) && study.ValueKind == JsonValueKind.Object && study.GetProperty("annotations").GetArrayLength() == 2).GetProperty("data");
        var studyAnnotations = Value(annotated).GetProperty("study").GetProperty("annotations").Clone();
        var openedB = entries.Single(e => Returned(e) && Name(e) == "open_document" && e.GetProperty("args").GetProperty("path").GetString() == driver.Text("b")).GetProperty("data"); string bid = DocumentId(openedB);
        var openedA = entries.Single(e => Returned(e) && Name(e) == "open_document" && e.GetProperty("data").GetProperty("code").GetString() == "document_opened" && DocumentId(e.GetProperty("data")) == oldId).GetProperty("data");
        var wrongOldId = Result("activate_document", "wrong_document");
        Program.Check("saved close/reopen receipts pin exact A/hash, new identity and original B handoff", reopenedId != oldId && DocumentId(closed) == oldId
            && new[] { saved, closed, reopened }.All(e => e.GetProperty("documentLifecycle").GetProperty("path").GetString() == driver.Text("a")
                && e.GetProperty("documentLifecycle").GetProperty("sha256").GetString() == driver.savedA)
            && activationIntent.GetProperty("name").GetString() == "activate_document" && activationIntent.GetProperty("args").GetProperty("documentId").GetString() == reopenedId
            && activationIntent.GetProperty("args").GetProperty("path").GetString() == driver.Text("a") && Id(activationSubmission) == activationId
            && originalActivationResponse.GetProperty("code").GetString() == "transport_lost" && originalActivationResponse.GetProperty("outcome").GetString() == "Unconfirmed"
            && JsonSerializer.Serialize(activationSubmission.GetProperty("args")) == JsonSerializer.Serialize(activationIntent.GetProperty("args")));
        JsonElement[] retained = [duplicate, copy, saved, closed, reopened, openedA, openedB, wrongOldId, annotated];
        foreach (var result in retained)
        {
            string operationId = result.GetProperty("id").GetString()!; var originalIntent = driver.OriginalIntent(operationId);
            var returned = entries.Single(e => Returned(e) && Name(e) == originalIntent.GetProperty("name").GetString()
                && e.TryGetProperty("args", out var args) && args.TryGetProperty("requestId", out var rid) && rid.GetString() == operationId);
            Program.Check("saved workflow result retains exact submitted intent", result.GetRawText() == returned.GetProperty("data").GetRawText()
                && Name(returned) == originalIntent.GetProperty("name").GetString() && JsonSerializer.Serialize(returned.GetProperty("args")) == JsonSerializer.Serialize(originalIntent.GetProperty("args")));
            if (result.TryGetProperty("execution", out var execution) && execution.ValueKind == JsonValueKind.Object)
            {
                string body = originalIntent.GetProperty("args").GetProperty("source").GetString()!;
                Program.Check("saved native code retains original body and source hash", Digest(body) == execution.GetProperty("sourceSha256").GetString()
                    && File.ReadAllText(Path.Combine(folder, "body-" + operationId + ".csx")) == body);
            }
        }
        driver.transcriptName = "finish-reopen-transcript-" + activationId + "-" + Guid.NewGuid() + ".json"; driver.continuationEvidence = evidence;
        string receipt = "finish-reopen-" + activationId + "-" + Guid.NewGuid() + ".json";
        try
        {
            var hashesBefore = driver.ValidatePreInventory(driver.savedA); driver.observer = await driver.Client("finish original reopen activation");
            var observed = await driver.RecoverSubmitted("activate_document", activationId, originalActivationResponse);
            var activation = observed.Data;
            Program.Check("finish recovers only original Completed activation for exact reopened A", activation.GetProperty("id").GetString() == activationId
                && activation.GetProperty("outcome").GetString() == "Completed" && activation.GetProperty("code").GetString() == "document_activated"
                && DocumentId(activation) == reopenedId && activation.GetProperty("documentLifecycle").GetProperty("path").GetString() == driver.Text("a"));
            foreach (var result in retained) Program.Check("saved workflow original outcome recovers byte-exact", (await driver.Observe(result.GetProperty("id").GetString()!)).GetRawText() == result.GetRawText());
            driver.client = await driver.Client("finish verified saved reopen"); var context = await driver.Context();
            Program.Check("finish starts on exact clean reopened QA with saved hash", context.Snapshot.Target.DocumentId == reopenedId && context.Snapshot.Target.ViewId == driver.studyUid && !context.Snapshot.IsModified);
            var inventory = await driver.Call("list_documents", new() { ["captureId"] = context.Id, ["requestId"] = Guid.NewGuid().ToString() }, "inspected");
            var documents = inventory.GetProperty("documents").Deserialize<DocumentPage>(Protocol.Json)!;
            Program.Check("saved finish retains clean active A and clean background B only", documents.Total == 2 && documents.NextOffset == null && documents.Items.Length == 2
                && documents.Items.Any(d => d.DocumentId == reopenedId && d.Path == driver.Text("a") && d.IsActive && !d.IsModified)
                && documents.Items.Any(d => d.DocumentId == bid && d.Path == driver.Text("b") && !d.IsActive && !d.IsModified));
            driver.ValidateReference(baseline); driver.duplicateInspections.Add(sealedInspection);
            driver.resumeSource = new { originalActivationGuid = activationId, activation, originalContinueTranscript = latest, originalSavedGuid = saveId, originalSavedHash = driver.savedA,
                originalDuplicateGuid = duplicateId, originalDuplicateSourceSha256 = duplicate.GetProperty("execution").GetProperty("sourceSha256"), originalEvidence = evidence,
                currentHarnessScripts = driver.scripts.ToDictionary(p => p.Key, p => Digest(p.Value)), boundary = "Only verified saved-reopen tail is continued; no duplicate, copy, save, close-A, reopen or activation replay." };
            driver.Save(receipt, new { activationIntent, activation, retained, hashesBefore, inventory, driver.resumeSource });
            await driver.FinishSavedReopen(baseline, bid, reopenedId, duplicate, copy, saved, studyAnnotations);
        }
        finally
        {
            if (driver.client is not null) await driver.client.DisposeAsync(); if (driver.observer is not null) await driver.observer.DisposeAsync();
            Program.Check("saved-reopen finish preserves every prior run file", evidence.All(p => Hash(p.Key) == p.Value));
        }
    }
}
