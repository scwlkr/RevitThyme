using System.Collections.Immutable;

namespace RevitThyme.Core;

public sealed record TimberStockPlacement(string PanelId, int Sheet, double XInches, double YInches,
    bool QuarterTurn, double WidthInches, double HeightInches, TimberPoint2 LocalMinimum, TimberPoint2 LocalMaximum)
{
    public TimberPoint2 Project(TimberPoint2 point) => QuarterTurn
        ? new(XInches + LocalMaximum.Y - point.Y, YInches + point.X - LocalMinimum.X)
        : new(XInches + point.X - LocalMinimum.X, YInches + point.Y - LocalMinimum.Y);
}
public sealed record TimberStockPlan(int SheetCount, ImmutableArray<TimberStockPlacement> Placements,
    ImmutableArray<TimberDiagnostic> Diagnostics)
{
    public bool Planned => Placements.Length > 0 && Diagnostics.All(d => d.Severity != TimberSeverity.Error);
}

public static class TimberStockLayout
{
    private const double Grid = 100000000;
    private sealed record Rectangle(TimberPanel Panel, TimberPoint2 Minimum, TimberPoint2 Maximum, long Width, long Height);
    private sealed class Shelf(long y, long height) { public long Y = y; public long Height = height; public long X; }
    private sealed class Sheet { public List<Shelf> Shelves = []; }

    public static TimberStockPlan Create(IReadOnlyList<TimberPanel> panels, TimberFoldProfile profile,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var diagnostics = profile.Validate().ToList();
        var placements = new List<TimberStockPlacement>();
        var sheets = new List<Sheet>();
        if (diagnostics.Any(d => d.Severity == TimberSeverity.Error)) return Result();
        if (profile.Grain == TimberGrain.AlongStockHeight && profile.Rotation == TimberRotation.Fixed)
        { diagnostics.Add(new("grain_rotation_conflict", TimberSeverity.Error, "Panel U grain along stock height requires quarter-turn rotation.")); return Result(); }
        if (panels.Count == 0 || panels.Count > 4096 || panels.Any(p => string.IsNullOrWhiteSpace(p.Id)) ||
            panels.Select(p => p.Id).Distinct(StringComparer.Ordinal).Count() != panels.Count)
        { diagnostics.Add(new("layout_inventory", TimberSeverity.Error, "Layout requires 1–4096 panels with distinct nonempty IDs.")); return Result(); }
        long border = (long)Math.Ceiling(profile.MarginInches * Grid), gap = (long)Math.Ceiling(profile.GapInches * Grid);
        long usableWidth = (long)Math.Floor(profile.StockWidthInches * Grid) - 2 * border;
        long usableHeight = (long)Math.Floor(profile.StockHeightInches * Grid) - 2 * border;
        var rectangles = new List<Rectangle>();
        foreach (var panel in panels)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var points = panel.RingsInches.SelectMany(r => r).ToArray();
            if (points.Length < 3 || points.Any(p => !double.IsFinite(p.X) || !double.IsFinite(p.Y) || Math.Abs(p.X) > 1000000 || Math.Abs(p.Y) > 1000000))
            { diagnostics.Add(new("layout_boundary", TimberSeverity.Error, "Panel has no bounded finite contour.", panel.SourceId, panel.Id)); continue; }
            var minimum = new TimberPoint2(points.Min(p => p.X), points.Min(p => p.Y));
            var maximum = new TimberPoint2(points.Max(p => p.X), points.Max(p => p.Y));
            // Round rectangles/gaps/border outward and stock inward. Integer placement
            // keeps every nominal spacing constraint conservative and repeatable.
            long width = (long)Math.Ceiling((maximum.X - minimum.X) * Grid);
            long height = (long)Math.Ceiling((maximum.Y - minimum.Y) * Grid);
            var rectangle = new Rectangle(panel, minimum, maximum, width, height);
            if (width <= 0 || height <= 0 || !Orientations(rectangle).Any(o => o.Width <= usableWidth && o.Height <= usableHeight))
            { diagnostics.Add(new("oversized_panel", TimberSeverity.Error, FormattableString.Invariant($"Panel {panel.Id}: {(maximum.X - minimum.X):G9} × {(maximum.Y - minimum.Y):G9} in exceeds usable stock {usableWidth / Grid:G9} × {usableHeight / Grid:G9} in."), panel.SourceId, panel.Id)); continue; }
            rectangles.Add(rectangle);
        }
        foreach (var rectangle in rectangles.OrderByDescending(r => (Int128)r.Width * r.Height).ThenByDescending(r => r.Width).ThenBy(r => r.Panel.Id, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool placed = false;
            for (int i = 0; i < sheets.Count && !placed; i++) placed = Place(sheets[i], i + 1, rectangle);
            if (!placed) { var sheet = new Sheet(); sheets.Add(sheet); Place(sheet, sheets.Count, rectangle); }
        }
        return Result();

        bool Place(Sheet sheet, int number, Rectangle rectangle)
        {
            foreach (var shelf in sheet.Shelves)
            foreach (var orientation in Orientations(rectangle))
                if (orientation.Height <= shelf.Height && shelf.X + orientation.Width <= usableWidth)
                { Add(number, shelf, rectangle, orientation); return true; }
            long y = sheet.Shelves.Count == 0 ? 0 : sheet.Shelves[^1].Y + sheet.Shelves[^1].Height + gap;
            foreach (var orientation in Orientations(rectangle).OrderBy(o => o.Height).ThenBy(o => o.Width))
            {
                if (orientation.Width > usableWidth || y + orientation.Height > usableHeight) continue;
                var next = new Shelf(y, orientation.Height); sheet.Shelves.Add(next); Add(number, next, rectangle, orientation); return true;
            }
            return false;
        }
        IEnumerable<(long Width, long Height, bool Turn)> Orientations(Rectangle rectangle)
        {
            // Explicit grain means the panel's local U axis; never infer grain from
            // materials or silently rotate it away from the selected stock axis.
            if (profile.Grain != TimberGrain.AlongStockHeight) yield return (rectangle.Width, rectangle.Height, false);
            if (profile.Rotation == TimberRotation.QuarterTurn && profile.Grain != TimberGrain.AlongStockWidth)
                yield return (rectangle.Height, rectangle.Width, true);
        }
        void Add(int sheet, Shelf shelf, Rectangle rectangle, (long Width, long Height, bool Turn) orientation)
        {
            placements.Add(new(rectangle.Panel.Id, sheet, (border + shelf.X) / Grid, (border + shelf.Y) / Grid,
                orientation.Turn, orientation.Width / Grid, orientation.Height / Grid, rectangle.Minimum, rectangle.Maximum));
            shelf.X += orientation.Width + gap;
        }
        TimberStockPlan Result() => new(sheets.Count, placements.ToImmutableArray(), diagnostics.ToImmutableArray());
    }
}
