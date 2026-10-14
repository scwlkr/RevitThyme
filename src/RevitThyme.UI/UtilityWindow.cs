using System.Windows;
using System.Windows.Controls;
using RevitThyme.Core;

namespace RevitThyme.UI;

public abstract class UtilityWindow : Window
{
    protected readonly IOperationClient Client;
    protected Snapshot? Snapshot;
    protected TextBlock Status = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 12, 0, 12) };
    protected Button RefreshButton = new() { Content = "_Refresh", Width = 78, Height = 27, Margin = new(0, 0, 8, 0) };
    protected Button ApplyButton = new() { Content = "_Apply", Width = 78, Height = 27, Margin = new(0, 0, 8, 0), IsEnabled = false };
    public bool IsBusy { get; private set; }
    protected UtilityWindow(IOperationClient client)
    {
        Client = client; FontFamily = new("Segoe UI"); FontSize = 12; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = SystemColors.ControlBrush;
    }
    protected StackPanel Buttons()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var close = new Button { Content = "Close", Width = 78, Height = 27 }; close.Click += (_, _) => Close();
        panel.Children.Add(RefreshButton); panel.Children.Add(ApplyButton); panel.Children.Add(close); return panel;
    }
    protected async Task<OperationResult> Run(Func<Task<OperationResult>> action)
    {
        if (IsBusy) throw new InvalidOperationException("Another request is running.");
        IsBusy = true; ApplyButton.IsEnabled = RefreshButton.IsEnabled = false;
        try
        {
            var result = await action(); Snapshot = result.Snapshot;
            Status.Text = result.Message + (result.Outcome == Outcome.Unconfirmed ? $" Request: {result.Id}" : "");
            return result;
        }
        catch (Exception error) { Snapshot = null; Status.Text = error.Message; throw; }
        finally { IsBusy = false; RefreshButton.IsEnabled = true; UpdateApply(); }
    }
    protected abstract void UpdateApply();
    protected async Task<OperationResult> Inspect()
    {
        var result = await Client.ExecuteAsync(new(Guid.NewGuid(), OperationKind.Inspect)); Snapshot = result.Snapshot; return result;
    }
    protected static async void Click(Func<Task<OperationResult>> action)
    { try { await action(); } catch (Exception) { /* Status contains the failure. */ } }
}
