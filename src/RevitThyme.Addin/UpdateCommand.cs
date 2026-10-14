using System.IO;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitThyme.Core;
using RevitThyme.UI;

namespace RevitThyme.Addin;

[Transaction(TransactionMode.Manual)]
public sealed class UpdateCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
    { FoundationApplication.Instance!.ShowUpdates(data.Application); return Result.Succeeded; }
}

internal sealed class UpdateCoordinator(string? fixturePath) : IDisposable
{
    private readonly CancellationTokenSource cancellation = new();
    private Task<UpdateResult?>? pending;
    private bool started;
    public UpdateWindow? Window { get; private set; }
    public void Idle(UIApplication app)
    {
        if (!started)
        {
            started = true;
            pending = Task.Run(async () =>
            {
                try
                {
                    string cache = fixturePath is null ? DailyUpdateCheck.CachePath : Path.Combine(Path.GetDirectoryName(fixturePath)!, "updates", "last-check.txt");
                    if (!DailyUpdateCheck.Claim(cache, DateTimeOffset.UtcNow)) return null;
                    using var http = UpdateChecker.CreateClient();
                    return await new UpdateChecker(http).CheckAsync(cancellation: cancellation.Token);
                }
                catch (Exception) { return null; } // Startup stays quiet when offline or a release is invalid.
            });
        }
        if (pending?.IsCompleted != true) return;
        var result = pending.GetAwaiter().GetResult(); pending = null;
        if (result?.Status == "available" && Window is null) Show(app, result);
    }
    public UpdateWindow Show(UIApplication app, UpdateResult? result = null)
    {
        if (Window is not null) { Window.Activate(); return Window; }
        Window = new(result); new WindowInteropHelper(Window) { Owner = app.MainWindowHandle };
        Window.Closed += (_, _) => Window = null; Window.Show(); return Window;
    }
    public void Dispose() { cancellation.Cancel(); Window?.Close(); }
}
