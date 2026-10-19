using RevitThyme.Core;

namespace RevitThyme.Unit;

internal static class TimberFoldStock
{
    public static void Run(Action<string, bool> check)
    {
        var layout = TimberStockLayout.Create([Panel("A", 6, 3)], new());
        check("stock layout places every panel once inside the configured border", layout.Planned && layout.SheetCount == 1 &&
            layout.Placements is [{ PanelId: "A", Sheet: 1, XInches: 0.25, YInches: 0.25, QuarterTurn: false }]);
        layout = TimberStockLayout.Create([Panel("E", 6, 3), Panel("D", 6, 3), Panel("C", 6, 3), Panel("B", 6, 3), Panel("A", 6, 3)],
            new(StockWidthInches: 13, StockHeightInches: 7));
        check("stock layout keeps spacing and accounts for all panels across new sheets", layout.Planned && layout.SheetCount == 2 &&
            layout.Placements is [
                { PanelId: "A", Sheet: 1, XInches: 0.25, YInches: 0.25 },
                { PanelId: "B", Sheet: 1, XInches: 6.375, YInches: 0.25 },
                { PanelId: "C", Sheet: 1, XInches: 0.25, YInches: 3.375 },
                { PanelId: "D", Sheet: 1, XInches: 6.375, YInches: 3.375 },
                { PanelId: "E", Sheet: 2, XInches: 0.25, YInches: 0.25 }]);
        var tallStock = new TimberFoldProfile(StockWidthInches: 8, StockHeightInches: 22);
        layout = TimberStockLayout.Create([Panel("rotate", 20, 6)], tallStock);
        check("quarter-turn stock fit preserves physical size and its contour transform", layout.Planned && layout.Placements is
            [{ QuarterTurn: true, WidthInches: 6, HeightInches: 20 }] && layout.Placements[0].Project(new(20, 6)) == new TimberPoint2(0.25, 20.25) &&
            !TimberStockLayout.Create([Panel("rotate", 20, 6)], tallStock with { Rotation = TimberRotation.Fixed }).Planned);
        layout = TimberStockLayout.Create([Panel("grain", 6, 3)], new(Grain: TimberGrain.AlongStockHeight));
        check("explicit panel-U grain constrains rotation and rejects incompatible fixed stock", layout.Planned &&
            layout.Placements[0].QuarterTurn &&
            !TimberStockLayout.Create([Panel("grain", 6, 3)], new(Grain: TimberGrain.AlongStockHeight, Rotation: TimberRotation.Fixed)).Planned &&
            !TimberStockLayout.Create([Panel("grain", 20, 6)], tallStock with { Grain = TimberGrain.AlongStockWidth }).Planned);
        check("stock rejects missing panel identities and reports oversized physical dimensions",
            !TimberStockLayout.Create([Panel("", 6, 3)], new()).Planned &&
            TimberStockLayout.Create([Panel("oversize", 30, 20)], new()) is { Planned: false, Diagnostics: [{ Code: "oversized_panel", GeometryId: "oversize" }] } oversized &&
            oversized.Diagnostics[0].Message.Contains("30", StringComparison.Ordinal));
    }

    internal static TimberPanel Panel(string id, double width, double height) => new(id, id, id, TimberFaceKind.Wall,
        new(new(0, 0, 0), new(1, 0, 0), new(0, 1, 0), new(0, 0, 1)), 0.125,
        [[new(0, 0), new(width, 0), new(width, height), new(0, height)]],
        [new(new(0, 0), new(width, 0), new(width, height)), new(new(0, 0), new(width, height), new(0, height))], width * height);
}
