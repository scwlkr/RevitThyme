using RevitThyme.Core;
using System.Text.Json;

namespace RevitThyme.Unit;

internal static class TimberFoldFragility
{
    internal static void Run(Action<string, bool> check)
    {
        var narrow = Draft(Bridge(.03125));
        check("outer-only one-thirty-second-inch bridge between bulk lobes blocks with a certified neck diagnostic",
            narrow.State == TimberDraftState.Blocked && narrow.Diagnostics.Any(d => d.Code == "panel_neck" && d.GeometryId == "F"));
        var tip = Draft([new(0, 0), new(1, 0), new(1, .375), new(3, .5), new(1, .625), new(1, 1), new(0, 1)]);
        check("a long narrow outer tapered tip is a source-mapped warning without claiming physical strength or blocking fitting",
            tip.State == TimberDraftState.ContoursValidated && tip.Diagnostics.Any(d => d.Code == "panel_tip" && d.Severity == TimberSeverity.Warning && d.GeometryId == "F"));
        var nominal = Draft(Bridge(.0625)); var allowance = Draft(Bridge(.06249)); var below = Draft(Bridge(.06248998));
        check("nominal outer neck width and exact numeric-allowance boundary remain accepted while a narrower grid witness blocks",
            nominal.State == TimberDraftState.ContoursValidated && allowance.State == TimberDraftState.ContoursValidated &&
            below.State == TimberDraftState.Blocked && below.Diagnostics.Any(d => d.Code == "panel_neck"));
        foreach (var (name, ring) in new (string, TimberPoint2[])[]
        {
            ("rectangle", [new(0, 0), new(3, 0), new(3, 1), new(0, 1)]),
            ("gable", [new(0, 0), new(3, 0), new(3, 1), new(1.5, 1.75), new(0, 1)]),
            ("tiny bevel", [new(.005, 0), new(3, 0), new(3, 1), new(0, 1), new(0, .005)])
        })
        {
            var ordinary = Draft(ring);
            check("ordinary " + name + " corners are neither outer necks nor long narrow tips", ordinary.State == TimberDraftState.ContoursValidated &&
                !ordinary.Diagnostics.Any(d => d.Code is "panel_neck" or "panel_tip" or "panel_fragility_unresolved"));
        }
        var fitted = TimberFabrication.Fit(Snapshot([new(0, 0), new(1, 0), new(1, .375), new(3, .5), new(1, .625), new(1, 1), new(0, 1)]),
            new(ScaleDenominator: 12), new("phase", []));
        check("warning-only tapered tip survives the shared fitting seam without a false neck error",
            fitted.State == TimberFitState.GeometryFitted && fitted.Diagnostics.Any(d => d.Code == "panel_tip") && !fitted.Diagnostics.Any(d => d.Code == "panel_neck") && !fitted.ManufacturingReady);
        var air = Draft([new(0, 0), new(3, 0), new(3, 3), new(1.515625, 3), new(1.515625, 1),
            new(1.484375, 1), new(1.484375, 3), new(0, 3)]);
        check("a narrow exterior air notch cannot serve as a certified material-neck connector",
            air.State == TimberDraftState.ContoursValidated && !air.Diagnostics.Any(d => d.Code == "panel_neck"));
        var segmented = Draft([new(0, 0), new(1, 0), new(1, .375), new(2.99, .499375), new(3, .5),
            new(2.99, .500625), new(1, .625), new(1, 1), new(0, 1)]);
        check("subdividing a long straight tip boundary does not hide the tapered-tip warning",
            segmented.State == TimberDraftState.ContoursValidated && segmented.Diagnostics.Any(d => d.Code == "panel_tip"));
        var moved = Draft(Bridge(.03125).Select(p => new TimberPoint2(100 - p.Y, 200 + p.X)).ToArray());
        check("certified outer necks remain blocking after rigid rotation and local rebase",
            moved.State == TimberDraftState.Blocked && moved.Diagnostics.Any(d => d.Code == "panel_neck"));
        var complex = new List<TimberPoint2> { new(0, 0), new(160, 0), new(160, 1) };
        for (int i = 159; i >= 0; i--) complex.AddRange([new(i + .75, 1), new(i + .75, 2), new(i + .25, 2), new(i + .25, 1)]);
        complex.Add(new(0, 1)); var exhausted = Draft(complex.ToArray());
        check("bounded outer-detail search exhaustion is an explicit blocking unresolved diagnostic",
            exhausted.State == TimberDraftState.Blocked && exhausted.Diagnostics.Any(d => d.Code == "panel_fragility_unresolved" && d.Message.Contains("250000")));
        GableControl(check);
    }
    private static void GableControl(Action<string, bool> check)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "gable-roof-control.json")));
        var snapshot = fixture.RootElement.GetProperty("snapshot").Deserialize<TimberSnapshot>(Protocol.Json)!;
        var profile = fixture.RootElement.GetProperty("profile").Deserialize<TimberFoldProfile>(Protocol.Json)!;
        var scope = fixture.RootElement.GetProperty("scope").Deserialize<TimberScope>(Protocol.Json)!;
        var original = JsonSerializer.Serialize(snapshot, Protocol.Json);
        var fit = TimberFabrication.Fit(snapshot, profile, scope); bool passed = false;
        if (fit.State == TimberFitState.GeometryFitted)
        {
            var stock = TimberStockLayout.Create(fit.Draft.Panels, profile);
            var directory = Path.Combine(Path.GetTempPath(), "timber-gable-fragility-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var cuts = TimberCutFiles.Stage(Path.Combine(directory, "cutting"), fit.Draft.Panels, stock, profile);
            var verified = cuts.Staged ? TimberCutFiles.Verify(cuts.Directory, fit.Draft.Panels, stock, profile) : cuts;
            var guides = TimberGuides.Stage(Path.Combine(directory, "guides"), fit, stock, profile);
            passed = stock.Planned && verified.Staged && guides.Staged && guides.PhysicalChecksOutstanding;
        }
        check("actual original gable retains six panels and three opening maps through fitting, stock, cut readback and guides",
            passed && fit.Verification.Passed && fit.Draft.Panels.Length == 6 && fit.Draft.FaceMap.Length == 6 && fit.Draft.OpeningMap.Length == 3 &&
            original == JsonSerializer.Serialize(snapshot, Protocol.Json) && !fit.ManufacturingReady);
    }
    private static TimberPoint2[] Bridge(double width) =>
        [new(0, 0), new(1, 0), new(1, .5 - width / 2), new(2, .5 - width / 2), new(2, 0), new(3, 0),
         new(3, 1), new(2, 1), new(2, .5 + width / 2), new(1, .5 + width / 2), new(1, 1), new(0, 1)];
    private static TimberPanelDraft Draft(TimberPoint2[] ring) => TimberFabrication.Draft(Snapshot(ring), new(ScaleDenominator: 12), new("phase", []));
    private static TimberSnapshot Snapshot(TimberPoint2[] ring) => new(new(1, "start", "session", "doc", "view", 0), "source", "phase", [],
        [new("1", "wall", TimberCategory.Wall, TimberDisposition.Included, "Authored fragility wall", ["F"], [], "s")],
        [new("F", "1", TimberFaceKind.Wall, new(0, 0, 0), new(1, 0, 0), new(0, 0, 1), new(0, -1, 0), [ring])], [], []);
}
