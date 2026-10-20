using System.Diagnostics;
using System.Text.Json;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;

namespace RevitThyme.LifecycleProbe;

// Test-only third-party cancellation. It never executes lifecycle operations or modifies models.
public sealed class LifecycleProbe : IExternalApplication
{
    private string? folder;
    private string[] allowed = [];
    public Result OnStartup(UIControlledApplication app)
    {
        folder = Environment.GetEnvironmentVariable("REVITTHYME_LIFECYCLE_PROBE");
        if (folder is null) return Result.Succeeded;
        if (!Path.IsPathFullyQualified(folder)) return Result.Failed;
        using var plan = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "plan.json")));
        allowed = [plan.RootElement.GetProperty("a").GetString()!, plan.RootElement.GetProperty("b").GetString()!];
        if (allowed.Any(p => !Path.IsPathFullyQualified(p) || !Path.GetDirectoryName(p)!.Equals(folder, StringComparison.OrdinalIgnoreCase))) return Result.Failed;
        app.ControlledApplication.DocumentClosing += Closing;
        using var process = Process.GetCurrentProcess();
        File.WriteAllText(Path.Combine(folder, "probe-ready.json"), JsonSerializer.Serialize(new { pid = process.Id, start = process.StartTime.ToUniversalTime().Ticks.ToString(), allowed }));
        return Result.Succeeded;
    }
    private void Closing(object? sender, DocumentClosingEventArgs e)
    {
        string intent = Path.Combine(folder!, "cancel-close.json");
        if (!File.Exists(intent) || !e.Cancellable) return;
        using var request = JsonDocument.Parse(File.ReadAllText(intent));
        string path = request.RootElement.GetProperty("path").GetString()!, id = request.RootElement.GetProperty("id").GetString()!;
        if (!Guid.TryParseExact(id, "D", out _) || !allowed.Contains(path, StringComparer.OrdinalIgnoreCase)
            || !path.Equals(e.Document.PathName, StringComparison.OrdinalIgnoreCase)) return;
        string receipt = Path.Combine(folder!, "cancelled-" + id + ".json");
        File.Move(intent, receipt); // Consume the exact one-shot intent before cancelling.
        e.Cancel();
    }
    public Result OnShutdown(UIControlledApplication app)
    {
        if (folder is not null) app.ControlledApplication.DocumentClosing -= Closing;
        return Result.Succeeded;
    }
}
