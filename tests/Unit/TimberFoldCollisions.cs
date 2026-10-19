using RevitThyme.Core;
using System.Collections.Immutable;

namespace RevitThyme.Unit;

internal static class TimberFoldCollisions
{
    public static void Run(Action<string, bool> check)
    {
        var empty = TimberAssemblyVerifier.Verify([]);
        check("empty assembly inventory is unresolved rather than collision-qualified",
            !empty.Passed && empty.Collisions.IsEmpty && empty.Unresolved is [{ Code: "empty_inventory" }]);
        var first = Rectangle("first", 0, 0, 2, 2, .125);
        var second = Rectangle("second", 0, 0, 2, 2, .125);
        var report = TimberAssemblyVerifier.Verify([first, second]);
        check("exact assembly verification detects half a cubic inch of coincident material",
            !report.Passed && report.Unresolved.Length == 0 && report.Collisions is [{ VolumeCubicInches: .5,
                VolumeNumerator: "1", VolumeDenominator: "2" }]);
        check("assembly verification reports every offending panel pair instead of stopping at the first",
            TimberAssemblyVerifier.Verify([first, second, first with { Id = "third" }]).Collisions.Length == 3);
        Contacts(check, first);
        Threshold(check);
        WallAndSkew(check, first);
        Hollow(check);
        Recovery(check, first);
        Performance(check);
    }

    private static void Contacts(Action<string, bool> check, TimberPanel first)
    {
        var edge = Rectangle("edge", 2, 0, 2, 2, .125);
        var vertex = Rectangle("vertex", 2, 2, 2, 2, .125);
        var face = Rectangle("face", 0, 0, 2, 2, .125);
        face = face with { Frame = face.Frame with { OriginInches = new(0, 0, -.125) } };
        check("exact face edge and vertex contacts have zero material-intersection volume",
            new[] { edge, vertex, face }.All(other => TimberAssemblyVerifier.Verify([first, other]).Passed));
    }
    private static void Threshold(Action<string, bool> check)
    {
        // The binary double 0.000001 is below exact 1/1000000; its immediate successor is above.
        var below = Rectangle("a", 0, 0, 1, 1, .000001);
        var above = below with { ThicknessInches = Math.BitIncrement(.000001) };
        check("collision gate compares exact rationals across adjacent doubles around one millionth cubic inch",
            TimberAssemblyVerifier.Verify([below, below with { Id = "b" }]).Passed &&
            TimberAssemblyVerifier.Verify([above, above with { Id = "b" }]).Collisions.Length == 1);
        var square = Rectangle("square", 0, 0, 1, 1, 2);
        var wedge = new TimberPanel("wedge", "wedge", "wedge", TimberFaceKind.Roof, square.Frame, 2,
            [[new(0, 0), new(1, 1000000), new(0, 1000000)]],
            [new(new(0, 0), new(1, 1000000), new(0, 1000000))], 500000);
        // x <= y / 1000000 within the unit square has area 1/2000000; depth 2 gives exact gate equality.
        var wider = wedge with { RingsInches = [[new(0, 0), new(Math.BitIncrement(1), 1000000), new(0, 1000000)]],
            Triangles = [new(new(0, 0), new(Math.BitIncrement(1), 1000000), new(0, 1000000))] };
        check("exact one-millionth cubic inch contact volume passes while an immediately wider rational wedge fails",
            TimberAssemblyVerifier.Verify([square, wedge]).Passed && TimberAssemblyVerifier.Verify([square, wider]).Collisions.Length == 1);
        var divided = Rectangle("many", 0, 0, 4, 1, .0000005);
        divided = divided with { Triangles = Enumerable.Range(0, 8).SelectMany(i => Cells(i * .5, 0, .5, 1)).ToImmutableArray() };
        var whole = Rectangle("whole", 0, 0, 4, 1, .0000005);
        var sum = TimberAssemblyVerifier.Verify([divided, whole]);
        check("sixteen individually subthreshold cell overlaps are summed before the panel-pair collision gate",
            sum.Collisions.Length == 1 && sum.Unresolved.Length == 0 && sum.CandidateCellPairs >= 16 &&
            Math.Abs(sum.Collisions[0].VolumeCubicInches - .000002) < 1e-18);
    }
    private static void WallAndSkew(Action<string, bool> check, TimberPanel first)
    {
        var roof = Rectangle("roof", 0, 0, 2, 2, .25);
        var wall = Rectangle("wall", 0, 0, 2, 2, .25);
        wall = wall with { Kind = TimberFaceKind.Wall, Frame = new(new(0, 1, -1), new(1, 0, 0), new(0, 0, 1), new(0, -1, 0)) };
        var crossing = TimberAssemblyVerifier.Verify([roof, wall]);
        check("perpendicular wall roof prisms have the analytic one-eighth cubic inch intersection",
            crossing.Unresolved.Length == 0 && crossing.Collisions is [{ VolumeNumerator: "1", VolumeDenominator: "8" }]);
        var sloped = roof with { Frame = roof.Frame with { U = new(1, 0, .5), OutwardNormal = new(-.5, 0, 1) } };
        var roofCut = TimberAssemblyVerifier.Verify([sloped, wall]);
        check("oblique roof wall clipping gives the analytic 155 over 1024 cubic inch overlap",
            roofCut.Unresolved.Length == 0 && roofCut.Collisions is [{ VolumeNumerator: "155", VolumeDenominator: "1024" }]);
        var skew = first with { Frame = first.Frame with { V = new(.5, 1, 0) } };
        var skewed = TimberAssemblyVerifier.Verify([skew, skew with { Id = "skew-copy" }]);
        check("given affine frame geometry is used without silently renormalizing a skew basis",
            skewed.Unresolved.Length == 0 && skewed.Collisions is [{ VolumeNumerator: "1", VolumeDenominator: "2" }]);
        double tilt = Math.ScaleB(1, -40);
        var tilted = first with { Frame = first.Frame with { U = new(1, 0, tilt), OutwardNormal = new(-tilt, 0, 1) } };
        var nearParallel = TimberAssemblyVerifier.Verify([tilted, tilted with { Id = "tilted-copy" }]);
        check("near parallel affine planes remain exact and retain their tiny nonunit determinant",
            nearParallel.Unresolved.Length == 0 && nearParallel.Collisions.Length == 1 &&
            nearParallel.Collisions[0].VolumeDenominator != "2");
    }
    private static void Hollow(Action<string, bool> check)
    {
        var hollow = HollowPanel();
        var aperture = Rectangle("inside-hole", 1.25, 1.25, 1, 1, .125);
        var report = TimberAssemblyVerifier.Verify([hollow, aperture]);
        check("material inside a perforated panel's empty aperture has no collision", report.Passed && report.CandidateCellPairs == 0);
        var filled = hollow with { Triangles = Cells(0, 0, 4, 4) };
        check("a tessellation that fills a contour hole is unresolved rather than trusted as panel material",
            TimberAssemblyVerifier.Verify([filled, aperture]).Unresolved.Any(d => d.Code == "invalid_partition"));
        var concealed = Rectangle("concealed-hole", 0, 0, 6, 6, .125);
        concealed = concealed with
        {
            RingsInches = [concealed.RingsInches[0], [new(1, 1), new(2, 2), new(2, 1)]],
            Triangles = Cells(1, 1, 4, 4).Concat(Enumerable.Range(0, 6).SelectMany(i => Cells(i, 0, 1, 1)).Skip(1))
                .Concat(Cells(0, 5, 6, 1)).Concat(Cells(0, 1, 1, 4)).Concat(Cells(5, 1, 1, 4)).ToImmutableArray(),
            AreaSquareInches = 35.5
        };
        check("equal area cannot conceal a filled hole whose vertices lie on triangle edges and missing material elsewhere",
            TimberAssemblyVerifier.Verify([concealed]).Unresolved.Any(d => d.Code == "invalid_partition"));
    }
    private static void Recovery(Action<string, bool> check, TimberPanel first)
    {
        var missing = first with { Triangles = [first.Triangles[0]] };
        check("incomplete triangle partitions cannot produce successful assembly verification",
            !TimberAssemblyVerifier.Verify([missing]).Passed);
        var repeated = first with { Triangles = [first.Triangles[0], first.Triangles[0]] };
        check("equal total triangle area cannot conceal overlapping cells and missing source material",
            TimberAssemblyVerifier.Verify([repeated]).Unresolved.Any(d => d.Code == "invalid_partition"));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        var stopped = TimberAssemblyVerifier.Verify([first, first with { Id = "cancelled-copy" }], cancelled.Token);
        check("cancelled exact verification reports unresolved pair identities instead of zero overlap",
            !stopped.Passed && stopped.Unresolved is [{ Code: "cancelled", FirstPanelId: not null, SecondPanelId: not null }]);
        var excessive = first with { Triangles = Enumerable.Repeat(first.Triangles[0], 4097).ToImmutableArray() };
        check("triangle resource limits remain unresolved instead of an apparent collision-free result",
            TimberAssemblyVerifier.Verify([first, excessive with { Id = "limited" }]).Unresolved.Any(d => d.Code == "invalid_panel"));
    }
    private static void Performance(Action<string, bool> check)
    {
        var panels = Enumerable.Range(0, 100).Select(i => Rectangle("panel-" + i, i * 3, 0, 2, 2, .125)).ToArray();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var report = TimberAssemblyVerifier.Verify(panels); watch.Stop();
        check("100 isolated panels verify all 4950 pairs using zero exact intersection candidates",
            report.Passed && report.PanelPairsChecked == 4950 && report.CandidateCellPairs == 0);
        Console.WriteLine($"Exact collision benchmark: 100 isolated panels, {watch.ElapsedMilliseconds} ms.");
        var arc = Rectangle("arc", 0, 0, 4, 4, .125);
        var ring = Enumerable.Range(0, 32).Select(i => new TimberPoint2(2 * Math.Cos(i * Math.PI / 16), 2 * Math.Sin(i * Math.PI / 16))).ToArray();
        arc = arc with { RingsInches = [ring.ToImmutableArray()], Triangles = Enumerable.Range(0, 32).Select(i =>
            new TimberTriangle(new(0, 0), ring[i], ring[(i + 1) % 32])).ToImmutableArray() };
        var arcPanels = Enumerable.Range(0, 100).Select(i => arc with { Id = "arc-" + i,
            Frame = arc.Frame with { OriginInches = new(i * 6, 0, 0) } }).ToArray();
        watch.Restart(); var arcs = TimberAssemblyVerifier.Verify(arcPanels); watch.Stop();
        check("100 arc-derived canonical polygon panels retain exact disjoint collision qualification",
            arcs.Passed && arcs.PanelPairsChecked == 4950 && arcs.CandidateCellPairs == 0);
        Console.WriteLine($"Exact collision benchmark: 100 panels with 32 arc-derived triangles each, {watch.ElapsedMilliseconds} ms.");
        var hollow = HollowPanel();
        var perforated = Enumerable.Range(0, 100).Select(i => hollow with { Id = "hollow-" + i,
            Frame = hollow.Frame with { OriginInches = new(i * 6, 0, 0) } }).ToArray();
        watch.Restart(); var holes = TimberAssemblyVerifier.Verify(perforated); watch.Stop();
        check("100 perforated panels verify their complete triangle partitions and every separated panel pair",
            holes.Passed && holes.PanelPairsChecked == 4950 && holes.CandidateCellPairs == 0);
        Console.WriteLine($"Exact collision benchmark: 100 perforated panels, {watch.ElapsedMilliseconds} ms.");
    }

    private static TimberPanel HollowPanel()
    {
        var panel = Rectangle("hollow", 0, 0, 4, 4, .125);
        return panel with
        {
            RingsInches = [panel.RingsInches[0], [new(1, 1), new(1, 3), new(3, 3), new(3, 1)]],
            Triangles = Cells(0, 0, 4, 1).Concat(Cells(0, 3, 4, 1)).Concat(Cells(0, 1, 1, 2)).Concat(Cells(3, 1, 1, 2)).ToImmutableArray(),
            AreaSquareInches = 12
        };
    }

    private static ImmutableArray<TimberTriangle> Cells(double x, double y, double width, double height) =>
        [new(new(x, y), new(x + width, y), new(x + width, y + height)),
            new(new(x, y), new(x + width, y + height), new(x, y + height))];

    private static TimberPanel Rectangle(string id, double x, double y, double width, double height, double thickness) =>
        new(id, id, id, TimberFaceKind.Roof, new(new(x, y, 0), new(1, 0, 0), new(0, 1, 0), new(0, 0, 1)), thickness,
            [[new(0, 0), new(width, 0), new(width, height), new(0, height)]],
            Cells(0, 0, width, height), width * height);
}
