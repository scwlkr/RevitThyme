using System.Security.Cryptography;
using System.Collections.Immutable;
using System.Text.Json;
using RevitThyme.Core;

namespace RevitThyme.Unit;

internal static class TimberRoofExposureContract
{
    public static void Run(Action<string, bool> check)
    {
        // Full wire-result characterizations complement the independently worked
        // geometry and area assertions in TimberFoldRoofs.
        (string File, string Hash)[] cases =
        [
            ("irregular-roof-envelope.json", "de3d4bc87c08f88717037f16f173f73cecac73017852d6222762381972b09421"),
            ("flat-roof-control.json", "5ee3383fbc1b6c59012662604fe712d50538d30e7d10c78fc824b150313821de"),
            ("gable-roof-control.json", "8f742902f571536c4f6c1002eb276c80b700dce27e039437c7ad1256ecf4b2bd"),
        ];
        foreach (var item in cases)
        {
            using var json = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", item.File)));
            var root = json.RootElement;
            var snapshot = root.GetProperty("snapshot").Deserialize<TimberSnapshot>(Protocol.Json) ?? throw new InvalidDataException("snapshot");
            var profile = root.GetProperty("profile").Deserialize<TimberFoldProfile>(Protocol.Json) ?? throw new InvalidDataException("profile");
            var scope = root.GetProperty("scope").Deserialize<TimberScope>(Protocol.Json) ?? throw new InvalidDataException("scope");
            var fit = TimberFabrication.Fit(snapshot, profile, scope);
            string hash = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(fit, Protocol.Json)));
            check("roof fitting preserves its complete geometry, ledger and diagnostics: " + item.File, hash == item.Hash);
        }
        Hulls(check);
        NoncanonicalHole(check);
    }

    private static void Hulls(Action<string, bool> check)
    {
        var empty = TimberJointHull.Exact([], new(default));
        var line = TimberJointHull.Exact([new(0, 0, 0), new(1, 0, 0), new(2, 0, 0), new(1, 0, 9)], new(default));
        var triangle = TimberJointHull.Exact([new(0, 0, 0), new(2, 0, 0), new(0, 1, 0), new(1, 0, 7)], new(default));
        check("empty and collinear projected hulls have fewer than three vertices", empty.Length == 0 && line.Length == 2);
        check("a proper projected hull retains its exact unit area", triangle.Length == 3 && TimberJointHull.Area(triangle) == 1);
    }

    private static void NoncanonicalHole(Action<string, bool> check)
    {
        var source = new TimberSnapshot(new(1, "start", "session", "doc", "view", 0), "doc-sha", "phase", [],
            [new("roof", "roof-unique", TimberCategory.Roof, TimberDisposition.Included, "Roof", ["R"], [], "roof-sha")],
            [new("R", "roof", TimberFaceKind.Roof, new(0, 0, 1), new(1, 0, 0), new(0, 1, 0), new(0, 0, 1),
                [[new(0, 0), new(4, 0), new(4, 4), new(0, 4)], [new(1, 1), new(3, 1), new(3, 3), new(1, 3)]])], [], []);
        var profile = new TimberFoldProfile(ScaleDenominator: 12);
        var draft = TimberFabrication.Draft(source, profile, new("phase", []));
        var panel = draft.Panels.Single();
        // The regular Draft builder already canonicalizes rings. Exercise the
        // corrective normalization this boundary currently performs explicitly.
        var noncanonical = draft with { Panels = [panel with
            { RingsInches = [panel.RingsInches[0], panel.RingsInches[1].Reverse().ToImmutableArray()] }] };
        var records = new List<TimberRoofExposureRecord>(); var diagnostics = new List<TimberDiagnostic>();
        var result = TimberRoofExposure.Apply(noncanonical, source, profile, records, diagnostics, new(), default).Single();
        check("roof exposure corrects a noncanonical hole without losing material", diagnostics.Count == 0 &&
            result.RingsInches.Length == 2 && result.AreaSquareInches == 12);
        var hole = result.RingsInches[1];
        double signedTwiceArea = hole.Zip(hole.Skip(1).Append(hole[0]), (a, b) => a.X * b.Y - a.Y * b.X).Sum();
        check("corrected roof hole is clockwise and has no exposure removal", signedTwiceArea < 0 &&
            records.Single().RemovedAreaSquareInches == 0 && records.Single().ExposedAreaSquareInches == 12);
    }
}
