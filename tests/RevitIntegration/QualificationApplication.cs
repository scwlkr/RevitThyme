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

// Test-only loader. Inert without the exact prepared run environment; excluded from product packages.
public sealed class QualificationApplication : IExternalApplication, IExternalEventHandler
{
    private string? folder;
    private string fixture = "";
    private int stage;
    private InspectWindow? window;
    private Task<OperationResult>? pending;
    private readonly List<object> checks = [];
    private int elementCount;
    private string savedHash = "";
    private DateTime started;
    private bool stopping;
    private ExternalEvent? switchEvent;
    public Result OnStartup(UIControlledApplication application)
    {
        folder = Environment.GetEnvironmentVariable("REVITTHYME_FOUNDATION_TEST_RUN");
        if (folder is null) return Result.Succeeded;
        if (!Path.IsPathFullyQualified(folder) || !Guid.TryParse(Path.GetFileName(folder), out _)) return Result.Failed;
        fixture = Path.Combine(folder, "Foundation-Test.rvt");
        if (fixture != Environment.GetEnvironmentVariable("REVITTHYME_FOUNDATION_FIXTURE") || File.Exists(fixture)) return Result.Failed;
        switchEvent = ExternalEvent.Create(this);
        started = DateTime.UtcNow; application.Idling += Idle; return Result.Succeeded;
    }
    private void Check(string name, bool passed)
    {
        checks.Add(new { name, passed }); if (!passed) throw new InvalidOperationException("Qualification failed: " + name);
    }
    private void Idle(object? sender, IdlingEventArgs args)
    {
        if (stopping) return;
        try
        {
            var app = (UIApplication)sender!;
            var host = FoundationApplication.Instance ?? throw new InvalidOperationException("Foundation did not start.");
            if ((DateTime.UtcNow - started).TotalMinutes > 10) throw new TimeoutException("Qualification timeout.");
            switch (stage)
            {
                case 0:
                    Check("empty test process", app.Application.Documents.Size == 0);
                    Check("no-document inspection", new OperationExecutor().Execute(new RevitModel(app, host),
                        new(Guid.NewGuid(), OperationKind.Inspect)).Code == "no_document");
                    Fixture.Create(app, fixture); stage++; break;
                case 1:
                    var view = new FilteredElementCollector(app.ActiveUIDocument.Document).OfClass(typeof(ViewPlan))
                        .Cast<ViewPlan>().Single(v => v.Name == "Foundation Plan");
                    app.ActiveUIDocument.RequestViewChange(view); stage++; break;
                case 2:
                    if (app.ActiveUIDocument.ActiveView.Name != "Foundation Plan") return;
                    elementCount = new FilteredElementCollector(app.ActiveUIDocument.Document).GetElementCount();
                    savedHash = Hash(fixture);
                    window = host.Show(app); stage++; break;
                case 3:
                    if (window!.IsBusy || !window.IsLoaded) return;
                    Check("WPF capture available", window.CurrentSnapshot is not null);
                    Check("WPF native inspection", window.CurrentSnapshot!.ViewName == "Foundation Plan"
                        && window.CurrentSnapshot.DocumentPath == fixture && window.CurrentSnapshot.CanModify);
                    pending = window.ApplyNameAsync("WPF Foundation Renamed"); stage++; break;
                case 4:
                    if (!pending!.IsCompleted) return;
                    Check("WPF ExternalEvent modification", pending.GetAwaiter().GetResult().Outcome == Outcome.Applied
                        && app.ActiveUIDocument.ActiveView.Name == "WPF Foundation Renamed");
                    pending = window!.RefreshAsync(); stage++; break;
                case 5:
                    if (!pending!.IsCompleted) return;
                    Check("WPF refresh reads native change", pending.GetAwaiter().GetResult().Snapshot?.ViewName == "WPF Foundation Renamed");
                    pending = window!.ApplyNameAsync("Foundation Plan"); stage++; break;
                case 6:
                    if (!pending!.IsCompleted) return;
                    Check("WPF restores original name", pending.GetAwaiter().GetResult().Outcome == Outcome.Applied
                        && app.ActiveUIDocument.ActiveView.Name == "Foundation Plan");
                    pending = window!.RefreshAsync(); stage++; break;
                case 7:
                    if (!pending!.IsCompleted) return;
                    CaptureWindow();
                    File.WriteAllText(Path.Combine(folder!, "ready.json"), JsonSerializer.Serialize(new
                    { binding = host.Binding, fixture, elementCount, savedHash, processId = Environment.ProcessId }, Protocol.Json));
                    window!.Close(); stage++; break;
                case 8:
                    if (!File.Exists(Path.Combine(folder!, "cli-complete.json"))) return;
                    Check("CLI final native name restored", app.ActiveUIDocument.ActiveView.Name == "Foundation Plan");
                    Check("element count preserved", new FilteredElementCollector(app.ActiveUIDocument.Document).GetElementCount() == elementCount);
                    Check("saved fixture hash preserved", Hash(fixture) == savedHash);
                    var model = new RevitModel(app, host);
                    var current = model.Capture();
                    var request = new OperationRequest(Guid.NewGuid(), OperationKind.RenameView, current.Target, current.ViewName, "Undo Probe");
                    var result = new OperationExecutor().Execute(model, request);
                    Check("undo probe applies", result.Outcome == Outcome.Applied);
                    app.PostCommand(RevitCommandId.LookupPostableCommandId(PostableCommand.Undo)); stage++; break;
                case 9:
                    if (app.ActiveUIDocument.ActiveView.Name != "Foundation Plan") return;
                    Check("native Revit Undo restores name", true);
                    app.PostCommand(RevitCommandId.LookupPostableCommandId(PostableCommand.Redo)); stage++; break;
                case 10:
                    if (app.ActiveUIDocument.ActiveView.Name != "Undo Probe") return;
                    Check("native Revit Redo restores edit", true);
                    app.PostCommand(RevitCommandId.LookupPostableCommandId(PostableCommand.Undo)); stage++; break;
                case 11:
                    if (app.ActiveUIDocument.ActiveView.Name != "Foundation Plan") return;
                    Check("native Revit Undo final restoration", true);
                    Check("final fixture file unsaved", Hash(fixture) == savedHash);
                    stage++;
                    if (switchEvent!.Raise() is ExternalEventRequest.Denied or ExternalEventRequest.TimedOut)
                        throw new InvalidOperationException("Document-switch test could not be scheduled.");
                    break;
            }
        }
        catch (Exception error)
        {
            stopping = true;
            File.WriteAllText(Path.Combine(folder!, "native-result.json"), JsonSerializer.Serialize(new { passed = false, checks, error = error.ToString() }, Protocol.Json));
        }
    }
    public void Execute(UIApplication app)
    {
        if (stage != 12 || stopping) return;
        try
        {
            var host = FoundationApplication.Instance!;
            var doc = app.ActiveUIDocument.Document;
            using (var input = new FileStream(fixture, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var output = new FileStream(Path.Combine(folder!, "Switch-Test.rvt"), FileMode.CreateNew, FileAccess.Write))
                input.CopyTo(output);
            app.OpenAndActivateDocument(Path.Combine(folder!, "Switch-Test.rvt"));
            Check("different document rejected", new OperationExecutor().Execute(new RevitModel(app, host),
                new(Guid.NewGuid(), OperationKind.Inspect)).Code == "wrong_fixture");
            doc.Close(false);
            app.OpenAndActivateDocument(fixture);
            Check("reopened fixture is rejected", new OperationExecutor().Execute(new RevitModel(app, host),
                new(Guid.NewGuid(), OperationKind.Inspect)).Code == "reopened_fixture");
            File.WriteAllText(Path.Combine(folder!, "native-result.json"), JsonSerializer.Serialize(new
            { passed = true, checks, host.Binding, fixture, elementCount, savedHash,
                build = System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileVersionInfo.FileVersion }, Protocol.Json));
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(folder!, "native-result.json"), JsonSerializer.Serialize(new { passed = false, checks, error = error.ToString() }, Protocol.Json));
        }
        finally { stopping = true; }
    }
    public string GetName() => "RevitThyme Foundation fixture-switch qualification";
    private void CaptureWindow()
    {
        window!.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(folder!, "native-wpf.png")); encoder.Save(stream);
    }
    private static string Hash(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
    public Result OnShutdown(UIControlledApplication application)
    { if (folder is not null) application.Idling -= Idle; switchEvent?.Dispose(); return Result.Succeeded; }
}
