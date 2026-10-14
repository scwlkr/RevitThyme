using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using RevitThyme.Addin;
using RevitThyme.Core;
using RevitThyme.UI;

namespace RevitThyme.RevitIntegration;

// Test-only loader: never operates on an existing document or published package.
public sealed class UpdateQualificationApplication : IExternalApplication
{
    private string? folder;
    private string fixture = "", savedHash = "";
    private int stage, count;
    private UpdateWindow? window;
    private Task? download;
    private DateTime started;
    private readonly List<object> checks = [];
    public Result OnStartup(UIControlledApplication application)
    {
        folder = Environment.GetEnvironmentVariable("REVITTHYME_UPDATE_TEST_RUN");
        if (folder is null) return Result.Succeeded;
        fixture = Path.Combine(folder, "Foundation-Test.rvt");
        if (!Path.IsPathFullyQualified(folder) || !Guid.TryParse(Path.GetFileName(folder), out _)
            || fixture != Environment.GetEnvironmentVariable("REVITTHYME_FOUNDATION_FIXTURE") || File.Exists(fixture)) return Result.Failed;
        started = DateTime.UtcNow; application.Idling += Idle; return Result.Succeeded;
    }
    private void Check(string name, bool passed)
    { checks.Add(new { name, passed }); if (!passed) throw new InvalidOperationException(name); }
    private void Idle(object? sender, IdlingEventArgs e)
    {
        if (stage == 99) return;
        try
        {
            var app = (UIApplication)sender!; var host = FoundationApplication.Instance!;
            if (DateTime.UtcNow - started > TimeSpan.FromMinutes(8)) throw new TimeoutException("Update qualification timed out.");
            switch (stage)
            {
                case 0:
                    Check("empty disposable Revit process", app.Application.Documents.Size == 0);
                    Fixture.Create(app, fixture); count = new FilteredElementCollector(app.ActiveUIDocument.Document).GetElementCount(); savedHash = Hash(fixture);
                    File.WriteAllText(Path.Combine(folder!, "progress.json"), "{\"stage\":\"fixture-ready\"}"); stage++; break;
                case 1:
                    Check("native update ribbon registered", app.GetRibbonPanels("RevitThyme").SelectMany(x => x.GetItems()).Any(x => x.Name == "RevitThyme.Updates"));
                    window = host.ShowUpdates(app); stage++; break;
                case 2:
                    if (window!.Result is null) return;
                    Check("manual native check returns real release metadata", window.Result.Release.SupportedRevitYears.Contains("2027"));
                    Check("native current or development build has no download offer", window.Result.Status is "ahead" or "current" && !window.DownloadEnabled);
                    Capture("native-updates.png"); window.Close();
                    // Simulate an available release to exercise the UI with the real published downloader.
                    window = host.ShowUpdates(app, new(ProductVersion.Current, ProductVersion.Build, "0.8.1", "available", "RevitThyme 0.8.1 is available (qualification simulation).",
                        new("0.8.1", "https://github.com/scwlkr/RevitThyme/releases/tag/v0.8.1", ["2027"])));
                    stage++; break;
                case 3:
                    Check("available release offers download and close-first instructions", window!.DownloadEnabled && window.StatusText.Contains("close Revit", StringComparison.Ordinal));
                    Capture("native-update-available.png"); download = window.DownloadAsync(); stage++; break;
                case 4:
                    if (!download!.IsCompleted) return; download.GetAwaiter().GetResult();
                    Check("native download returns a verified installer", window!.DownloadedInstaller is { } path && File.Exists(path) && File.Exists(path + ".sha256"));
                    Check("download instruction keeps Revit open", window.StatusText.Contains("close Revit", StringComparison.Ordinal));
                    Capture("native-update-downloaded.png");
                    Check("daily startup attempt recorded in isolated cache", File.Exists(Path.Combine(folder!, "updates", "last-check.txt")));
                    Check("fixture element count preserved", new FilteredElementCollector(app.ActiveUIDocument.Document).GetElementCount() == count);
                    Check("fixture saved file preserved", Hash(fixture) == savedHash);
                    Check("fixture remains unmodified", !app.ActiveUIDocument.Document.IsModified);
                    Finish(true); break;
            }
        }
        catch (Exception error) { Finish(false, error.ToString()); }
    }
    private void Finish(bool passed, string? error = null)
    {
        stage = 99;
        File.WriteAllText(Path.Combine(folder!, "native-result.json"), JsonSerializer.Serialize(new { passed, checks, error, fixture, savedHash, count,
            version = ProductVersion.Build, downloadedInstaller = window?.DownloadedInstaller, processId = Environment.ProcessId }, Protocol.Json));
    }
    private void Capture(string name)
    {
        window!.UpdateLayout(); var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(folder!, name)); encoder.Save(file);
    }
    private static string Hash(string path)
    { using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); return Convert.ToHexStringLower(SHA256.HashData(file)); }
    public Result OnShutdown(UIControlledApplication application)
    { if (folder is not null) application.Idling -= Idle; return Result.Succeeded; }
}
