using System.Windows;
using RevitThyme.Core;

namespace RevitThyme.UI;

public partial class InspectWindow : Window
{
    private readonly IOperationClient client;
    private Snapshot? snapshot;
    private bool busy;
    public bool IsBusy => busy;
    public Snapshot? CurrentSnapshot => snapshot;
    public InspectWindow(IOperationClient client)
    {
        this.client = client; InitializeComponent();
        Loaded += async (_, _) => await RefreshAsync();
    }
    public async Task<OperationResult> RefreshAsync()
    {
        return await RunAsync(new(Guid.NewGuid(), OperationKind.Inspect));
    }
    public async Task<OperationResult> ApplyNameAsync(string name)
    {
        if (snapshot is null) throw new InvalidOperationException("Inspect a view first.");
        return await RunAsync(new(Guid.NewGuid(), OperationKind.RenameView, snapshot.Target, snapshot.ViewName, name));
    }
    private async Task<OperationResult> RunAsync(OperationRequest request)
    {
        if (busy) throw new InvalidOperationException("Another request is running.");
        busy = true; RefreshButton.IsEnabled = ApplyButton.IsEnabled = false;
        try
        {
            var result = await client.ExecuteAsync(request);
            StatusText.Text = result.Message;
            snapshot = result.Snapshot;
            if (snapshot is not null)
            {
                DocumentText.Text = snapshot.DocumentTitle;
                TypeText.Text = snapshot.ViewType; NameInput.Text = snapshot.ViewName;
                IdentityText.Text = $"View {snapshot.ViewElementId}  ·  Revit {snapshot.RevitVersion}";
                if (!snapshot.CanModify) StatusText.Text = snapshot.WriteBlock;
            }
            return result;
        }
        catch (Exception)
        {
            snapshot = null; StatusText.Text = "Revit connection failed. Refresh before editing."; throw;
        }
        finally { busy = false; RefreshButton.IsEnabled = true; ApplyButton.IsEnabled = snapshot?.CanModify == true; }
    }
    private async void RefreshClick(object sender, RoutedEventArgs e)
    {
        try { await RefreshAsync(); } catch (Exception) { /* RunAsync displays the error. */ }
    }
    private async void ApplyClick(object sender, RoutedEventArgs e)
    {
        try { await ApplyNameAsync(NameInput.Text); } catch (Exception) { /* RunAsync displays the error. */ }
    }
    private void CloseClick(object sender, RoutedEventArgs e) => Close();
}
