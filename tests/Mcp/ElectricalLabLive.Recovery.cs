using System.Diagnostics;
using System.Text.Json;
using RevitThyme.Core;

namespace RevitThyme.McpTests;

internal sealed partial class ElectricalLabLive
{
    private static string OriginalGuid(string value) => Guid.TryParse(value, out var id) && id != Guid.Empty ? id.ToString()
        : throw new ArgumentException("Original request must be a non-empty GUID.");
    private JsonElement OriginalIntent(string id)
    {
        var intent = Read(Path.Combine(folder, "intent-" + id + ".json"));
        if (OriginalGuid(intent.GetProperty("args").GetProperty("requestId").GetString()!) != id || intent.GetProperty("name").GetString() == "get_operation")
            throw new InvalidDataException("Original intent does not identify the requested native operation.");
        return intent;
    }
    private object ValidatePreInventory()
    {
        Program.Check("electrical recovery pins exact original session/process and owned input folder", Text("folder") == folder && binding.ProtocolVersion == 13
            && binding.ProcessId == input.GetProperty("pid").GetInt32() && binding.ProcessStart == Text("start")
            && Path.GetDirectoryName(Text("a")) == folder && Path.GetDirectoryName(Text("b")) == folder && Text("a") != Text("b")
            && Text("a") != Text("source") && Text("b") != Text("source") && Text("b") != Text("bSource") && Text("a") != Text("bSource"));
        var hashes = new { source = Hash(Text("source")), bSource = Hash(Text("bSource")), a = Hash(Text("a")), b = Hash(Text("b")), manifest = Hash(Path.Combine(Text("bundle"), "manifest.json")) };
        Program.Check("pre-inventory recovery preserves source and both unopened-for-edit copies", hashes.source == Text("sourceSha256") && hashes.a == Text("sourceSha256")
            && hashes.bSource == Text("bSourceSha256") && hashes.b == Text("bSourceSha256") && hashes.manifest == Text("manifestSha256"));
        if (input.TryGetProperty("referenceSha256", out var referenceHash)) Program.Check("recovery reference pin unchanged", Hash(Text("referencePath")) == referenceHash.GetString());
        if (input.TryGetProperty("registrationSha256", out var registrationHash)) Program.Check("recovery installed registration pin unchanged", Hash(Text("registration")) == registrationHash.GetString());
        Payloads(); return hashes;
    }
    public static async Task ObserveRun(string folder, string request)
    {
        var driver = new ElectricalLabLive(folder); string id = OriginalGuid(request); var intent = driver.OriginalIntent(id);
        string originalTranscript = Path.Combine(folder, "transcript.json"), originalHash = Hash(originalTranscript);
        string receipt = "observe-" + id + "-" + Guid.NewGuid() + ".json"; var observations = new List<object>();
        var before = driver.ValidatePreInventory();
        void Persist() => driver.Save(receipt, new { observationOnly = true, originalGuid = id, intent, binding = driver.binding, originalTranscriptSha256 = originalHash, before, observations });
        Persist();
        for (int index = 0; index < 2; index++)
        {
            // Separate external SDK servers prove recovery needs neither a capture nor the original connection.
            await using var client = await driver.Client("read-only original outcome " + index); var watch = Stopwatch.StartNew();
            var result = await Program.Call(client, "get_operation", new() { ["requestId"] = id }).WaitAsync(TimeSpan.FromSeconds(5));
            string code = result.Data.GetProperty("code").GetString()!;
            observations.Add(new { index, name = "get_operation", requestedOriginalGuid = id, milliseconds = watch.Elapsed.TotalMilliseconds, result.Data, result.Result.IsError }); Persist();
            Program.Check("read-only electrical recovery returns responsive Pending or exact original terminal identity", watch.Elapsed < TimeSpan.FromSeconds(2)
                && (code == "pending" ? result.Result.IsError == true : result.Data.TryGetProperty("id", out var terminal) && terminal.GetString() == id));
        }
        var after = driver.ValidatePreInventory(); Program.Check("read-only observer leaves original workflow transcript untouched", Hash(originalTranscript) == originalHash);
        driver.Save(receipt, new { observationOnly = true, originalGuid = id, intent, binding = driver.binding, originalTranscriptSha256 = originalHash, before, after, observations });
        Console.WriteLine("Electrical outcome observation receipt: " + Path.Combine(folder, receipt));
    }
    public static async Task ResumeRun(string folder, string request)
    {
        var driver = new ElectricalLabLive(folder); string id = OriginalGuid(request); var intent = driver.OriginalIntent(id);
        string transcript = Path.Combine(folder, "transcript.json"), originalHash = Hash(transcript); var entries = Read(transcript).EnumerateArray().ToArray();
        var args = intent.GetProperty("args");
        if (intent.GetProperty("name").GetString() != "activate_document" || args.GetProperty("path").GetString() != driver.Text("a")
            || File.Exists(Path.Combine(folder, "initial-inventory.json")) || File.Exists(Path.Combine(folder, "complete.json")))
            throw new InvalidDataException("Resume accepts only the original owned-A activation before the first inventory.");
        bool Submitted(JsonElement e) => e.TryGetProperty("phase", out var phase) && phase.GetString() == "submitted";
        if (entries.Where(Submitted).Any(e => e.GetProperty("name").GetString() is "run_query" or "run_modify" or "save_document" or "close_document" or "activate_view" or "export_view_image")
            || entries.Where(Submitted).Count(e => e.GetProperty("name").GetString() == "activate_document") != 1
            || !entries.Where(Submitted).Any(e => e.GetProperty("name").GetString() == "activate_document" && e.GetProperty("args").GetProperty("requestId").GetString() == id))
            throw new InvalidDataException("Original transcript is outside the narrowly permitted pre-inventory activation boundary.");
        JsonElement Opened(string path) => entries.Single(e => e.TryGetProperty("phase", out var phase) && phase.GetString() == "returned"
            && e.GetProperty("name").GetString() == "open_document" && e.GetProperty("data").GetProperty("code").GetString() == "document_opened"
            && e.GetProperty("args").GetProperty("path").GetString() == path).GetProperty("data");
        var openedA = Opened(driver.Text("a")); var openedB = Opened(driver.Text("b")); string aid = DocumentId(openedA), bid = DocumentId(openedB);
        if (args.GetProperty("documentId").GetString() != aid || openedA.GetProperty("outcome").GetString() != "Completed" || openedB.GetProperty("outcome").GetString() != "Completed")
            throw new InvalidDataException("Original open receipts or activation target do not agree.");
        foreach (var open in new[] { openedA, openedB })
        {
            string openId = OriginalGuid(open.GetProperty("id").GetString()!); var openIntent = driver.OriginalIntent(openId);
            string path = open.GetProperty("documentLifecycle").GetProperty("path").GetString()!;
            if (openIntent.GetProperty("name").GetString() != "open_document" || openIntent.GetProperty("args").GetProperty("path").GetString() != path
                || path != driver.Text("a") && path != driver.Text("b")
                || open.GetProperty("documentLifecycle").GetProperty("sha256").GetString() != driver.Text(path == driver.Text("a") ? "sourceSha256" : "bSourceSha256"))
                throw new InvalidDataException("Original typed open intent/path/hash receipt does not match the owned copies.");
        }
        driver.transcriptName = "resume-transcript-" + id + "-" + Guid.NewGuid() + ".json";
        string receipt = "resume-activation-" + id + "-" + Guid.NewGuid() + ".json";
        try
        {
            var before = driver.ValidatePreInventory(); driver.observer = await driver.Client("resume original activation recovery");
            var activation = await driver.Observe(id);
            driver.Save(receipt, new { originalGuid = id, intent, aid, bid, openedA, openedB, activation, before, originalTranscriptSha256 = originalHash, resumedTranscript = driver.transcriptName });
            if (activation.GetProperty("code").GetString() == "pending") throw new InvalidOperationException("Original activation remains Pending; no context, inventory or dependent operation was submitted.");
            if (activation.GetProperty("id").GetString() != id || activation.GetProperty("code").GetString() != "document_activated"
                || activation.GetProperty("outcome").GetString() != "Completed" || DocumentId(activation) != aid
                || activation.GetProperty("documentLifecycle").GetProperty("path").GetString() != driver.Text("a"))
                throw new InvalidDataException("Original activation has no exact verified Completed result for owned A.");
            foreach (var open in new[] { openedA, openedB })
                Program.Check("resume recovers exact original typed open outcome without replay", (await driver.Observe(open.GetProperty("id").GetString()!)).GetRawText() == open.GetRawText());
            driver.client = await driver.Client("resume after exact original activation"); var context = await driver.Context();
            Program.Check("resumed current context is exact original active clean A", context.Snapshot.Target.DocumentId == aid && !context.Snapshot.IsModified);
            await driver.Inventory(context.Id, [aid, bid]);
            var initial = await driver.Code("inventory.csx"); var baseline = Value(initial);
            driver.Save("initial-inventory.json", new { baseline, reference = driver.reference });
            driver.resumeSource = new { activationGuid = id, activation, originalTranscriptSha256 = originalHash, originalIntentSha256 = Hash(Path.Combine(folder, "intent-" + id + ".json")) };
            driver.Preserve(); await driver.Workflow(baseline, aid, bid);
        }
        finally
        {
            if (driver.client is not null) await driver.client.DisposeAsync(); if (driver.observer is not null) await driver.observer.DisposeAsync();
            Program.Check("resumed workflow preserves original activation transcript", Hash(transcript) == originalHash);
        }
    }
    private async Task RecordBlocked(string name, string request, JsonElement response)
    {
        string id = OriginalGuid(request); var intent = OriginalIntent(id); var watch = Stopwatch.StartNew();
        var retained = await Observe(id); Save("blocked-operation-" + id + "-" + Guid.NewGuid() + ".json", new { name, originalGuid = id, intent, response, retained,
            recoveryMilliseconds = watch.Elapsed.TotalMilliseconds, binding, boundary = "Dependent assertion remains failed. No replacement request or lifecycle/UI fallback." });
    }
}
