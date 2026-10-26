using RevitThyme.Quality;

internal static class MutationJournalChecks
{
    public static async Task Run(string root, string config, string path, MutationReport report, Action<string, bool> check)
    {
        byte[] original = File.ReadAllBytes(path);
        var journals = new[]
        {
            report with { EngineId = "different-engine" }, report with { CommandsDigest = "changed-commands" },
            report with { TotalSites = report.TotalSites + 1 },
            report with { Results = [report.Results[0] with { SiteId = "different-site" }] },
            report with { Results = report.Results.Select((r, i) => i == 0 ? r with { State = MutationState.Unconfirmed } : r).ToArray() }
        };
        foreach (var journal in journals)
        {
            try
            {
                Reports.Write(path, journal);
                bool rejected = false;
                try { await MutationRunner.Run(root, config, "src/Math.cs", path, 0); } catch (InvalidDataException) { rejected = true; }
                check("changed or unresolved mutation journals cannot resume as measured passes", rejected);
            }
            finally { File.WriteAllBytes(path, original); }
        }
    }
}
