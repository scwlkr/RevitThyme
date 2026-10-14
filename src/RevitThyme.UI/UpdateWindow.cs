using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using RevitThyme.Core;

namespace RevitThyme.UI;

public sealed class UpdateWindow : Window
{
    private readonly TextBlock status = new() { Text = "Checking for updates…", TextWrapping = TextWrapping.Wrap, Margin = new(0, 12, 0, 12) };
    private readonly Button check = new() { Content = "Check now", MinWidth = 90, Height = 27, Margin = new(0, 0, 8, 0) };
    private readonly Button download = new() { Content = "Download update", MinWidth = 112, Height = 27, Margin = new(0, 0, 8, 0), IsEnabled = false };
    private readonly Button close = new() { Content = "Later", MinWidth = 78, Height = 27 };
    private readonly CancellationTokenSource cancellation = new();
    private UpdateResult? result;
    private bool busy;
    public string StatusText => status.Text;
    public UpdateResult? Result => result;
    public bool DownloadEnabled => download.IsEnabled;
    public string? DownloadedInstaller { get; private set; }
    public UpdateWindow(UpdateResult? initial = null)
    {
        Title = "RevitThyme updates"; Width = 450; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        FontFamily = new("Segoe UI"); FontSize = 12; Background = SystemColors.ControlBrush;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
        var content = new StackPanel { Margin = new(16) };
        content.Children.Add(new TextBlock { Text = "RevitThyme " + ProductVersion.Current, FontSize = 16 });
        content.Children.Add(status);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(check); buttons.Children.Add(download); buttons.Children.Add(close); content.Children.Add(buttons); Content = content;
        check.Click += async (_, _) => await CheckAsync();
        download.Click += async (_, _) => await DownloadAsync(); close.Click += (_, _) => Close();
        Closed += (_, _) => cancellation.Cancel();
        if (initial is not null) SetResult(initial); else Loaded += async (_, _) => await CheckAsync();
    }
    public async Task CheckAsync()
    {
        if (busy) return; SetBusy(true); status.Text = "Checking for updates…"; result = null;
        try
        {
            using var http = UpdateChecker.CreateClient();
            SetResult(await new UpdateChecker(http).CheckAsync(cancellation: cancellation.Token));
        }
        catch (Exception error) { status.Text = "Could not check for updates. " + error.Message; }
        finally { SetBusy(false); }
    }
    private void SetResult(UpdateResult value)
    {
        result = value; status.Text = value.Message + (value.Status == "available" ? " Save your work and close Revit before installing." : "");
        close.Content = value.Status == "available" ? "Later" : "Close"; download.IsEnabled = value.Status == "available";
    }
    private void SetBusy(bool value)
    { busy = value; check.IsEnabled = close.IsEnabled = !value; download.IsEnabled = !value && result?.Status == "available"; }
    public async Task DownloadAsync()
    {
        if (busy || result?.Status != "available") return;
        string version = result.LatestVersion; SetBusy(true); status.Text = "Downloading and verifying the installer…";
        try
        {
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "RevitThyme", version, Guid.NewGuid().ToString("N"));
            string path = await UpdateBootstrap.RunAsync(version, folder, install: false);
            DownloadedInstaller = path;
            status.Text = "Installer saved to " + path + ". Save your work, close Revit, then run the installer.";
            result = null; close.Content = "Close";
            Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true, Arguments = "/select,\"" + path + "\"" });
        }
        catch (Exception error) { status.Text = "Could not download the update. " + error.Message; }
        finally { SetBusy(false); }
    }
}
