using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using RevitThyme.Addin;
using RevitThyme.Core;

namespace RevitThyme.RevitIntegration;

// Native test adapter uses the production dispatcher, pipe server and RevitModel.
// Only startup UI is omitted to preserve the concurrently installed host.
public sealed class ImageExportQualificationApplication : IExternalApplication, IExternalEventHandler
{
    private string folder = "", fixture = "";
    private FoundationApplication host = null!;
    private ExternalEvent pulse = null!;
    private IDisposable server = null!;
    private IDisposable outcomeServer = null!;
    private System.Threading.Timer timer = null!;
    private bool ready, stopped;
    private string? switchBack;
    private static void Set(object value, string name, object? data) => value.GetType().GetProperty(name)!.SetValue(value, data);
    private static void Field(object value, string name, object data) => value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(value, data);
    public Result OnStartup(UIControlledApplication app)
    {
        folder = Environment.GetEnvironmentVariable("REVITTHYME_IMAGE_TEST_RUN")!;
        if (folder is null || !System.IO.Path.IsPathFullyQualified(folder) || !Guid.TryParse(System.IO.Path.GetFileName(folder), out _)) return Result.Failed;
        fixture = System.IO.Path.Combine(folder, "Image-Test.rvt");
        if (File.Exists(fixture)) return Result.Failed;
        host = new();
        string session = Guid.NewGuid().ToString();
        Set(host, nameof(host.Binding), new SessionBinding(Protocol.Version, Environment.ProcessId,
            Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture), session,
            $"RevitThyme-Foundation-{Environment.ProcessId}-{session}", "2027"));
        Field(host, "fixturePath", fixture);
        var serverType = typeof(FoundationApplication).Assembly.GetType("RevitThyme.Addin.PipeServer", true)!;
        server = (IDisposable)Activator.CreateInstance(serverType, host.Binding, host.Operations, null)!;
        serverType.GetMethod("Start")!.Invoke(server, null);
        outcomeServer = (IDisposable)Activator.CreateInstance(serverType, host.Binding, host.Operations, OperationKind.GetOperation)!;
        serverType.GetMethod("Start")!.Invoke(outcomeServer, null);
        Write("session.json", host.Binding);
        pulse = ExternalEvent.Create(this); host.Operations.Schedule = () => pulse.Raise();
        app.ControlledApplication.DocumentChanged += Changed;
        app.ControlledApplication.DocumentClosing += Closing;
        app.ViewActivated += Activated; app.Idling += Idle;
        timer = new(_ => { if (!stopped) { try { pulse.Raise(); } catch (Exception) { } } }, null, 1000, 500);
        return Result.Succeeded;
    }
    private void HostEvent(string name, object? sender, object data) => typeof(FoundationApplication).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(host, [sender, data]);
    private void Changed(object? sender, DocumentChangedEventArgs e) => HostEvent("Changed", sender, e);
    private void Closing(object? sender, DocumentClosingEventArgs e) => HostEvent("Closing", sender, e);
    private void Activated(object? sender, ViewActivatedEventArgs e) => HostEvent("Activated", sender, e);
    private void Idle(object? sender, IdlingEventArgs e) { if (!stopped) pulse.Raise(); }
    public void Execute(UIApplication app) => Advance(app);
    private void Advance(UIApplication app)
    {
        if (stopped) return;
        try
        {
            if (!ready)
            {
                if (app.Application.Documents.Size != 0) throw new InvalidOperationException("Only a fresh empty qualification process is allowed.");
                Fixture.Create(app, fixture, mcp: true);
                var doc = app.ActiveUIDocument.Document;
                using (var t = new Transaction(doc, "Create image qualification sheet"))
                {
                    t.Start();
                    var sheet = ViewSheet.Create(doc, ElementId.InvalidElementId); sheet.Name = "Image QA Sheet"; sheet.SheetNumber = "QA-01";
                    var view = new FilteredElementCollector(doc).OfClass(typeof(ViewPlan)).Cast<ViewPlan>().Single(v => v.Name == "Occupied Name");
                    Viewport.Create(doc, sheet.Id, view.Id, new XYZ(1, 1, 0));
                    var template = view.CreateViewTemplate(); template.Name = "Image QA Template";
                    var threeDType = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>().First(v => v.ViewFamily == ViewFamily.ThreeDimensional);
                    var threeD = View3D.CreateIsometric(doc, threeDType.Id); threeD.Name = "Image QA 3D"; threeD.DisplayStyle = DisplayStyle.HLR;
                    t.Commit();
                }
                doc.Save();
                app.ActiveUIDocument.RequestViewChange(new FilteredElementCollector(doc).OfClass(typeof(ViewPlan)).Cast<ViewPlan>().Single(v => v.Name == "Foundation Plan"));
                ready = true; return;
            }
            if (!File.Exists(System.IO.Path.Combine(folder, "ready.json")))
            {
                var doc = app.ActiveUIDocument.Document;
                if (doc.ActiveView.Name != "Foundation Plan") return;
                Write("ready.json", new { host.Binding, fixture, snapshot = new RevitModel(app, host).Capture(), savedHash = Hash(fixture), count = new FilteredElementCollector(doc).GetElementCount(),
                    views = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Select(v => new { v.UniqueId, id = v.Id.Value, v.Name, type = v.ViewType.ToString(), v.IsTemplate }).ToArray(),
                    wall = new FilteredElementCollector(doc).OfClass(typeof(Wall)).Cast<Wall>().Select(w => new { w.UniqueId, heightParameter = w.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM).Id.Value.ToString(CultureInfo.InvariantCulture), height = w.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM).AsDouble() }).First() });
            }
            string command = System.IO.Path.Combine(folder, "command.txt");
            if (File.Exists(command))
            {
                string action = File.ReadAllText(command); File.Delete(command);
                var doc = app.ActiveUIDocument.Document;
                switch (action)
                {
                    case "switch":
                        app.ActiveUIDocument.RequestViewChange(new FilteredElementCollector(doc).OfClass(typeof(ViewPlan)).Cast<ViewPlan>().Single(v => v.Name == "Occupied Name")); break;
                    case "wrong-document":
                        switchBack = doc.PathName;
                        var other = app.Application.NewProjectDocument(UnitSystem.Imperial);
                        string path = System.IO.Path.Combine(folder, "Other.rvt"); other.SaveAs(path); other.Close(false); app.OpenAndActivateDocument(path); break;
                    case "restore-document": app.OpenAndActivateDocument(switchBack!); break;
                    case "reopen":
                        var otherDoc = app.Application.NewProjectDocument(UnitSystem.Imperial);
                        string otherPath = System.IO.Path.Combine(folder, "Reopen-Other.rvt"); otherDoc.SaveAs(otherPath); otherDoc.Close(false);
                        app.OpenAndActivateDocument(otherPath); doc.Close(false); app.OpenAndActivateDocument(fixture); break;
                    case "audit":
                        Write("audit.json", new { count = new FilteredElementCollector(doc).GetElementCount(), doc.IsModified,
                            activeView = doc.ActiveView.UniqueId, savedHash = Hash(fixture), selection = app.ActiveUIDocument.Selection.GetElementIds().Select(i => i.Value).ToArray(),
                            viewName = new FilteredElementCollector(doc).OfClass(typeof(ViewPlan)).Cast<ViewPlan>().Single(v => v.Name.StartsWith("Foundation Plan", StringComparison.Ordinal)).Name }); break;
                    case "close":
                        // Discard only these owned fixtures; never save tests over the baseline.
                        app.PostCommand(RevitCommandId.LookupPostableCommandId(PostableCommand.Close)); break;
                }
                Write("command-done.json", new { action });
            }
            if (File.Exists(System.IO.Path.Combine(folder, "pause-queue")))
            {
                if (host.Operations.HasPending) Write("queue-pending.json", new { pending = true });
                return;
            }
            host.Operations.ExecuteNext(new RevitModel(app, host));
        }
        catch (Exception error) { Write("native-failure.json", new { error = error.ToString() }); host.Operations.RejectPending("test_failed", error.Message); stopped = true; }
    }
    private void Write(string name, object data) => File.WriteAllText(System.IO.Path.Combine(folder, name), JsonSerializer.Serialize(data, Protocol.Json));
    private static string Hash(string path) { using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); return Convert.ToHexString(SHA256.HashData(stream)); }
    public string GetName() => "Disposable image export qualification";
    public Result OnShutdown(UIControlledApplication app)
    {
        stopped = true; timer?.Dispose(); host?.Operations.Stop(); server?.Dispose(); outcomeServer?.Dispose(); pulse?.Dispose();
        app.Idling -= Idle; app.ViewActivated -= Activated; app.ControlledApplication.DocumentChanged -= Changed; app.ControlledApplication.DocumentClosing -= Closing;
        return Result.Succeeded;
    }
}
