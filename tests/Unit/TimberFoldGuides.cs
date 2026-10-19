using RevitThyme.Core;
using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Xml.Linq;

namespace RevitThyme.Unit;

internal static class TimberFoldGuides
{
    public static void Run(Action<string, bool> check)
    {
        string parent = Path.Combine(Path.GetTempPath(), "timber-guides-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(parent);
        var panel = TimberFoldStock.Panel("wall:P1", 2, 1) with
        {
            SourceId = "wall-source", FaceId = "wall-face",
            Frame = new(new(10, 20, 30), new(1, 0, 0), new(0, 1, 0), new(0, 0, 1))
        };
        var draft = new TimberPanelDraft(TimberDraftState.ContoursValidated, new(0, 0, 0), [panel],
            [new(panel.SourceId, panel.FaceId, [panel.Id])], [], []);
        var fitted = new TimberFittedDraft(TimberFitState.GeometryFitted, draft, [], TimberAssemblyVerifier.Verify([panel]), []);
        var stock = TimberStockLayout.Create([panel], new());
        var result = TimberGuides.Stage(Path.Combine(parent, "first"), fitted, stock, new());
        check("guide stages source-mapped stock and assembly reference drawings with visible physical caveats", result.Staged &&
            result.Drawings.Length == 2 && result.Labels.Any(l => l.PanelId == panel.Id && l.SourceId == "wall-source" && l.FaceId == "wall-face") &&
            !result.ManufacturingReady && result.PhysicalChecksOutstanding &&
            File.ReadAllText(Path.Combine(result.Directory, "GUIDE.svg")).Contains("Physical checks outstanding", StringComparison.Ordinal));
        bool rejected = false;
        string missingMapDirectory = Path.Combine(parent, "missing-map");
        try
        {
            var invalid = TimberGuides.Stage(missingMapDirectory, fitted with { Draft = draft with { FaceMap = default } }, stock, new());
            rejected = !invalid.Staged && invalid.Diagnostics.Any(d => d.Severity == TimberSeverity.Error) && !Directory.Exists(missingMapDirectory);
        }
        catch (Exception) { /* A malformed inventory must produce a diagnostic, not escape staging. */ }
        check("guide rejects an uninitialized source map with a diagnostic before writing", rejected);
        var first = TimberFoldStock.Panel("A", .25, .25);
        var second = first with { Id = "B", SourceId = "B", FaceId = "B", Frame = first.Frame with { OriginInches = new(1, 1, 1) } };
        var overlappingProjection = Fit([first, second]);
        var callouts = TimberGuides.Stage(Path.Combine(parent, "callouts"), overlappingProjection,
            TimberStockLayout.Create(overlappingProjection.Draft.Panels, new()), new());
        var partLabels = callouts.Labels.Where(l => l.GuideFile == "ASSEMBLY-REFERENCE.dxf" && l.PanelId is not null).ToArray();
        check("guide native callouts remain separated when distinct 3D parts share the same projection", callouts.Staged &&
            !partLabels.SelectMany((l, i) => partLabels.Skip(i + 1).Select(other => (l, other))).Any(pair =>
                Math.Abs(pair.l.PhysicalPositionInches.X - pair.other.PhysicalPositionInches.X) < .5 &&
                Math.Abs(pair.l.PhysicalPositionInches.Y - pair.other.PhysicalPositionInches.Y) < .25));
        var assembly = Polylines(Path.Combine(result.Directory, "ASSEMBLY-REFERENCE.dxf"));
        check("guide orthonormal camera projects literal world positions and inward material depth", assembly.Count >= 6 &&
            Near(assembly[0][0].X, -7.071067811865475) && Near(assembly[0][0].Y, 12.247448713915892) &&
            Near(assembly[0][1].X, -5.65685424949238) && Near(assembly[0][1].Y, 11.430952132988166) &&
            Near(assembly[1][0].Y, 12.145386641299926));
        var stockRings = Polylines(Path.Combine(result.Directory, "STOCK-REFERENCE-001.dxf"));
        check("guide reference preserves literal physical stock border and canonical placed panel", stockRings.Count >= 3 &&
            stockRings[0].SequenceEqual([new(0, 0), new(24, 0), new(24, 18), new(0, 18)]) &&
            stockRings[1].SequenceEqual([new(.25, .25), new(23.75, .25), new(23.75, 17.75), new(.25, 17.75)]) &&
            stockRings[2].SequenceEqual([new(.25, .25), new(2.25, .25), new(2.25, 1.25), new(.25, 1.25)]));
        check("guide DXFs contain only closed zero-width reference polylines and inch units", result.Drawings.All(d => ReferenceOnly(Path.Combine(result.Directory, d.File))));
        check("guide drawing envelopes include every vector and native label", result.Drawings.All(d =>
            Polylines(Path.Combine(result.Directory, d.File)).SelectMany(r => r).Concat(result.Labels.Where(l => l.GuideFile == d.File).Select(l => l.PhysicalPositionInches))
                .All(p => p.X >= d.MinimumInches.X && p.Y >= d.MinimumInches.Y && p.X <= d.MaximumInches.X && p.Y <= d.MaximumInches.Y)));
        var repeat = TimberGuides.Stage(Path.Combine(parent, "repeat"), fitted, stock, new());
        check("guide complete bytes and receipts repeat in a new directory with real saved hashes", repeat.Staged &&
            result.Files.SequenceEqual(repeat.Files) && result.Files.All(f =>
                f.Bytes == new FileInfo(Path.Combine(result.Directory, f.Name)).Length &&
                f.Sha256 == Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(result.Directory, f.Name)))) &&
                File.ReadAllBytes(Path.Combine(result.Directory, f.Name)).SequenceEqual(File.ReadAllBytes(Path.Combine(repeat.Directory, f.Name)))) &&
            !File.ReadAllText(Path.Combine(result.Directory, "guide-staging.json")).Contains(parent, StringComparison.OrdinalIgnoreCase));
        var shuffled = Fit([second, first]);
        var reordered = TimberGuides.Stage(Path.Combine(parent, "reordered"), shuffled, TimberStockLayout.Create(shuffled.Draft.Panels, new()), new());
        check("guide aliases, drawings and canonical receipt survive source enumeration changes", callouts.Files.SequenceEqual(reordered.Files));
        var before = result.Files.Select(f => File.ReadAllBytes(Path.Combine(result.Directory, f.Name))).ToArray();
        var protectedResult = TimberGuides.Stage(result.Directory, fitted, stock, new());
        check("guide cannot replace or adopt an existing output directory", !protectedResult.Staged && result.Files.Select((f, i) =>
            File.ReadAllBytes(Path.Combine(result.Directory, f.Name)).SequenceEqual(before[i])).All(value => value));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel(); bool cancelled = false;
        string cancelledDirectory = Path.Combine(parent, "cancelled");
        try { TimberGuides.Stage(cancelledDirectory, fitted, stock, new(), cancellation.Token); }
        catch (OperationCanceledException) { cancelled = true; }
        check("guide cancellation is observed before any output appears", cancelled && !Directory.Exists(cancelledDirectory));
        var errors = new (string Name, TimberFittedDraft Fitted, TimberStockPlan Stock)[]
        {
            ("blocked fit", fitted with { State = TimberFitState.Blocked }, stock),
            ("blocked draft", fitted with { Draft = draft with { State = TimberDraftState.Blocked } }, stock),
            ("missing part mapping", fitted with { Draft = draft with { FaceMap = [] } }, stock),
            ("mismatched source mapping", fitted with { Draft = draft with { FaceMap = [new("wrong", panel.FaceId, [panel.Id])] } }, stock),
            ("duplicate source mapping", fitted with { Draft = draft with { FaceMap = [draft.FaceMap[0], draft.FaceMap[0]] } }, stock),
            ("missing stock part", fitted, stock with { Placements = [] }),
            ("modified stock transform", fitted, stock with { Placements = [stock.Placements[0] with { XInches = 10 }] }),
            ("unresolved verification", fitted with { Verification = new([], [new(null, null, "unresolved", "unresolved")], 0, 0) }, stock),
            ("unknown opening host", fitted with { Draft = draft with { OpeningMap = [new("opening-source", "O1", "unknown", [panel.Id])] } }, stock),
            ("unknown joint owner", fitted with { Joints = [new("unknown", panel.Id, [[new(0, 0), new(1, 0), new(1, 1)]], .5, "1", .001, .001, .001, 0, 0)] }, stock)
        };
        foreach (var invalid in errors)
        {
            string target = Path.Combine(parent, "invalid-" + Guid.NewGuid().ToString("N"));
            var blocked = TimberGuides.Stage(target, invalid.Fitted, invalid.Stock, new());
            check("guide rejects incomplete inventory before writing: " + invalid.Name,
                !blocked.Staged && blocked.Diagnostics.Any(d => d.Severity == TimberSeverity.Error) && !Directory.Exists(target));
        }
        var forged = Fit([first, first with { Id = "overlap", SourceId = "overlap", FaceId = "overlap" }]) with { Verification = new([], [], 0, 0) };
        string forgedDirectory = Path.Combine(parent, "forged");
        check("guide independently blocks overlapping solids despite a forged passed flag", !TimberGuides.Stage(forgedDirectory,
            forged, TimberStockLayout.Create(forged.Draft.Panels, new()), new()).Staged && !Directory.Exists(forgedDirectory));
        var xml = XDocument.Load(Path.Combine(result.Directory, "GUIDE.svg"));
        string text = string.Join("\n", xml.Descendants().Where(e => e.Name.LocalName == "text").Select(e => e.Value));
        check("guide visible legend maps stable aliases to actual panel source and face identities", text.Contains("P001", StringComparison.Ordinal) &&
            text.Contains("Panel: wall:P1", StringComparison.Ordinal) && text.Contains("Source: wall-source | Face: wall-face", StringComparison.Ordinal) &&
            text.Contains("+Z up", StringComparison.Ordinal) && text.Contains("No fitted joints recorded", StringComparison.Ordinal));
        var smallProfile = new TimberFoldProfile(ScaleDenominator: 12, StockWidthInches: 2.5, StockHeightInches: 2.5, MarginInches: .125);
        var joined = TimberFabrication.Fit(RoofAndWall(), smallProfile, new("phase", []));
        var joinedStock = TimberStockLayout.Create(joined.Draft.Panels, smallProfile);
        var joinedGuides = TimberGuides.Stage(Path.Combine(parent, "joined"), joined, joinedStock, smallProfile);
        string joinedSvg = XDocument.Load(Path.Combine(joinedGuides.Directory, "GUIDE.svg")).Root!.Value;
        check("guide uses actual fitted joint ownership across every stock sheet", joinedGuides.Staged && joinedStock.SheetCount == 2 &&
            joinedGuides.Drawings.Count(d => d.Role == "Stock") == 2 && joinedGuides.Drawings.Single(d => d.Role == "Assembly").PanelIds.Length == 2 &&
            joinedSvg.Contains("1. P001 owns contact; P002 receives the fitted trim.", StringComparison.Ordinal) &&
            joinedSvg.Contains("Owner source: roof | Receiver source: wall", StringComparison.Ordinal));
        string roofFile = FormattableString.Invariant($"STOCK-REFERENCE-{joinedStock.Placements.Single(p => p.PanelId == "R:P1").Sheet:000}.dxf");
        var expectedHole = new HashSet<TimberPoint2> { new(.625, .625), new(.625, 1.625), new(1.625, 1.625), new(1.625, .625) };
        check("guide preserves a literal hole contour rather than filling the roof aperture", Polylines(Path.Combine(joinedGuides.Directory, roofFile))
            .Any(ring => ring.Count == 4 && expectedHole.SetEquals(ring)));
        var anchor = Polylines(Path.Combine(joinedGuides.Directory, roofFile))[^1][0];
        check("guide part leader anchors actual retained material rather than a hole center",
            !(anchor.X > .625 && anchor.X < 1.625 && anchor.Y > .625 && anchor.Y < 1.625));
        var escapedPanel = panel with { Id = "P<&>\"", SourceId = "S<&>\"", FaceId = "F<&>\"" };
        var escapedDraft = Fit([escapedPanel]);
        var escaped = TimberGuides.Stage(Path.Combine(parent, "escaped"), escapedDraft, TimberStockLayout.Create([escapedPanel], new()), new());
        check("guide preserves literal identities safely in SVG XML", escaped.Staged && XDocument.Load(Path.Combine(escaped.Directory, "GUIDE.svg")).Root!.Value
            .Contains("Panel: P<&>\"", StringComparison.Ordinal));
    }

    private static TimberFittedDraft Fit(ImmutableArray<TimberPanel> panels) => new(TimberFitState.GeometryFitted,
        new(TimberDraftState.ContoursValidated, new(0, 0, 0), panels,
            panels.Select(p => new TimberFacePartMap(p.SourceId, p.FaceId, [p.Id])).ToImmutableArray(), [], []), [], TimberAssemblyVerifier.Verify(panels), []);
    private static bool Near(double actual, double expected) => Math.Abs(actual - expected) < 1e-12;
    private static List<List<TimberPoint2>> Polylines(string path)
    {
        var lines = File.ReadAllLines(path); var result = new List<List<TimberPoint2>>(); List<TimberPoint2>? ring = null;
        for (int i = 0; i + 1 < lines.Length; i += 2)
        {
            int code = int.Parse(lines[i], CultureInfo.InvariantCulture); string value = lines[i + 1];
            if (code == 0) { ring = value == "LWPOLYLINE" ? [] : null; if (ring is not null) result.Add(ring); }
            if (ring is not null && code == 10)
            {
                if (lines[i + 2].Trim() != "20") throw new InvalidDataException("Expected planar vertex pair.");
                ring.Add(new(double.Parse(value, CultureInfo.InvariantCulture), double.Parse(lines[i + 3], CultureInfo.InvariantCulture)));
            }
        }
        return result;
    }
    private static bool ReferenceOnly(string path)
    {
        var lines = File.ReadAllLines(path); bool entities = false, polyline = false, inches = false;
        for (int i = 0; i + 1 < lines.Length; i += 2)
        {
            int code = int.Parse(lines[i], CultureInfo.InvariantCulture); string value = lines[i + 1];
            if (code == 9 && value == "$INSUNITS") inches = lines[i + 2].Trim() == "70" && lines[i + 3].Trim() == "1";
            if (code == 2 && value == "ENTITIES") entities = true;
            if (code == 0 && value == "ENDSEC") entities = false;
            if (!entities) continue;
            if (code == 0) { if (value != "LWPOLYLINE") return false; polyline = true; }
            if (polyline && code == 8 && value != "REFERENCE") return false;
            if (polyline && code == 70 && value.Trim() != "1") return false;
            if (polyline && code is 38 or 39 or 40 or 41 or 42 or 43 && double.Parse(value, CultureInfo.InvariantCulture) != 0) return false;
        }
        return inches && Polylines(path).Count > 0;
    }
    private static TimberSnapshot RoofAndWall() => new(new(1, "start", "session", "doc", "view", 0), "doc-sha", "phase", [],
        [new("roof", "roof-unique", TimberCategory.Roof, TimberDisposition.Included, "Planar roof", ["R"], [], "roof-sha"),
            new("wall", "wall-unique", TimberCategory.Wall, TimberDisposition.Included, "Exterior wall", ["W"], [], "wall-sha")],
        [new("R", "roof", TimberFaceKind.Roof, new(0, 0, 1), new(1, 0, 0), new(0, 1, 0), new(0, 0, 1),
            [[new(0, 0), new(2, 0), new(2, 2), new(0, 2)], [new(.5, .5), new(.5, 1.5), new(1.5, 1.5), new(1.5, .5)]]),
            new("W", "wall", TimberFaceKind.Wall, new(0, 0, 0), new(1, 0, 0), new(0, 0, 1), new(0, -1, 0),
                [[new(0, 0), new(2, 0), new(2, 1), new(0, 1)]])], [], []);
}
