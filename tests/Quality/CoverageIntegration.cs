using RevitThyme.Quality;

internal static class CoverageIntegration
{
    public static async Task Run(string root, string config, string collector, Action<string, bool> check)
    {
        string output = Path.Combine(root, "artifacts", "coverage-receipt.json");
        var receipt = await CoverageCollection.Collect(root, config, collector, output);
        check("real collector receipts bind the tested source and reports", receipt.Reports.Length == 1
            && receipt.SourceDigest == SourceSnapshot.Digest(receipt.Sources)
            && CoverageCollection.Validate(root, config, output).SourceDigest == receipt.SourceDigest);
        string report = SourceSnapshot.Resolve(root, receipt.Reports[0].Path);
        byte[] bytes = File.ReadAllBytes(report);
        File.AppendAllText(report, " ");
        check("modified coverage bytes cannot certify the source", Rejected(() => CoverageCollection.Validate(root, config, output)));
        File.WriteAllBytes(report, bytes);
        string extra = Path.Combine(root, "TestResults", "extra", "coverage.cobertura.xml");
        Directory.CreateDirectory(Path.GetDirectoryName(extra)!); File.WriteAllBytes(extra, bytes);
        check("extra stale reports cannot inflate fresh coverage", Rejected(() => CoverageCollection.Validate(root, config, output)));
        File.Delete(extra);
        string source = Path.Combine(root, "src", "Math.cs"), original = File.ReadAllText(source);
        File.AppendAllText(source, "\n// later edit");
        check("changed source invalidates measured coverage", Rejected(() => CoverageCollection.Validate(root, config, output)));
        File.WriteAllText(source, original);
        var committed = await Commands.Run(["git", "-c", "user.name=QualityFixture", "-c", "user.email=quality@example.invalid",
            "-c", "commit.gpgsign=false", "commit", "--allow-empty", "-m", "metric-revision"], root,
            Path.Combine(root, "artifacts", "commit.txt"), 30);
        check("even a new revision with identical files requires fresh evidence", committed.Passed
            && Rejected(() => CoverageCollection.Validate(root, config, output)));
    }

    private static bool Rejected(Action action)
    {
        try { action(); return false; }
        catch (Exception error) when (error is IOException or InvalidDataException) { return true; }
    }
}
