using System.Windows;
using System.Windows.Controls;
using RevitThyme.Core;

namespace RevitThyme.UI;

public sealed class RoomDimensionsWindow : UtilityWindow
{
    private sealed record RoomChoice(string Id, string Name) { public override string ToString() => Name; }
    private readonly ComboBox rooms = new() { Height = 26, MinWidth = 260 };
    private readonly TextBox inset = new() { Text = "1' 6\"", Height = 26, Width = 112, Padding = new(4, 2, 4, 2) };
    private readonly TextBox clearance = new() { Text = "1' 0\"", Height = 26, Width = 112, Padding = new(4, 2, 4, 2) };
    private readonly TextBlock dimensions = new() { Margin = new(0, 12, 0, 0) };
    public RoomDimensionPreview? CurrentPreview { get; private set; }
    public RoomDimensionsWindow(IOperationClient client) : base(client)
    {
        Title = "RevitThyme — Room Dimensions"; Width = 440; Height = 300;
        var root = new StackPanel { Margin = new(16) }; Content = root;
        void Row(string label, Control control)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, 0, 0, 10) };
            row.Children.Add(new Label { Content = label, Width = 110, Target = control, Padding = new(0, 4, 0, 0) }); row.Children.Add(control); root.Children.Add(row);
        }
        Row("_Room", rooms); Row("_Inset (ft/in)", inset); Row("Tag _clearance", clearance); root.Children.Add(dimensions); root.Children.Add(Status); root.Children.Add(Buttons());
        ApplyButton.Content = "_Create"; RefreshButton.Content = "_Preview";
        rooms.SelectionChanged += (_, _) => Invalidate(); inset.TextChanged += (_, _) => Invalidate(); clearance.TextChanged += (_, _) => Invalidate();
        RefreshButton.Click += (_, _) => Click(RefreshAsync); ApplyButton.Click += (_, _) => Click(CreateAsync);
        Loaded += (_, _) => Click(RefreshAsync);
    }
    private void Invalidate() { CurrentPreview = null; dimensions.Text = ""; UpdateApply(); }
    public Task<OperationResult> RefreshAsync() => Run(async () =>
    {
        CurrentPreview = null; var context = await Inspect(); if (!context.Succeeded) return context;
        var selection = await Client.ExecuteAsync(new(Guid.NewGuid(), OperationKind.GetSelection, Snapshot!.Target));
        string? preferred = (rooms.SelectedItem as RoomChoice)?.Id ?? selection.Selection?.SingleOrDefaultIfOne();
        var result = await Client.ExecuteAsync(new(Guid.NewGuid(), OperationKind.QueryElements, Snapshot.Target, Query: new(CategoryId: "-2000160", Limit: 100)));
        if (!result.Succeeded) return result;
        rooms.ItemsSource = result.Elements!.Items.Select(r => new RoomChoice(r.UniqueId, r.Name)).ToArray();
        rooms.SelectedItem = rooms.Items.Cast<RoomChoice>().FirstOrDefault(r => r.Id == preferred) ?? rooms.Items.Cast<RoomChoice>().FirstOrDefault();
        if (rooms.SelectedItem is not RoomChoice room) return OperationResult.Reject(new(Guid.NewGuid(), OperationKind.PreviewRoomDimensions), "no_rooms", "Place an enclosed rectangular room in this project.");
        var options = new RoomDimensionOptions(LengthText.Parse(inset.Text), LengthText.Parse(clearance.Text));
        result = await Client.ExecuteAsync(new(Guid.NewGuid(), OperationKind.PreviewRoomDimensions, Snapshot.Target, ElementIds: [room.Id], RoomOptions: options));
        CurrentPreview = result.RoomDimensions;
        if (CurrentPreview is { } p) dimensions.Text = $"Width {LengthText.Format(p.WidthFeet)}   ·   Depth {LengthText.Format(p.DepthFeet)}";
        return result;
    });
    public Task<OperationResult> CreateAsync() => Run(async () =>
    {
        if (Snapshot is null || CurrentPreview is not { } preview) throw new InvalidOperationException("Preview the room first.");
        var result = await Client.ExecuteAsync(new(Guid.NewGuid(), OperationKind.CreateRoomDimensions, Snapshot.Target,
            ElementIds: [preview.RoomId], RoomOptions: preview.Options, RoomPreview: preview));
        CurrentPreview = null; return result;
    });
    protected override void UpdateApply() => ApplyButton.IsEnabled = !IsBusy && Snapshot?.CanModify == true && CurrentPreview is not null;
}
internal static class SelectionExtensions
{
    public static string? SingleOrDefaultIfOne(this string[] values) => values.Length == 1 ? values[0] : null;
}
