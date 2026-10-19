using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using RevitThyme.Core;

namespace RevitThyme.UI;

public sealed class TimberFoldWindow : Window
{
    private readonly IOperationClient client;
    private readonly Dictionary<string, TextBox> numbers = [];
    private readonly ComboBox rotation = new() { ItemsSource = Enum.GetValues<TimberRotation>(), SelectedIndex = 1, MinWidth = 100 };
    private readonly ComboBox grain = new() { ItemsSource = Enum.GetValues<TimberGrain>(), SelectedIndex = 0, MinWidth = 120 };
    private readonly CheckBox measuredThickness = new() { Content = "Thickness measured" };
    private readonly CheckBox measuredStock = new() { Content = "Stock measured" };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 10, 0, 8) };
    private readonly TextBox diagnostics = new() { IsReadOnly = true, AcceptsReturn = true,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto, TextWrapping = TextWrapping.Wrap, Height = 146 };
    private readonly Button inspect = new() { Content = "_Inspect", Width = 84, Height = 27 };
    private readonly Button openFiles = new() { Content = "Open _files", Width = 84, Height = 27, Margin = new(0, 0, 8, 0), IsEnabled = false };
    public TimberFoldReply? CurrentInspection { get; private set; }

    public TimberFoldWindow(IOperationClient client)
    {
        this.client = client;
        Title = "RevitThyme — TimberFold"; Width = 510; Height = 570;
        ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new("Segoe UI"); FontSize = 12; Background = SystemColors.ControlBrush;
        var layout = new Grid(); layout.RowDefinitions.Add(new()); layout.RowDefinitions.Add(new() { Height = GridLength.Auto }); Content = layout;
        var root = new StackPanel { Margin = new(16, 16, 16, 0) };
        layout.Children.Add(new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        root.Children.Add(new TextBlock { Text = "Exterior walls and planar roofs", Margin = new(0, 0, 0, 12) });
        var profile = new TimberFoldProfile();
        AddPair(root, "Scale 1:", "scale", profile.ScaleDenominator, "Thickness (in)", "thickness", profile.ThicknessInches);
        AddPair(root, "Stock width (in)", "width", profile.StockWidthInches, "Height (in)", "height", profile.StockHeightInches);
        AddPair(root, "Border (in)", "margin", profile.MarginInches, "Spacing (in)", "gap", profile.GapInches);
        AddPair(root, "Min. bar (in)", "bar", profile.MinimumBarInches, "Chord error (in)", "chord", profile.ChordToleranceInches);
        AddPair(root, "Joint gap (in)", "joint", profile.JointClearanceInches, "Roof search (in)", "roof", profile.RoofFitSearchInches);
        var constraints = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, 2, 0, 8) };
        constraints.Children.Add(new Label { Content = "Rotation", Padding = new(0, 3, 8, 0) }); constraints.Children.Add(rotation);
        constraints.Children.Add(new Label { Content = "Grain", Margin = new(16, 0, 0, 0), Padding = new(0, 3, 8, 0) }); constraints.Children.Add(grain);
        root.Children.Add(constraints);
        var measurements = new StackPanel { Orientation = Orientation.Horizontal };
        measuredThickness.Margin = new(0, 0, 20, 0); measurements.Children.Add(measuredThickness); measurements.Children.Add(measuredStock);
        root.Children.Add(measurements);
        root.Children.Add(new TextBlock { Text = "Kerf compensation: laser software. Physical cutting and assembly remain untested.",
            TextWrapping = TextWrapping.Wrap, Margin = new(0, 10, 0, 0) });
        root.Children.Add(status); root.Children.Add(diagnostics);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(16, 12, 16, 16) };
        buttons.Children.Add(openFiles); buttons.Children.Add(inspect);
        openFiles.Click += (_, _) =>
        {
            if (CurrentInspection?.Evidence is { } evidence)
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(evidence.Directory) { UseShellExecute = true });
        };
        var close = new Button { Content = "Close", Width = 84, Height = 27, Margin = new(8, 0, 0, 0) };
        close.Click += (_, _) => Close(); buttons.Children.Add(close); Grid.SetRow(buttons, 1); layout.Children.Add(buttons);
        inspect.Click += async (_, _) => await InspectAsync();
        Loaded += async (_, _) => await InspectAsync();
    }

    private void AddPair(StackPanel root, string left, string leftId, double leftValue, string right, string rightId, double rightValue)
    {
        var row = new Grid { Margin = new(0, 0, 0, 8) };
        foreach (double width in new[] { 112.0, 94, 18, 100, 94 }) row.ColumnDefinitions.Add(new() { Width = new(width) });
        AddField(row, left, leftId, leftValue, 0); AddField(row, right, rightId, rightValue, 3); root.Children.Add(row);
    }

    private void AddField(Grid row, string label, string id, double value, int column)
    {
        var input = new TextBox { Text = value.ToString("0.########", CultureInfo.CurrentCulture), Height = 26, Padding = new(4, 2, 4, 2) };
        numbers.Add(id, input);
        var caption = new Label { Content = label, Target = input, Padding = new(0, 4, 0, 0) };
        Grid.SetColumn(caption, column); row.Children.Add(caption);
        Grid.SetColumn(input, column + 1); row.Children.Add(input);
    }

    private TimberFoldProfile ReadProfile()
    {
        double Read(string id) => double.Parse(numbers[id].Text, CultureInfo.CurrentCulture);
        return new(ScaleDenominator: Read("scale"), StockWidthInches: Read("width"), StockHeightInches: Read("height"),
            ThicknessInches: Read("thickness"), MarginInches: Read("margin"), GapInches: Read("gap"), MinimumBarInches: Read("bar"),
            ChordToleranceInches: Read("chord"), JointClearanceInches: Read("joint"), RoofFitSearchInches: Read("roof"),
            ThicknessMeasured: measuredThickness.IsChecked == true, StockMeasured: measuredStock.IsChecked == true,
            Rotation: (TimberRotation)rotation.SelectedItem, Grain: (TimberGrain)grain.SelectedItem);
    }

    public async Task<OperationResult?> InspectAsync()
    {
        if (!inspect.IsEnabled) return null;
        inspect.IsEnabled = openFiles.IsEnabled = false; CurrentInspection = null;
        try
        {
            var profile = ReadProfile();
            var context = await client.ExecuteAsync(new(Guid.NewGuid(), OperationKind.Inspect));
            var result = context.Succeeded && context.Snapshot is { } snapshot
                ? await client.ExecuteAsync(new(Guid.NewGuid(), OperationKind.InspectTimberFold, snapshot.Target, TimberFold: new(profile))) : context;
            CurrentInspection = result.TimberFold; status.Text = result.Message;
            if (result.TimberFold is { } timber)
            {
                var info = timber.Inspection;
                string options = info.DesignOptionIds.Length == 0 ? "Main model" : "Main + options " + string.Join(", ", info.DesignOptionIds);
                if (info.DesignOptionsTruncated) options += " (see files for all)";
                status.Text = $"{result.Snapshot?.DocumentTitle} · Phase {info.PhaseId}\n{options}\n{info.FaceCount} faces · {info.OpeningCount} openings · {info.ErrorCount} errors · {info.WarningCount} warnings";
                diagnostics.Text = string.Join(Environment.NewLine, info.Diagnostics.Select(d => $"{d.Severity} [{d.SourceId ?? "scope"}]: {d.Message}"));
                diagnostics.Text += Environment.NewLine + string.Join(Environment.NewLine,
                    info.SourceSample.Select(s => $"{s.Id} · {s.Category} · {s.Disposition}: {s.Reason}"));
                if (info.DiagnosticsTruncated) diagnostics.Text += $"\nShowing {info.Diagnostics.Length} of {info.ErrorCount + info.WarningCount + info.InfoCount} diagnostics.";
                if (info.SourcesTruncated) diagnostics.Text += $"\nShowing {info.SourceSample.Length} of {info.SourceCount} sources. Open files for all IDs and reasons.";
                openFiles.IsEnabled = timber.Evidence is not null;
            }
            else diagnostics.Text = "";
            return result;
        }
        catch (Exception error) when (error is FormatException or OverflowException or ArgumentException or InvalidOperationException or System.IO.IOException)
        { status.Text = error.Message; return null; }
        finally { inspect.IsEnabled = true; }
    }
}
