using System.Text.Json;
using RevitThyme.Core;

namespace RevitThyme.Unit;

internal static class TimberFoldRoofs
{
    public static void Run(Action<string, bool> check)
    {
        using var json = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "irregular-roof-envelope.json")));
        var root = json.RootElement;
        var snapshot = root.GetProperty("snapshot").Deserialize<TimberSnapshot>(Protocol.Json)!;
        var profile = root.GetProperty("profile").Deserialize<TimberFoldProfile>(Protocol.Json)!;
        var scope = root.GetProperty("scope").Deserialize<TimberScope>(Protocol.Json)!;
        var fit = TimberFabrication.Fit(snapshot, profile, scope);
        bool At(string faceId, double xFeet, double yFeet)
        {
            var face = snapshot.Faces.Single(f => f.Id == faceId); var panel = fit.Draft.Panels.Single(p => p.FaceId == faceId);
            double zFeet = face.OriginFeet.Z - (face.Normal.X * (xFeet - face.OriginFeet.X) + face.Normal.Y * (yFeet - face.OriginFeet.Y)) / face.Normal.Z;
            double factor = 12 / profile.ScaleDenominator;
            var origin = fit.Draft.AssemblyOriginFeet;
            var delta = new TimberPoint3((xFeet - origin.X) * factor - panel.Frame.OriginInches.X,
                (yFeet - origin.Y) * factor - panel.Frame.OriginInches.Y, (zFeet - origin.Z) * factor - panel.Frame.OriginInches.Z);
            var p = new TimberPoint2(delta.X * panel.Frame.U.X + delta.Y * panel.Frame.U.Y + delta.Z * panel.Frame.U.Z,
                delta.X * panel.Frame.V.X + delta.Y * panel.Frame.V.Y + delta.Z * panel.Frame.V.Z);
            bool Inside(System.Collections.Immutable.ImmutableArray<TimberPoint2> ring)
            {
                bool inside = false;
                for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
                {
                    var a = ring[i]; var b = ring[j];
                    if ((a.Y > p.Y) != (b.Y > p.Y) && p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
                }
                return inside;
            }
            return Inside(panel.RingsInches[0]) && !panel.RingsInches.Skip(1).Any(Inside);
        }
        check("actual intersecting roof exposure preserves four roof identities and removes only lower finite surfaces",
            fit.Draft.Panels.Count(p => p.Kind == TimberFaceKind.Roof) == 4 &&
            At("R-318356-1", 18, 9) && !At("R-318356-1", 18, 14) &&
            At("R-318374-2", 15, 15) && !At("R-318374-2", 15, 9) &&
            At("R-318374-1", 21, 15) && !At("R-318374-1", 21, 9) &&
            !fit.Diagnostics.Any(d => d.Code == "joint_topology_loss" && d.GeometryId?.StartsWith("R-", StringComparison.Ordinal) == true));
        var expected = new Dictionary<string, double> { ["R-318356-1"] = 10.900831390311476, ["R-318356-2"] = 13.416407864998739,
            ["R-318374-1"] = 6.288941186718159, ["R-318374-2"] = 6.288941186718159 };
        check("actual roof exposure independently worked areas are accounted separately before physical joints",
            fit.RoofExposure.Length == 4 && fit.RoofExposure.All(e => Math.Abs(e.ExposedAreaSquareInches - expected[e.FaceId]) < .000001 &&
                Math.Abs(e.SourceAreaSquareInches - e.ExposedAreaSquareInches - e.RemovedAreaSquareInches) < .00000001 &&
                e.GridAreaDiscrepancy == "0" && e.CellGridEnclosureExcessAreaSum >= 0));
        var shuffled = snapshot with { Sources = snapshot.Sources.Reverse().ToArray(), Faces = snapshot.Faces.Reverse().ToArray(), Openings = snapshot.Openings.Reverse().ToArray() };
        var again = TimberFabrication.Fit(shuffled, profile, scope);
        check("roof envelope and clearance results are invariant under original source enumeration",
            JsonSerializer.Serialize(fit, Protocol.Json) == JsonSerializer.Serialize(again, Protocol.Json));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel(); bool observed = false;
        try { TimberFabrication.Fit(snapshot, profile, scope, cancelled.Token); } catch (OperationCanceledException) { observed = true; }
        check("pre-cancelled actual roof fitting cannot produce a fitted result", observed);
        var wall = fit.Draft.Panels.Single(p => p.FaceId == "W-318326-1");
        var tops = new List<double>();
        var outside = wall.RingsInches[0];
        for (int i = 0; i < outside.Length; i++)
        {
            var a = outside[i]; var b = outside[(i + 1) % outside.Length];
            if (a.X != b.X && 2.975 >= Math.Min(a.X, b.X) && 2.975 <= Math.Max(a.X, b.X))
                tops.Add(a.Y + (2.975 - a.X) * (b.Y - a.Y) / (b.X - a.X));
        }
        check("actual wall top grows to the finite higher covering roof across physical wall depth with clearance",
            tops.Count > 0 && Math.Abs(tops.Max() - 2.5485) <= .00001 && fit.Draft.OpeningMap.Length == 4 &&
            fit.Draft.Panels.Where(p => p.Kind == TimberFaceKind.Wall).Sum(p => p.RingsInches.Length - 1) == 6);
        var narrow = TimberFabrication.Fit(snapshot, profile with { RoofFitSearchInches = .01 }, scope);
        check("actual required wall growth beyond the configured search is blocked with source and panel diagnostics",
            narrow.State == TimberFitState.Blocked && narrow.Diagnostics.Any(d => d.Code == "wall_roof_search" && d.SourceId == "318326" && d.GeometryId == "W-318326-1:P1"));
        var rotated = snapshot with { Faces = snapshot.Faces.Select(f => f.Id != "W-318326-1" ? f : f with
        {
            U = f.V, V = new(-f.U.X, -f.U.Y, -f.U.Z),
            RingsFeet = f.RingsFeet.Select(r => r.Select(p => new TimberPoint2(p.Y, -p.X)).ToArray()).ToArray()
        }).ToArray() };
        var rotatedFit = TimberFabrication.Fit(rotated, profile, scope);
        var rotatedWall = rotatedFit.Draft.Panels.Single(p => p.FaceId == "W-318326-1");
        var heights = new List<double>();
        for (int i = 0; i < rotatedWall.RingsInches[0].Length; i++)
        {
            var a = rotatedWall.RingsInches[0][i]; var b = rotatedWall.RingsInches[0][(i + 1) % rotatedWall.RingsInches[0].Length];
            if (a.Y != b.Y && .025 >= Math.Min(a.Y, b.Y) && .025 <= Math.Max(a.Y, b.Y))
                heights.Add(a.X + (.025 - a.Y) * (b.X - a.X) / (b.Y - a.Y));
        }
        check("actual wall growth follows world-up when the valid wall frame U axis points vertically",
            heights.Count > 0 && Math.Abs(heights.Max() - 2.5485) <= .00001);
        Deliver(check, "irregular", fit, profile, 10, 4);
        Controls(check);
        FiniteRoof(check);
    }
    private static void FiniteRoof(Action<string, bool> check)
    {
        var source = new TimberSnapshot(new(1, "start", "session", "doc", "view", 0), "finite-roof", "phase", [],
            [new("roof", "roof-uid", TimberCategory.Roof, TimberDisposition.Included, "Roof", ["R"], [], "r"),
             new("wall", "wall-uid", TimberCategory.Wall, TimberDisposition.Included, "Wall", ["W"], [], "w")],
            [new("R", "roof", TimberFaceKind.Roof, new(0, 0, 1), new(1, 0, 0), new(0, 1, 0), new(0, 0, 1),
                [[new(0, 0), new(1, 0), new(1, 1), new(0, 1)]]),
             new("W", "wall", TimberFaceKind.Wall, new(0, 0, 0), new(1, 0, 0), new(0, 0, 1), new(0, -1, 0),
                [[new(0, 0), new(2, 0), new(2, .5), new(0, .5)]])], [], []);
        var fit = TimberFabrication.Fit(source, new(ScaleDenominator: 12), new("phase", []));
        var wall = fit.Draft.Panels.Single(p => p.FaceId == "W");
        check("wall growth remains bounded by finite roof material instead of extending its underside plane past the eave",
            fit.State == TimberFitState.GeometryFitted && fit.Verification.Passed && wall.RingsInches[0].Any(p => p.X == 2 && p.Y == .5) &&
            wall.RingsInches[0].Any(p => p.X < 1 && Math.Abs(p.Y - .874) < .0000001) && fit.WallEnvelope.Length == 1);
    }
    private static void Controls(Action<string, bool> check)
    {
        foreach (string name in new[] { "multistorey", "flat" })
        {
            using var json = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", name + "-roof-control.json")));
            var root = json.RootElement;
            var snapshot = root.GetProperty("snapshot").Deserialize<TimberSnapshot>(Protocol.Json)!;
            var profile = root.GetProperty("profile").Deserialize<TimberFoldProfile>(Protocol.Json)!;
            var scope = root.GetProperty("scope").Deserialize<TimberScope>(Protocol.Json)!;
            var fit = TimberFabrication.Fit(snapshot, profile, scope);
            double baseZ = snapshot.Faces.Where(f => f.Kind == TimberFaceKind.Wall).Min(f => f.OriginFeet.Z);
            var ground = snapshot.Faces.Where(f => f.Kind == TimberFaceKind.Wall && f.OriginFeet.Z == baseZ).Select(f => f.Id).ToHashSet();
            check("actual " + name + " storey continuations and parapets do not create distant roof growth or search failures",
                !fit.Diagnostics.Any(d => d.Code == "wall_roof_search") &&
                (name == "flat" ? fit.WallEnvelope.Length == 0 : fit.WallEnvelope.All(e => !ground.Contains(e.FaceId))));
            Deliver(check, name, fit, profile, name == "flat" ? 18 : 10, name == "flat" ? 7 : 5);
        }
    }
    private static void Deliver(Action<string, bool> check, string name, TimberFittedDraft fit, TimberFoldProfile profile, int expectedPanels, int expectedOpenings)
    {
        bool passed = false;
        if (fit.State == TimberFitState.GeometryFitted)
        {
            string directory = Path.Combine(Path.GetTempPath(), "timber-" + name + "-envelope-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var stock = TimberStockLayout.Create(fit.Draft.Panels, profile);
            var cutting = TimberCutFiles.Stage(Path.Combine(directory, "cutting"), fit.Draft.Panels, stock, profile);
            var verified = cutting.Staged ? TimberCutFiles.Verify(cutting.Directory, fit.Draft.Panels, stock, profile) : cutting;
            var guide = TimberGuides.Stage(Path.Combine(directory, "guides"), fit, stock, profile);
            passed = fit.Verification.Passed && fit.Draft.Panels.Length == expectedPanels && fit.Draft.FaceMap.Length == expectedPanels &&
                fit.Draft.OpeningMap.Length == expectedOpenings && stock.Planned && verified.Staged && guide.Staged && !guide.ManufacturingReady && guide.PhysicalChecksOutstanding;
        }
        check("actual " + name + " fixture remains fitted through stock, independent cut readback and mapped guides", passed);
        var proofs = fit.Joints.Select(j => (j.ReceiverPanelId, j.Refinement)).Concat(fit.RoofExposure.Select(e => (e.PanelId, e.Refinement)))
            .Concat(fit.WallEnvelope.Select(e => (e.PanelId, e.Refinement))).Where(p => p.Refinement is not null).ToArray();
        check("actual " + name + " exact boundary ledgers separate quantization from material changes within the cumulative budget",
            proofs.All(item =>
            {
                var p = item.Refinement!;
                var original = System.Numerics.BigInteger.Parse(p.OriginalTwiceGridArea, System.Globalization.CultureInfo.InvariantCulture);
                var adjustment = System.Numerics.BigInteger.Parse(p.SignedTwiceGridAdjustment, System.Globalization.CultureInfo.InvariantCulture);
                var refined = System.Numerics.BigInteger.Parse(p.RefinedTwiceGridArea, System.Globalization.CultureInfo.InvariantCulture);
                var output = System.Numerics.BigInteger.Parse(p.OutputTwiceGridArea, System.Globalization.CultureInfo.InvariantCulture);
                var changed = System.Numerics.BigInteger.Parse(p.ChangedTwiceGridArea, System.Globalization.CultureInfo.InvariantCulture);
                return original + adjustment == refined && (p.Operation == "Difference" ? refined == output + changed : p.Operation == "Union" && refined + changed == output);
            }) && proofs.GroupBy(p => p.Item1).All(g => g.Sum(p => p.Refinement!.MaximumDisplacementGridUnits) <= 500));
    }
}
