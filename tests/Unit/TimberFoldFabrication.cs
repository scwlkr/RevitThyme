using RevitThyme.Core;

namespace RevitThyme.Unit;

internal static class TimberFoldFabrication
{
    public static void Run(Action<string, bool> check)
    {
        var draft = Draft(Wall());
        var panel = draft.Panels.FirstOrDefault();
        check("fabrication converts a 20 by 10 foot wall at 1:48 into a five by two-and-a-half inch panel",
            draft.State == TimberDraftState.ContoursValidated && panel is { AreaSquareInches: 12.5, ThicknessInches: 0.125 } &&
            panel.RingsInches[0].SequenceEqual(new TimberPoint2[] { new(0, 0), new(5, 0), new(5, 2.5), new(0, 2.5) }) &&
            draft.FaceMap is [{ SourceId: "1", FaceId: "F1", PanelIds.Length: 1 }] && !draft.ManufacturingReady);
        var holed = Wall();
        holed = holed with { Faces = [holed.Faces[0] with { RingsFeet = [holed.Faces[0].RingsFeet[0],
            [new(2, 2), new(2, 6), new(6, 6), new(6, 2)]] }] };
        var holePanel = Draft(holed).Panels.Single();
        check("perforated panels triangulate without filling a four-foot square hole",
            holePanel.AreaSquareInches == 11.5 && holePanel.RingsInches.Length == 2 && holePanel.Triangles.Length > 0 &&
            Math.Abs(holePanel.Triangles.Sum(TriangleArea) - 11.5) < 1e-12 &&
            holePanel.Triangles.All(t => { double x = (t.A.X + t.B.X + t.C.X) / 3, y = (t.A.Y + t.B.Y + t.C.Y) / 3;
                return !(x > .5 && x < 1.5 && y > .5 && y < 1.5); }));
        var window = Window();
        var windowDraft = Draft(window);
        check("native window aperture is restored around glazing cuts so its central bar survives",
            windowDraft.State == TimberDraftState.ContoursValidated && windowDraft.Panels is [{ AreaSquareInches: 11.5625, RingsInches.Length: 3 }] &&
            windowDraft.OpeningMap is [{ SourceId: "2", OpeningId: "O2", HostFaceId: "F1", PanelIds.Length: 1 }]);
        var crossed = Wall();
        crossed = crossed with { Faces = [crossed.Faces[0] with { RingsFeet =
            [[new(0, 0), new(20, 10), new(0, 10), new(20, 0), new(20, -5)]] }] };
        var crossedDraft = Draft(crossed);
        check("self-intersecting source contours are blocked instead of silently repaired by polygon union",
            crossedDraft.State == TimberDraftState.Blocked && crossedDraft.Diagnostics.Any(d => d.Code == "invalid_contour" && d.GeometryId == "F1"));
        var narrowWindow = window with { Openings = [window.Openings[0] with { OpaqueRingsFeet =
            [[new(3.9375, 2), new(4.0625, 2), new(4.0625, 6), new(3.9375, 6)]] }] };
        var narrow = Draft(narrowWindow);
        check("a one-thirty-second-inch retained window web blocks the nominal one-sixteenth-inch profile",
            narrow.State == TimberDraftState.Blocked && narrow.Diagnostics.Any(d => d.Code == "window_web" && d.GeometryId == "O2"));
        SignedFrame(check, window);
        Concavity(check);
        OpeningMap(check);
        Repeatability(check);
        Blocking(check, window);
        NativeDoorNotch(check);
        ActualPanelWebs(check);
    }
    private static void ActualPanelWebs(Action<string, bool> check)
    {
        var window = Window();
        TimberPoint2[] aperture = [new(.125, .125), new(19.875, .125), new(19.875, 9.875), new(.125, 9.875)];
        var framed = window with { Faces = [window.Faces[0] with { RingsFeet = [window.Faces[0].RingsFeet[0], aperture.Reverse().ToArray()] }],
            Openings = [window.Openings[0] with { VoidRingsFeet = [aperture], PaneRingsFeet = [aperture], OpaqueRingsFeet = [] }] };
        var thin = Draft(framed);
        check("a single glazing cut leaving only a one-thirty-second-inch outer panel frame blocks fabrication",
            thin.State == TimberDraftState.Blocked && thin.Diagnostics.Any(d => d.Code == "panel_web" && d.GeometryId == "F1"));
        TimberPoint2[] nearby = [new(6.125, 2), new(10.125, 2), new(10.125, 6), new(6.125, 6)];
        var pair = window with
        {
            Sources = [window.Sources[0] with { OpeningIds = ["O2", "O3"] }, window.Sources[1],
                window.Sources[1] with { Id = "3", UniqueId = "window3", OpeningIds = ["O3"] }],
            Faces = [window.Faces[0] with { RingsFeet = [.. window.Faces[0].RingsFeet, nearby.Reverse().ToArray()] }],
            Openings = [window.Openings[0] with { OpaqueRingsFeet = [] },
                new("O3", "3", "1", "F1", TimberOpeningKind.Window, [nearby], [nearby], true, [])]
        };
        var crowded = Draft(pair);
        check("narrow retained material between different native openings is checked on the actual panel",
            crowded.State == TimberDraftState.Blocked && crowded.Diagnostics.Any(d => d.Code == "panel_web"));
    }
    private static void NativeDoorNotch(Action<string, bool> check)
    {
        var wall = Wall();
        var door = wall with
        {
            Sources = [wall.Sources[0] with { OpeningIds = ["door"] }, new("2", "door-source", TimberCategory.Opening,
                TimberDisposition.Included, "Native ground doorway", [], ["door"], "door-sha")],
            Faces = [wall.Faces[0] with { RingsFeet = [[new(0, 0), new(8, 0), new(8, 7), new(11, 7),
                new(11, 0), new(20, 0), new(20, 10), new(0, 10)]] }],
            Openings = [new("door", "2", "1", "F1", TimberOpeningKind.Door,
                [[new(8, 0), new(11, 0), new(11, 7), new(8, 7)]], [], true)]
        };
        var draft = Draft(door);
        check("native ground-door notches retain their cut geometry and positive-length source correspondence",
            draft.State == TimberDraftState.ContoursValidated && draft.Panels is [{ AreaSquareInches: 11.1875, RingsInches.Length: 1 }] &&
            draft.OpeningMap is [{ PanelIds.Length: 1 }]);
        var external = door with { Openings = [door.Openings[0] with { VoidRingsFeet =
            [[new(20, 10), new(21, 10), new(21, 11), new(20, 11)]] }] };
        var absent = Draft(external);
        check("an external cutout touching only a wall corner cannot claim opening correspondence",
            absent.State == TimberDraftState.Blocked && absent.Diagnostics.Any(d => d.Code == "unaccounted_opening"));
    }
    private static void Repeatability(Action<string, bool> check)
    {
        var original = Wall();
        original = original with { Sources = [original.Sources[0], original.Sources[0] with { Id = "3", UniqueId = "third-source", FaceIds = ["F3"] }],
            Faces = [original.Faces[0], original.Faces[0] with { Id = "F3", SourceId = "3", OriginFeet = new(20, 0, 0) }] };
        var first = Draft(original);
        var translated = original with { Sources = original.Sources.Reverse().ToArray(), Faces = original.Faces.Reverse().Select(f => f with
        {
            OriginFeet = new(f.OriginFeet.X + 1000000000, f.OriginFeet.Y - 1000000000, f.OriginFeet.Z + 1000000000),
            RingsFeet = [f.RingsFeet[0].Reverse().Skip(1).Concat(f.RingsFeet[0].Reverse().Take(1)).ToArray()]
        }).ToArray() };
        var second = Draft(translated);
        check("canonical panels and source maps repeat after source reordering, ring reversal and billion-foot translation",
            first.State == TimberDraftState.ContoursValidated && second.State == TimberDraftState.ContoursValidated &&
            System.Text.Json.JsonSerializer.Serialize(first.Panels) == System.Text.Json.JsonSerializer.Serialize(second.Panels) &&
            System.Text.Json.JsonSerializer.Serialize(first.FaceMap) == System.Text.Json.JsonSerializer.Serialize(second.FaceMap) &&
            second.Panels[1].Frame.OriginInches == new TimberPoint3(5, 0, 0));
    }
    private static void Blocking(Action<string, bool> check, TimberSnapshot window)
    {
        var unresolved = Draft(window with { Openings = [window.Openings[0] with { GlazingResolved = false }] });
        check("unresolved glazing cannot obtain a contour draft or invented panes", unresolved.State == TimberDraftState.Blocked && unresolved.Panels.Length == 0 &&
            unresolved.Diagnostics.Any(d => d.Code == "unresolved_glazing"));
        var escaped = Draft(window with { Openings = [window.Openings[0] with { PaneRingsFeet =
            [[new(2, 2), new(7, 2), new(7, 6), new(2, 6)]] }] });
        check("pane projections outside their native aperture block correspondence", escaped.State == TimberDraftState.Blocked && escaped.Diagnostics.Any(d => d.Code == "invalid_contour"));
        var sliver = Wall();
        sliver = sliver with { Faces = [sliver.Faces[0] with { RingsFeet =
            [[new(0, 0), new(.00004, 0), new(.00004, .00004), new(0, .00004)]] }] };
        var tiny = Draft(sliver);
        check("unresolved contour-tolerance slivers block rather than passing with zero or near-zero parts",
            tiny.State == TimberDraftState.Blocked && tiny.Panels.Length == 0 && tiny.Diagnostics.Any(d => d.Code == "invalid_contour"));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        bool observed = false;
        try { TimberFabrication.Draft(Wall(), new(), new("phase", []), cancelled.Token); }
        catch (OperationCanceledException) { observed = true; }
        check("cancelled fabrication does not return a successful draft", observed);
    }
    private static void OpeningMap(Action<string, bool> check)
    {
        var wall = Wall();
        wall = wall with
        {
            Sources = [wall.Sources[0] with { OpeningIds = ["split", "left"] },
                new("2", "split-source", TimberCategory.Opening, TimberDisposition.Included, "Splitting opening", [], ["split"], "split-sha"),
                new("3", "left-source", TimberCategory.Opening, TimberDisposition.Included, "Left pane opening", [], ["left"], "left-sha")],
            Openings = [new("split", "2", "1", "F1", TimberOpeningKind.Door,
                    [[new(9, 0), new(11, 0), new(11, 10), new(9, 10)]], [], true),
                new("left", "3", "1", "F1", TimberOpeningKind.Door,
                    [[new(2, 2), new(4, 2), new(4, 4), new(2, 4)]], [], true)]
        };
        var draft = Draft(wall);
        check("opening provenance identifies only the connected parts affected by each cut",
            draft.State == TimberDraftState.ContoursValidated && draft.Panels.Length == 2 &&
            draft.OpeningMap.Single(o => o.OpeningId == "split").PanelIds.Length == 2 &&
            draft.OpeningMap.Single(o => o.OpeningId == "left").PanelIds.SequenceEqual(new[] { "F1:P1" }));
    }
    private static void SignedFrame(Action<string, bool> check, TimberSnapshot window)
    {
        var framed = window with { Openings = [window.Openings[0] with { OpaqueRingsFeet =
            [window.Openings[0].VoidRingsFeet[0], [new(2.25, 2.25), new(2.25, 5.75), new(5.75, 5.75), new(5.75, 2.25)]] }] };
        var draft = Draft(framed);
        check("signed opaque frame holes stay transparent instead of being filled as opaque polygons",
            draft.State == TimberDraftState.ContoursValidated && draft.Panels is [{ AreaSquareInches: 11.734375, RingsInches.Length: 2 }] &&
            draft.Panels[0].Triangles.All(t => {
                double x = (t.A.X + t.B.X + t.C.X) / 3, y = (t.A.Y + t.B.Y + t.C.Y) / 3;
                return !(x > .5625 && x < 1.4375 && y > .5625 && y < 1.4375); }));
    }
    private static void Concavity(Action<string, bool> check)
    {
        var source = Wall();
        var concave = source with { Faces = [source.Faces[0] with { RingsFeet =
            [[new(0, 0), new(20, 0), new(20, 5), new(10, 5), new(10, 10), new(0, 10)]] }] };
        var panel = Draft(concave).Panels.Single();
        check("concave L-shaped panels triangulate without bridging the absent upper-right rectangle",
            panel.AreaSquareInches == 9.375 && Math.Abs(panel.Triangles.Sum(TriangleArea) - 9.375) < 1e-12 &&
            panel.Triangles.All(t => (t.A.X + t.B.X + t.C.X) / 3 <= 2.5 || (t.A.Y + t.B.Y + t.C.Y) / 3 <= 1.25));
    }
    private static double TriangleArea(TimberTriangle t) => Math.Abs((t.B.X - t.A.X) * (t.C.Y - t.A.Y) - (t.B.Y - t.A.Y) * (t.C.X - t.A.X)) / 2;
    private static TimberPanelDraft Draft(TimberSnapshot snapshot) => TimberFabrication.Draft(snapshot, new(), new("phase", []));
    private static TimberSnapshot Window()
    {
        var wall = Wall(); TimberPoint2[] aperture = [new(2, 2), new(6, 2), new(6, 6), new(2, 6)];
        return wall with
        {
            Sources = [wall.Sources[0] with { OpeningIds = ["O2"] }, new("2", "unique-window", TimberCategory.Window,
                TimberDisposition.Included, "Native glazed window", [], ["O2"], "window-sha")],
            Faces = [wall.Faces[0] with { RingsFeet = [wall.Faces[0].RingsFeet[0], aperture.Reverse().ToArray()] }],
            Openings = [new("O2", "2", "1", "F1", TimberOpeningKind.Window, [aperture], [aperture], true,
                [[new(3.875, 2), new(4.125, 2), new(4.125, 6), new(3.875, 6)]])]
        };
    }
    private static TimberSnapshot Wall() => new(new(1, "start", "session", "doc", "view", 0), "document-sha", "phase", [],
        [new("1", "unique-1", TimberCategory.Wall, TimberDisposition.Included, "Exterior basic wall", ["F1"], [], "source-sha")],
        [new("F1", "1", TimberFaceKind.Wall, new(0, 0, 0), new(1, 0, 0), new(0, 0, 1), new(0, -1, 0),
            [[new(0, 0), new(20, 0), new(20, 10), new(0, 10)]])], [], []);
}
