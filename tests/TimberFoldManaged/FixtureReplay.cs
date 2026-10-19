using System.Diagnostics;
using System.Text.Json;
using RevitThyme.Core;

namespace RevitThyme.TimberFold.Managed;

internal static class FixtureReplay
{
    private sealed record Fixture(string Name, string File, int Panels, int Openings, string Sha256);
    private static readonly Fixture[] Fixtures =
    [
        new("gable", "gable-roof-control.json", 6, 3, "F5E17D83546BB594C00877D2974A4D98A3E791741551BA91DFBEA745A4BEDEF0"),
        new("irregular", "irregular-roof-envelope.json", 10, 4, "EB1E317995949B52B3FE0ABB1991C2EACEE1C9B102D28FDF92E118F2F115439C"),
        new("multistorey", "multistorey-roof-control.json", 10, 5, "DF2E19150242506537251B70732FB8D3A4096CD4607A32F89DAC3798F835C81C"),
        new("flat", "flat-roof-control.json", 18, 7, "256CCC7A0BC7B82CE51DB924B923F20A4B221EF6EAB33CA16F87965B56248909")
    ];
    internal sealed record Result(int Pass, string Building, string FixtureSha256, int Panels, int OpeningMaps, int Sheets,
        int CutFiles, int GuideFiles, int Warnings, double FitMilliseconds, double TotalMilliseconds, bool SourceUnchanged);
    internal static Result[] Run(string fixtureDirectory, string output)
    {
        if (!Path.IsPathFullyQualified(fixtureDirectory) || !Directory.Exists(fixtureDirectory))
            throw new DirectoryNotFoundException("Fixture root must be an existing absolute directory.");
        var results = new List<Result>();
        for (int pass = 1; pass <= 2; pass++)
        foreach (var fixture in Fixtures)
        {
            string input = Path.Combine(fixtureDirectory, fixture.File);
            Require(PayloadProof.Hash(input) == fixture.Sha256, "Original fixture hash differs: " + fixture.File);
            using var json = JsonDocument.Parse(File.ReadAllBytes(input));
            var snapshot = json.RootElement.GetProperty("snapshot").Deserialize<TimberSnapshot>(Protocol.Json)!;
            var profile = json.RootElement.GetProperty("profile").Deserialize<TimberFoldProfile>(Protocol.Json)!;
            var scope = json.RootElement.GetProperty("scope").Deserialize<TimberScope>(Protocol.Json)!;
            byte[] before = JsonSerializer.SerializeToUtf8Bytes(snapshot, Protocol.Json);
            string directory = Path.Combine(output, "pass-" + pass, fixture.Name);
            Directory.CreateDirectory(directory);
            var watch = Stopwatch.StartNew();
            var fit = TimberFabrication.Fit(snapshot, profile, scope); double fitMilliseconds = watch.Elapsed.TotalMilliseconds;
            Save("fitted.json", fit);
            Require(fit.State == TimberFitState.GeometryFitted && fit.Verification.Passed &&
                fit.Verification.Collisions.Length == 0 && fit.Verification.Unresolved.Length == 0,
                fixture.Name + " fit blocked: " + string.Join("; ", fit.Diagnostics.Where(d => d.Severity == TimberSeverity.Error).Select(d => d.Code + ": " + d.Message)));
            Require(fit.Draft.Panels.Length == fixture.Panels && fit.Draft.FaceMap.Length == fixture.Panels &&
                fit.Draft.OpeningMap.Length == fixture.Openings, "Literal original part/opening inventory changed: " + fixture.Name);
            CheckMaps(snapshot, fit);
            var stock = TimberStockLayout.Create(fit.Draft.Panels, profile); Save("stock.json", stock);
            Require(stock.Planned && stock.Placements.Length == fixture.Panels &&
                stock.Placements.Select(p => p.PanelId).Distinct(StringComparer.Ordinal).Count() == fixture.Panels, "Stock failed or duplicated parts: " + fixture.Name);
            var cut = TimberCutFiles.Stage(Path.Combine(directory, "cutting"), fit.Draft.Panels, stock, profile);
            Require(cut.Staged, "Cut stage failed: " + string.Join("; ", cut.Diagnostics.Select(d => d.Message)));
            var verified = TimberCutFiles.Verify(cut.Directory, fit.Draft.Panels, stock, profile);
            Require(verified.Staged && verified.Parts.Length == fixture.Panels && verified.NativeImportOutstanding && verified.PhysicalChecksOutstanding,
                "Cut readback or original part inventory failed: " + fixture.Name);
            var guides = TimberGuides.Stage(Path.Combine(directory, "guides"), fit, stock, profile);
            Require(guides.Staged && guides.PhysicalChecksOutstanding &&
                guides.Drawings.SelectMany(d => d.PanelIds).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
                    .SequenceEqual(fit.Draft.Panels.Select(p => p.Id).Order(StringComparer.Ordinal)), "Reference guides failed or lost parts: " + fixture.Name);
            Require(!fit.ManufacturingReady && !cut.ManufacturingReady && !guides.ManufacturingReady, "Managed replay cannot establish manufacturing readiness.");
            bool sourceUnchanged = before.SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(snapshot, Protocol.Json)) && PayloadProof.Hash(input) == fixture.Sha256;
            Require(sourceUnchanged, "Immutable original fixture changed during replay: " + fixture.Name);
            results.Add(new(pass, fixture.Name, fixture.Sha256, fit.Draft.Panels.Length, fit.Draft.OpeningMap.Length, stock.SheetCount,
                verified.Files.Length, guides.Files.Length, fit.Diagnostics.Count(d => d.Severity == TimberSeverity.Warning), fitMilliseconds, watch.Elapsed.TotalMilliseconds, sourceUnchanged));
            Console.WriteLine($"PASS run {pass} {fixture.Name}: {fixture.Panels} parts, {fixture.Openings} opening maps, cuts/readback/guides.");
            void Save<T>(string name, T value) => File.WriteAllBytes(Path.Combine(directory, name), JsonSerializer.SerializeToUtf8Bytes(value, Protocol.Json));
        }
        return results.ToArray();
    }
    private static void CheckMaps(TimberSnapshot source, TimberFittedDraft fit)
    {
        var panels = fit.Draft.Panels.ToDictionary(p => p.Id, StringComparer.Ordinal);
        Require(fit.Draft.FaceMap.Select(m => m.FaceId).Order(StringComparer.Ordinal).SequenceEqual(source.Faces.Select(f => f.Id).Order(StringComparer.Ordinal)), "Face-map identities differ from original capture.");
        foreach (var face in source.Faces)
        {
            var map = fit.Draft.FaceMap.Single(m => m.FaceId == face.Id);
            Require(map.SourceId == face.SourceId && map.PanelIds.Length > 0 && map.PanelIds.All(id =>
                panels.TryGetValue(id, out var panel) && panel.FaceId == face.Id && panel.SourceId == face.SourceId), "A source face has a false or empty contributing part map.");
        }
        Require(fit.Draft.OpeningMap.Select(m => m.OpeningId).Order(StringComparer.Ordinal).SequenceEqual(source.Openings.Select(o => o.Id).Order(StringComparer.Ordinal)), "Opening-map identities differ from original capture.");
        foreach (var opening in source.Openings)
        {
            var map = fit.Draft.OpeningMap.Single(m => m.OpeningId == opening.Id);
            Require(map.SourceId == opening.SourceId && map.HostFaceId == opening.HostFaceId && map.PanelIds.Length > 0 &&
                map.PanelIds.All(id => panels.TryGetValue(id, out var panel) && panel.FaceId == opening.HostFaceId), "An original opening has a false or empty contributing part map.");
        }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
}
