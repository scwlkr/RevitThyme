using System.Text.Json;
using RevitThyme.Core;

namespace RevitThyme.McpTests;

internal sealed partial class ElectricalLabLive
{
    private async Task Execute()
    {
        Program.Check("electrical qualification pins exact owned process and independent copies", Text("folder") == folder && binding.ProcessId == input.GetProperty("pid").GetInt32()
            && binding.ProcessStart == Text("start") && Text("a") != Text("source") && Text("b") != Text("bSource") && Text("a") != Text("b"));
        Preserve(); Payloads(); client = await Client("driver"); observer = await Client("outcome observer");
        var tools = await client.ListToolsAsync(); Log(new { name = "list_tools", tools = tools.Select(t => new { t.Name, t.ProtocolTool.Annotations, schema = t.JsonSchema }) });
        Program.Check("electrical official SDK exposes installed protocol 13 and 32 tools", tools.Count == 32 && (await Call("list_operations")).GetProperty("protocolVersion").GetInt32() == 13);
        var home = await Context(false); Program.Check("electrical workflow begins at owned Home", !home.Snapshot.HasDocument);
        var openedA = await Open(Text("a"), savedA); var openedB = await Open(Text("b"), Text("bSourceSha256"));
        string aid = DocumentId(openedA), bid = DocumentId(openedB); var current = await Context(false);
        await Call("activate_document", Lifecycle(current.Id, Text("a"), aid), "document_activated");
        var initialResult = await Code("inventory.csx"); var baseline = Value(initialResult);
        Save("initial-inventory.json", new { baseline, reference }); Preserve();
        Save("inspection-ready.json", new { passed = true, binding, aid, bid, baseline, qaName });
        await Workflow(baseline, aid, bid);
    }
    private async Task Workflow(JsonElement baseline, string aid, string bid)
    {
        ValidateReference(baseline); annotationUids = baseline.GetProperty("sourceAnnotations").EnumerateArray().Select(a => a.GetProperty("uid").GetString()!).ToArray();
        string sourceUid = baseline.GetProperty("sourceUid").GetString()!, sheetUid = baseline.GetProperty("sheet").GetProperty("uid").GetString()!;
        await Export(sheetUid, "baseline-sheet"); await Export(sourceUid, "baseline-power");
        AssertCanonical(baseline, Value(await Code("inventory.csx")));
        var duplicate = await Code("duplicate.csx", true); studyUid = Value(duplicate).GetProperty("uid").GetString()!;
        await FinishDuplicate(baseline, aid, bid, duplicate);
    }
    private async Task FinishDuplicate(JsonElement baseline, string aid, string bid, JsonElement duplicate)
    {
        annotationUids = baseline.GetProperty("sourceAnnotations").EnumerateArray().Select(a => a.GetProperty("uid").GetString()!).ToArray();
        string sheetUid = baseline.GetProperty("sheet").GetProperty("uid").GetString()!;
        var empty = Value(await Code("inventory.csx")); AssertCanonical(baseline, empty); CheckStudy(empty, baseline, 0);
        await Export(studyUid, "empty-study"); var copy = await Code("copy-annotations.csx", true);
        var copied = Value(await Code("inventory.csx")); AssertCanonical(baseline, copied); CheckStudy(copied, baseline, 2);
        var studyAnnotations = copied.GetProperty("study").GetProperty("annotations").Clone();
        Program.Check("copy result and independent query agree on exact two native annotation identities", Value(copy).GetProperty("copied").EnumerateArray().Select(a => a.GetProperty("uid").GetString()).Order()
            .SequenceEqual(studyAnnotations.EnumerateArray().Select(a => a.GetProperty("uid").GetString()).Order()));
        await Export(studyUid, "copied-study"); await Export(sheetUid, "unchanged-sheet"); await ActivateView(studyUid);
        var current = await Context(); Program.Check("bounded electrical edits are unsaved before explicit save", current.Snapshot.IsModified); Preserve();
        string saveId = Guid.NewGuid().ToString(); var saved = await Call("save_document", Lifecycle(current.Id, Text("a"), aid, savedA, saveId), "document_saved");
        savedA = Hash(Text("a")); Program.Check("explicit typed save reports exact saved A hash", saved.GetProperty("documentLifecycle").GetProperty("sha256").GetString() == savedA);
        current = await Context(); Program.Check("saved electrical A is clean on exact study view", !current.Snapshot.IsModified && current.Snapshot.Target.ViewId == studyUid); Preserve();
        JsonElement[] retained = [duplicate, copy, saved]; await client.DisposeAsync(); client = await Client("completed-outcome recovery");
        foreach (var result in retained) await RecoverExact(result);
        current = await Context(); await Inventory(current.Id, [aid, bid]);
        var closeArgs = Lifecycle(current.Id, Text("a"), aid, savedA); closeArgs["activateBeforeClose"] = new CloseActivationTarget(bid, Text("b"), Text("bSourceSha256"));
        await Call("close_document", closeArgs, "document_closed"); var handoff = await Context(false);
        Program.Check("active close hands off only to exact clean B", handoff.Snapshot.Target.DocumentId == bid && handoff.Snapshot.DocumentPath == Text("b") && !handoff.Snapshot.IsModified);
        await Inventory(handoff.Id, [bid]); Preserve(); var reopened = await Open(Text("a"), savedA); string reopenedId = DocumentId(reopened);
        Program.Check("saved electrical copy reopens with a new native document identity", reopenedId != aid);
        handoff = await Context(false); await Call("activate_document", Lifecycle(handoff.Id, Text("a"), aid), "wrong_document");
        Program.Check("retired document identity refusal preserves exact B handoff", (await Context(false)).Snapshot == handoff.Snapshot);
        await Call("activate_document", Lifecycle(handoff.Id, Text("a"), reopenedId), "document_activated"); await ActivateView(studyUid);
        await FinishSavedReopen(baseline, bid, reopenedId, duplicate, copy, saved, studyAnnotations);
    }
    private async Task FinishSavedReopen(JsonElement baseline, string bid, string reopenedId, JsonElement duplicate, JsonElement copy, JsonElement saved, JsonElement studyAnnotations)
    {
        JsonElement[] retained = [duplicate, copy, saved];
        var persisted = Value(await Code("inventory.csx")); AssertCanonical(baseline, persisted); CheckStudy(persisted, baseline, 2);
        await VerifyDuplicateIdentity("after save/reopen");
        Program.Check("saved study preserves exact annotation IDs, types, positions and ownership after reopen", persisted.GetProperty("study").GetProperty("annotations").GetRawText() == studyAnnotations.GetRawText());
        await Export(studyUid, "reopened-study"); foreach (var result in retained) await RecoverExact(result);
        var current = await Context(); await Call("close_document", Lifecycle(current.Id, Text("b"), bid, Text("bSourceSha256")), "document_closed");
        var final = await Context(); await Inventory(final.Id, [reopenedId]); var finalValue = Value(await Code("inventory.csx")); AssertCanonical(baseline, finalValue); CheckStudy(finalValue, baseline, 2);
        final = await Context(); Program.Check("electrical workflow finishes clean on exact persisted study in A", final.Snapshot.Target.DocumentId == reopenedId
            && final.Snapshot.DocumentPath == Text("a") && final.Snapshot.Target.ViewId == studyUid && !final.Snapshot.IsModified); Preserve(); Payloads();
        if (continuationEvidence is not null) Program.Check("completed Applied continuation preserves all original evidence", continuationEvidence.All(p => Hash(p.Key) == p.Value));
        Save("complete.json", new { passed = true, binding, savedA, savedB = Text("bSourceSha256"), sourceSha256 = Text("sourceSha256"), final.Snapshot, studyUid, qaName,
            currentSourceWorkflow = "passed", historicalAcceptance = false, historicalFineAcceptance = false, historicalAcceptanceStatus = "outstanding", currentSourceProfile, historicalReferenceDifferences,
            baseline, finalInventory = finalValue, studyAnnotations, nativeAddedIds = added.Order(), collectedAddedIds = collectedAdded.Order(), duplicateInspections, duplicate, copy, saved, transcriptName, resumeSource, calls,
            resultSource = "immutable installed official SDK; Roslyn native transaction bodies; typed lifecycle/image operations; completed outcomes recovered without interrupted-loss claim" });
    }
}
