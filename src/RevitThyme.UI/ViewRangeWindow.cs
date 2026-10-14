using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using RevitThyme.Core;

namespace RevitThyme.UI;

public sealed class ViewRangeWindow : UtilityWindow
{
    private sealed record LevelChoice(string Id, string Name) { public override string ToString() => Name; }
    private readonly ComboBox units = new() { ItemsSource = new[] { "ft/in", "mm", "m" }, SelectedIndex = 0, Width = 82 };
    private readonly ComboBox[] levels = Enumerable.Range(0, 4).Select(_ => new ComboBox { MinWidth = 150, Height = 26 }).ToArray();
    private readonly TextBox[] offsets = Enumerable.Range(0, 4).Select(_ => new TextBox { Width = 106, Height = 26, Padding = new(4, 2, 4, 2) }).ToArray();
    private readonly Canvas section = new() { Width = 180, Height = 200, Background = Brushes.WhiteSmoke };
    private readonly TextBlock viewName = new() { Margin = new(0, 0, 0, 12), TextTrimming = TextTrimming.CharacterEllipsis };
    private ViewRangeInfo? info;
    private string unit = "ft/in";
    private string[] shown = new string[4];
    private bool filling;
    public ViewRangeInfo? CurrentRange => info;
    public ViewRangeWindow(IOperationClient client) : base(client)
    {
        Title = "RevitThyme — View Range"; Width = 620; Height = 360;
        var root = new StackPanel { Margin = new(16) }; Content = root; root.Children.Add(viewName);
        var body = new Grid(); body.ColumnDefinitions.Add(new() { Width = new(196) }); body.ColumnDefinitions.Add(new());
        body.Children.Add(section); var controls = new StackPanel(); Grid.SetColumn(controls, 1); body.Children.Add(controls);
        var header = new DockPanel { Margin = new(0, 0, 0, 8) }; header.Children.Add(new TextBlock { Text = "Level and offset", VerticalAlignment = VerticalAlignment.Center });
        units.HorizontalAlignment = HorizontalAlignment.Right; header.Children.Add(units); controls.Children.Add(header);
        string[] names = ["Top", "Cut plane", "Bottom", "View depth"];
        for (int i = 0; i < 4; i++)
        {
            int index = i;
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, 0, 0, 10) };
            row.Children.Add(new Label { Content = names[i], Width = 76, Target = offsets[i], Padding = new(0, 4, 0, 0) });
            row.Children.Add(levels[i]); offsets[i].Margin = new(8, 0, 0, 0); row.Children.Add(offsets[i]); controls.Children.Add(row);
            levels[i].SelectionChanged += (_, _) => { offsets[index].IsEnabled = (levels[index].SelectedItem as LevelChoice)?.Id != "Unlimited"; Draw(); };
            offsets[i].TextChanged += (_, _) => Draw();
            offsets[i].PreviewKeyDown += (_, e) =>
            {
                if (e.Key is not (Key.Up or Key.Down)) return;
                try { offsets[index].Text = LengthText.Format(ReadOffset(index) + (e.Key == Key.Up ? 1 : -1) / 96.0, unit); e.Handled = true; }
                catch (FormatException error) { Status.Text = error.Message; }
            };
        }
        root.Children.Add(body); root.Children.Add(Status); root.Children.Add(Buttons());
        units.SelectionChanged += (_, _) =>
        {
            if (filling) return;
            if (info is null) { unit = (string)units.SelectedItem; return; }
            try { var settings = ReadSettings(); unit = (string)units.SelectedItem; Fill(settings); }
            catch (FormatException error) { Status.Text = error.Message; filling = true; units.SelectedItem = unit; filling = false; }
        };
        RefreshButton.Click += (_, _) => Click(RefreshAsync); ApplyButton.Click += (_, _) => Click(() => ApplyAsync());
        Loaded += (_, _) => Click(RefreshAsync);
    }
    public Task<OperationResult> RefreshAsync() => Run(async () =>
    {
        info = null; var context = await Inspect(); if (!context.Succeeded) return context;
        var result = await Client.ExecuteAsync(new(Guid.NewGuid(), OperationKind.GetViewRange, Snapshot!.Target));
        info = result.ViewRange;
        if (info is not null)
        {
            viewName.Text = result.Snapshot!.ViewName + " · " + result.Snapshot.ViewType;
            var choices = new[] { new LevelChoice("Current", "Associated level"), new("Above", "Level above"), new("Below", "Level below"), new("Unlimited", "Unlimited") }
                .Concat(info.Levels.Select(l => new LevelChoice(l.Id, l.Name))).ToArray();
            filling = true; foreach (var combo in levels) combo.ItemsSource = choices; filling = false; Fill(info.Settings);
            if (!info.CanEdit) result = result with { Message = info.EditBlock! };
        }
        return result;
    });
    public Task<OperationResult> ApplyAsync(ViewRangeSettings? settings = null) => Run(async () =>
    {
        if (Snapshot is null || info is null) throw new InvalidOperationException("Refresh the view range first.");
        var result = await Client.ExecuteAsync(new(Guid.NewGuid(), OperationKind.SetViewRange, Snapshot.Target,
            ExpectedRange: info.Settings, Range: settings ?? ReadSettings()));
        info = result.ViewRange; if (info is not null) Fill(info.Settings); return result;
    });
    private void Fill(ViewRangeSettings settings)
    {
        filling = true; var planes = new[] { settings.Top, settings.Cut, settings.Bottom, settings.Depth };
        for (int i = 0; i < 4; i++)
        {
            levels[i].SelectedItem = levels[i].Items.Cast<LevelChoice>().Single(l => l.Id == planes[i].Level);
            shown[i] = offsets[i].Text = LengthText.Format(planes[i].OffsetFeet, unit);
        }
        // Exact values stay behind rounded text until that field is edited.
        displayed = planes; filling = false; Draw();
    }
    private RangePlane[] displayed = [];
    private double ReadOffset(int i) => displayed.Length == 4 && offsets[i].Text == shown[i] ? displayed[i].OffsetFeet : LengthText.Parse(offsets[i].Text, unit);
    private ViewRangeSettings ReadSettings()
    {
        var p = Enumerable.Range(0, 4).Select(i => new RangePlane(((LevelChoice)levels[i].SelectedItem).Id, ReadOffset(i))).ToArray();
        return new(p[0], p[1], p[2], p[3]);
    }
    protected override void UpdateApply() => ApplyButton.IsEnabled = !IsBusy && Snapshot?.CanModify == true && info?.CanEdit == true;
    private void Draw()
    {
        if (filling || info is null) return;
        section.Children.Clear();
        try
        {
            var settings = ReadSettings(); var p = new[] { settings.Top, settings.Cut, settings.Bottom, settings.Depth };
            double? Elevation(RangePlane plane)
            {
                double current = info.AssociatedLevelFeet;
                double? level = plane.Level switch { "Unlimited" => null, "Current" => current,
                    "Above" => info.Levels.Where(l => l.ElevationFeet > current).Select(l => (double?)l.ElevationFeet).FirstOrDefault(),
                    "Below" => info.Levels.Where(l => l.ElevationFeet < current).Select(l => (double?)l.ElevationFeet).LastOrDefault(),
                    _ => info.Levels.FirstOrDefault(l => l.Id == plane.Level)?.ElevationFeet };
                return level + plane.OffsetFeet;
            }
            var heights = p.Select(Elevation).ToArray(); var finite = heights.Where(h => h.HasValue).Select(h => h!.Value).ToArray();
            if (finite.Length == 0) return; double min = finite.Min() - 1, max = finite.Max() + 1;
            string[] names = ["Top", "Cut", "Bottom", "Depth"]; Brush[] colors = [Brushes.SlateGray, Brushes.Firebrick, Brushes.SlateGray, Brushes.SteelBlue];
            for (int i = 0; i < 4; i++)
            {
                double y = heights[i].HasValue ? 20 + (max - heights[i]!.Value) / (max - min) * 155 : i == 0 ? 10 : 190;
                section.Children.Add(new Line { X1 = 8, X2 = 174, Y1 = y, Y2 = y, Stroke = colors[i], StrokeThickness = i == 1 ? 2 : 1 });
                var label = new TextBlock { Text = names[i] + (p[i].Level == "Unlimited" ? " ∞" : ""), Foreground = colors[i], Background = Brushes.WhiteSmoke };
                Canvas.SetLeft(label, 10); Canvas.SetTop(label, y - 17); section.Children.Add(label);
            }
        }
        catch (Exception error) when (error is FormatException or InvalidOperationException or NullReferenceException) { Status.Text = error.Message; }
    }
}
