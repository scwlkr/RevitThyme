using RevitThyme.Core;

namespace RevitThyme.Unit;

internal static class TimberFoldJoints
{
    public static void Run(Action<string, bool> check)
    {
        var snapshot = RoofAndWall(); var profile = new TimberFoldProfile(ScaleDenominator: 12);
        var result = TimberFabrication.Fit(snapshot, profile, new("phase", []));
        var wall = result.Draft.Panels.Single(p => p.Kind == TimberFaceKind.Wall);
        double top = wall.RingsInches[0].Max(p => p.Y);
        check("joint fitting trims a wall to the finite roof underside with positive nominal clearance",
            result.State == TimberFitState.GeometryFitted && result.Verification.Passed && result.Joints.Length == 1 &&
            result.Joints[0].OwnerPanelId == "R:P1" && result.Joints[0].ReceiverPanelId == "W:P1" &&
            top <= .874 && top > .8739999 && Math.Abs(result.Joints[0].RemovedAreaSquareInches - .252) < .0000002 &&
            result.Draft.Panels.Single(p => p.Kind == TimberFaceKind.Roof).AreaSquareInches == 4 && !result.ManufacturingReady);
        WindowLoss(check);
        ContactAndFinite(check);
        OwnerHoles(check);
        FinalizedOwners(check);
        TopologyLoss(check);
        TraceAndRepeatability(check);
        InvalidAndCancelled(check);
        FractionalBoundary(check);
    }
    private static void FractionalBoundary(Action<string, bool> check)
    {
        var source = RoofAndWall();
        var fractional = source with { Faces = [source.Faces[0] with { RingsFeet =
            [[new(0, 0), new(1.0 / 3, 0), new(1.0 / 3, 2), new(0, 2)]] }, source.Faces[1]] };
        var result = TimberFabrication.Fit(fractional, new(ScaleDenominator: 12), new("phase", []));
        var wall = result.Draft.Panels.Single(p => p.FaceId == "W");
        check("fractional owner boundaries are enclosed outward before applying the physical clearance box",
            result.State == TimberFitState.GeometryFitted && result.Verification.Passed &&
            wall.RingsInches[0].Any(p => p.Y < 1 && p.X >= .3343333333333333 && p.X <= .33433335) &&
            result.Joints.Single().CellGridEnclosureExcessAreaSum > 0);
    }
    private static void TraceAndRepeatability(Action<string, bool> check)
    {
        var source = RoofAndWall(); var profile = new TimberFoldProfile(ScaleDenominator: 12);
        var first = TimberFabrication.Fit(source, profile, new("phase", []));
        var shuffled = source with { Sources = source.Sources.Reverse().ToArray(), Faces = source.Faces.Reverse().ToArray() };
        var again = TimberFabrication.Fit(shuffled, profile, new("phase", []));
        check("joint ownership, canonical removed geometry and fitted geometry repeat when source enumeration changes",
            first.State == TimberFitState.GeometryFitted &&
            System.Text.Json.JsonSerializer.Serialize(first) == System.Text.Json.JsonSerializer.Serialize(again));
        var record = first.Joints.Single(); var remainder = first.Draft.Panels.Single(p => p.FaceId == "W");
        var twiceArea = System.Numerics.BigInteger.Parse(record.RemovedTwiceGridArea, System.Globalization.CultureInfo.InvariantCulture);
        check("joint evidence accounts for the complete removed wall strip and records conservative clearance expansion",
            Math.Abs(2 - remainder.AreaSquareInches - record.RemovedAreaSquareInches) < 1e-12 && twiceArea > 0 &&
            record.ClearanceUInches >= .001 && record.ClearanceVInches >= .001 && record.ClearanceDepthInches >= .001 &&
            record.CellGridEnclosureExcessAreaSum >= 0 && record.CellAxisBoxExpansionAreaSum > 0 && record.RemovedRingsInches.Length > 0);
    }
    private static void InvalidAndCancelled(Action<string, bool> check)
    {
        var zero = TimberFabrication.Fit(RoofAndWall(), new(ScaleDenominator: 12, JointClearanceInches: 0), new("phase", []));
        check("zero-clearance settings cannot claim positive-clearance joint qualification", zero.State == TimberFitState.Blocked &&
            zero.Diagnostics.Any(d => d.Code == "joint_clearance"));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel(); bool observed = false;
        try { TimberFabrication.Fit(RoofAndWall(), new(ScaleDenominator: 12), new("phase", []), cancellation.Token); }
        catch (OperationCanceledException) { observed = true; }
        check("cancelled joint fitting cannot produce a fitted result", observed);
    }
    private static void ContactAndFinite(Action<string, bool> check)
    {
        var source = RoofAndWall();
        var touching = source with { Faces = [source.Faces[0], source.Faces[1] with { RingsFeet =
            [[new(0, 0), new(2, 0), new(2, .875), new(0, .875)]] }] };
        var before = TimberFabrication.Draft(touching, new(ScaleDenominator: 12), new("phase", []));
        var contact = TimberFabrication.Fit(touching, new(ScaleDenominator: 12), new("phase", []));
        check("exact material contact still receives positive clearance even when initial collision volume is zero",
            TimberAssemblyVerifier.Verify(before.Panels).Passed && contact.State == TimberFitState.GeometryFitted && contact.Joints.Length == 1 &&
            contact.Draft.Panels.Single(p => p.Kind == TimberFaceKind.Wall).RingsInches[0].Max(p => p.Y) <= .874);
        var finite = source with { Faces = [source.Faces[0] with { RingsFeet =
            [[new(0, 0), new(1.5, 0), new(1.5, 2), new(0, 2)]] }, source.Faces[1]] };
        var bounded = TimberFabrication.Fit(finite, new(ScaleDenominator: 12), new("phase", []));
        var wall = bounded.Draft.Panels.Single(p => p.Kind == TimberFaceKind.Wall);
        check("finite roof contact does not trim the wall beyond the owner's actual footprint and clearance",
            bounded.State == TimberFitState.GeometryFitted && wall.RingsInches[0].Any(p => p.X == 2 && p.Y == 1) &&
            wall.RingsInches[0].Any(p => p.X < 1.5 && p.Y <= .874));
    }
    private static void OwnerHoles(Action<string, bool> check)
    {
        var source = RoofAndWall();
        var holed = source with { Faces = [source.Faces[0] with { RingsFeet = [source.Faces[0].RingsFeet[0],
            [new(.5, .5), new(.5, 1.5), new(1.5, 1.5), new(1.5, .5)]] }, source.Faces[1] with { OriginFeet = new(0, 1, 0) }] };
        var result = TimberFabrication.Fit(holed, new(ScaleDenominator: 12), new("phase", []));
        var wall = result.Draft.Panels.Single(p => p.Kind == TimberFaceKind.Wall);
        check("owner triangulation holes remain absent from conservative joint shadows",
            result.State == TimberFitState.GeometryFitted && result.Verification.Passed &&
            wall.RingsInches[0].Any(p => p.Y == 1 && p.X > .5 && p.X < 1.5));
    }
    private static void FinalizedOwners(Action<string, bool> check)
    {
        var source = RoofAndWall();
        var three = source with { Sources = [source.Sources[0], source.Sources[1], source.Sources[1] with
            { Id = "wall2", UniqueId = "wall2-unique", FaceIds = ["Z"] }], Faces = [source.Faces[0] with { OriginFeet = new(0, .1, 1) },
            source.Faces[1], new("Z", "wall2", TimberFaceKind.Wall, new(1, -1, 0), new(0, 1, 0), new(0, 0, 1), new(1, 0, 0),
                [[new(0, 0), new(1.05, 0), new(1.05, .95), new(0, .95)]])] };
        var result = TimberFabrication.Fit(three, new(ScaleDenominator: 12), new("phase", []));
        var last = result.Draft.Panels.Single(p => p.FaceId == "Z");
        check("later joints use finalized owner material instead of geometry removed by an earlier roof joint",
            result.State == TimberFitState.GeometryFitted && result.Verification.Passed &&
            last.RingsInches[0].Any(p => p.X == 1.05 && p.Y == .95) && result.Joints.Any(j => j.OwnerPanelId == "W:P1" && j.ReceiverPanelId == "Z:P1"));
    }
    private static void TopologyLoss(Action<string, bool> check)
    {
        var source = RoofAndWall();
        var split = source with { Faces = [source.Faces[0] with { OriginFeet = new(0, 0, .5) }, source.Faces[1]] };
        var rejected = TimberFabrication.Fit(split, new(ScaleDenominator: 12), new("phase", []));
        check("a conservative joint that would split a receiver blocks instead of silently creating extra parts",
            rejected.State == TimberFitState.Blocked && rejected.Draft.Panels.Length == 2 && rejected.Diagnostics.Any(d => d.Code == "joint_topology_loss"));
        var erased = source with { Sources = [source.Sources[0], source.Sources[1] with { Category = TimberCategory.Roof }],
            Faces = [source.Faces[0], source.Faces[0] with { Id = "W", SourceId = "wall" }] };
        var gone = TimberFabrication.Fit(erased, new(ScaleDenominator: 12), new("phase", []));
        check("overlapping coincident roofs cannot lose an entire source panel under joint ownership",
            gone.State == TimberFitState.Blocked && gone.Draft.Panels.Length == 2 && gone.Diagnostics.Any(d => d.Code == "joint_topology_loss"));
    }
    private static void WindowLoss(Action<string, bool> check)
    {
        var source = RoofAndWall();
        TimberPoint2[] aperture = [new(.5, .5), new(1.5, .5), new(1.5, .99), new(.5, .99)];
        var window = source with
        {
            Sources = [source.Sources[0], source.Sources[1] with { OpeningIds = ["O"] },
                new("window", "window-unique", TimberCategory.Window, TimberDisposition.Included, "Glazed window", [], ["O"], "window-sha")],
            Faces = [source.Faces[0], source.Faces[1] with { RingsFeet = [source.Faces[1].RingsFeet[0], aperture.Reverse().ToArray()] }],
            Openings = [new("O", "window", "wall", "W", TimberOpeningKind.Window, [aperture], [aperture], true,
                [[new(.99, .5), new(1.01, .5), new(1.01, .99), new(.99, .99)]])]
        };
        var result = TimberFabrication.Fit(window, new(ScaleDenominator: 12, MinimumBarInches: .005), new("phase", []));
        check("joint trims cannot erase retained window bars or expose pane holes while still claiming opening completeness",
            result.State == TimberFitState.Blocked && result.Diagnostics.Any(d => d.Code == "joint_window_loss" && d.GeometryId == "O"));
    }
    private static TimberSnapshot RoofAndWall() => new(new(1, "start", "session", "doc", "view", 0), "doc-sha", "phase", [],
        [new("roof", "roof-unique", TimberCategory.Roof, TimberDisposition.Included, "Planar roof", ["R"], [], "roof-sha"),
            new("wall", "wall-unique", TimberCategory.Wall, TimberDisposition.Included, "Exterior wall", ["W"], [], "wall-sha")],
        [new("R", "roof", TimberFaceKind.Roof, new(0, 0, 1), new(1, 0, 0), new(0, 1, 0), new(0, 0, 1),
            [[new(0, 0), new(2, 0), new(2, 2), new(0, 2)]]),
            new("W", "wall", TimberFaceKind.Wall, new(0, 0, 0), new(1, 0, 0), new(0, 0, 1), new(0, -1, 0),
                [[new(0, 0), new(2, 0), new(2, 1), new(0, 1)]])], [], []);
}
