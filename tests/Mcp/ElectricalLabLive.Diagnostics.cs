using System.Text.Json;

namespace RevitThyme.McpTests;

internal sealed partial class ElectricalLabLive
{
    public static async Task DuplicateDiagnostic(string folder)
    {
        var driver = new ElectricalLabLive(folder);
        driver.transcriptName = "duplicate-diagnostic-transcript-" + Guid.NewGuid() + ".json";
        var evidence = Directory.GetFiles(folder, "resume-transcript-*.json").Append(Path.Combine(folder, "transcript.json"))
            .Append(Path.Combine(folder, "initial-inventory.json")).ToDictionary(path => path, Hash);
        string receipt = "duplicate-diagnostic-" + Guid.NewGuid() + ".json";
        try
        {
            var hashesBefore = driver.ValidatePreInventory(); var baseline = Read(Path.Combine(folder, "initial-inventory.json")).GetProperty("baseline");
            driver.client = await driver.Client("read-only duplicate companion diagnostic"); driver.observer = await driver.Client("diagnostic outcome observer");
            var before = await driver.Context();
            Program.Check("duplicate diagnostic begins on exact clean source power plan", !before.Snapshot.IsModified
                && before.Snapshot.Target.ViewId == baseline.GetProperty("sourceUid").GetString());
            var beforeInventory = await driver.Code("inventory.csx"); driver.AssertCanonical(baseline, Value(beforeInventory));
            Program.Check("diagnostic starts with every frozen original inventory field unchanged", Value(beforeInventory).GetRawText() == baseline.GetRawText());
            // run_query owns rollback: this is never submitted through run_modify or promoted into a workflow edit.
            var diagnostic = await driver.Code("inspect-duplicate.csx");
            driver.Save(receipt, new { diagnosticOnly = true, driver.binding, before.Snapshot, diagnostic, beforeInventory, hashesBefore,
                boundary = "Temporary duplicate is inside host query rollback. Unknown companions remain unclassified; production duplicate and resume guards are unchanged." });
            var after = await driver.Context();
            Program.Check("duplicate diagnostic rollback preserves clean exact active source identity", !after.Snapshot.IsModified
                && after.Snapshot.Target.DocumentId == before.Snapshot.Target.DocumentId && after.Snapshot.Target.ViewId == before.Snapshot.Target.ViewId);
            var afterInventory = await driver.Code("inventory.csx"); driver.AssertCanonical(baseline, Value(afterInventory));
            Program.Check("diagnostic rollback leaves every original native element and canonical field unchanged", Value(afterInventory).GetRawText() == baseline.GetRawText());
            var hashesAfter = driver.ValidatePreInventory();
            Program.Check("diagnostic preserves original run transcripts and frozen inventory", evidence.All(p => Hash(p.Key) == p.Value));
            driver.Save(receipt, new { diagnosticOnly = true, passed = true, driver.binding, before = before.Snapshot, after = after.Snapshot,
                diagnostic, beforeInventory, afterInventory, hashesBefore, hashesAfter, evidence, driver.transcriptName,
                boundary = "Query-only temporary authoring rolled back; companion evidence awaits review. No workflow acceptance or lifecycle continuation." });
            Console.WriteLine("Electrical duplicate diagnostic receipt: " + Path.Combine(folder, receipt));
        }
        finally
        {
            if (driver.client is not null) await driver.client.DisposeAsync(); if (driver.observer is not null) await driver.observer.DisposeAsync();
        }
    }
}
