using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Media.Imaging;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using RevitThyme.Addin;
using RevitThyme.Core;

namespace RevitThyme.RevitIntegration;

// UI-only qualification in a new process and disposable fixture. Never edits an existing model.
public sealed class RibbonQualificationApplication : IExternalApplication, IExternalEventHandler
{
    private string? folder;
    private string fixture = "", savedHash = "";
    private int stage, count;
    private bool stopped;
    private UITheme originalTheme;
    private DateTime started;
    private ExternalEvent? pulseEvent;
    private Timer? pulse;
    private readonly List<object> checks = [];
    private readonly Dictionary<string, byte[]> darkImages = [];

    public Result OnStartup(UIControlledApplication application)
    {
        folder = Environment.GetEnvironmentVariable("REVITTHYME_RIBBON_TEST_RUN");
        if (folder is null) return Result.Succeeded;
        fixture = Path.Combine(folder, "Foundation-Test.rvt");
        if (!Path.IsPathFullyQualified(folder) || File.Exists(fixture)
            || fixture != Environment.GetEnvironmentVariable("REVITTHYME_FOUNDATION_FIXTURE")) return Result.Failed;
        originalTheme = UIThemeManager.CurrentTheme; started = DateTime.UtcNow;
        pulseEvent = ExternalEvent.Create(this); application.Idling += Idle;
        pulse = new(_ => { if (!stopped) { try { pulseEvent.Raise(); } catch (Exception) { } } }, null, 1000, 500);
        return Result.Succeeded;
    }

    private void Check(string name, bool passed)
    {
        checks.Add(new { name, passed }); Write("progress.json", new { stage, checks });
        if (!passed) throw new InvalidOperationException(name);
    }
    private static PushButton[] Buttons(UIApplication app) => app.GetRibbonPanels("RevitThyme")
        .SelectMany(p => p.GetItems()).OfType<PushButton>().ToArray();
    private static byte[] Pixels(BitmapSource image)
    {
        var pixels = new byte[image.PixelHeight * ((image.PixelWidth * image.Format.BitsPerPixel + 7) / 8)];
        image.CopyPixels(pixels, pixels.Length / image.PixelHeight, 0); return pixels;
    }
    private void Post(UIApplication app, string name)
    {
        var panel = app.GetRibbonPanels("RevitThyme").Single(p => p.GetItems().Any(b => b.Name == name));
        var id = RevitCommandId.LookupCommandId("CustomCtrl_%CustomCtrl_%RevitThyme%" + panel.Name + "%" + name);
        Check("native command can post: " + name, id is not null && app.CanPostCommand(id)); app.PostCommand(id!);
    }
    private void Idle(object? sender, IdlingEventArgs args) => Execute((UIApplication)sender!);
    public void Execute(UIApplication app)
    {
        if (stopped) return;
        try
        {
            if (DateTime.UtcNow - started > TimeSpan.FromMinutes(8)) throw new TimeoutException("Ribbon qualification timed out.");
            var host = FoundationApplication.Instance ?? throw new InvalidOperationException("Native host missing.");
            var doc = app.ActiveUIDocument?.Document;
            switch (stage)
            {
                case 0:
                    // Startup can precede Revit applying the user's saved frame theme.
                    originalTheme = UIThemeManager.CurrentTheme;
                    Check("empty isolated Revit process", app.Application.Documents.Size == 0);
                    Check("task panels in reading order", app.GetRibbonPanels("RevitThyme").Select(p => p.Name).SequenceEqual(["Views", "Annotation", "RevitThyme"]));
                    var buttons = Buttons(app);
                    Check("five original toolkit command keys retained", buttons.Select(b => b.Name).Order().SequenceEqual(new[] {
                        "RevitThyme.Native.ViewRange", "RevitThyme.InspectView", "RevitThyme.Native.RoomDimensions", "RevitThyme.Agent", "RevitThyme.Updates" }.Order()));
                    Check("every command has short and extended help", buttons.All(b => !string.IsNullOrWhiteSpace(b.ToolTip) && !string.IsNullOrWhiteSpace(b.LongDescription)));
                    Check("every command has small and large embedded icons", buttons.All(b => b.Image is BitmapSource { PixelWidth: > 0 } && b.LargeImage is BitmapSource { PixelWidth: > 0 }));
                    Check("model tools unavailable with no document", !new DocumentAvailability().IsCommandAvailable(app, new())
                        && !new ViewRangeAvailability().IsCommandAvailable(app, new()) && !new RoomDimensionsAvailability().IsCommandAvailable(app, new()));
                    Check("update command available with no document", new ApplicationAvailability().IsCommandAvailable(app, new()));
                    Post(app, "RevitThyme.Updates"); stage++; break;
                case 1:
                    if (host.UpdateWindow is not { IsVisible: true }) return;
                    Check("Check for Updates opens native dialog with no document", true);
                    host.UpdateWindow.Close(); Fixture.Create(app, fixture);
                    var created = app.ActiveUIDocument!.Document;
                    using (var transaction = new Transaction(created, "Create ribbon test view"))
                    {
                        transaction.Start();
                        var type = new FilteredElementCollector(created).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>().First(t => t.ViewFamily == ViewFamily.ThreeDimensional);
                        View3D.CreateIsometric(created, type.Id).Name = "Ribbon Test 3D"; transaction.Commit();
                    }
                    created.Save(); stage++; break;
                case 2:
                    app.ActiveUIDocument!.RequestViewChange(new FilteredElementCollector(doc!).OfClass(typeof(ViewPlan)).Cast<ViewPlan>().Single(v => v.Name == "Foundation Plan"));
                    stage++; break;
                case 3:
                    if (doc!.ActiveView.Name != "Foundation Plan") return;
                    savedHash = Hash(); count = new FilteredElementCollector(doc).GetElementCount();
                    Check("plan tools available in floor plan", new ViewRangeAvailability().IsCommandAvailable(app, new()) && new RoomDimensionsAvailability().IsCommandAvailable(app, new()));
                    Post(app, "RevitThyme.Agent"); stage++; break;
                case 4:
                    var pane = app.GetDockablePane(new(new Guid("D988D5C7-B1BB-40CE-A636-48C344DA7E68")));
                    if (!pane.IsShown()) return; // PostCommand runs after Revit returns to its message loop.
                    Check("Open Agent opens dockable pane without connection", host.AgentPanel?.Session is null);
                    pane.Hide(); Post(app, "RevitThyme.Native.ViewRange"); stage++; break;
                case 5:
                    if (host.ViewRangeWindow is not { IsBusy: false, CurrentRange: not null }) return;
                    Check("View Range button opens populated native dialog", true); host.ViewRangeWindow.Close();
                    Post(app, "RevitThyme.Native.RoomDimensions"); stage++; break;
                case 6:
                    if (host.RoomDimensionsWindow is not { IsBusy: false }) return;
                    Check("Room Dimensions button opens native dialog", host.RoomDimensionsWindow.IsVisible); host.RoomDimensionsWindow.Close();
                    Post(app, "RevitThyme.InspectView"); stage++; break;
                case 7:
                    if (host.InspectWindow is not { IsBusy: false, CurrentSnapshot: not null } inspect) return;
                    Check("Inspect View button captures active native view", inspect.CurrentSnapshot.ViewName == "Foundation Plan"); inspect.Close();
                    UIThemeManager.CurrentTheme = UITheme.Dark; stage++; break;
                case 8:
                    foreach (var button in Buttons(app)) darkImages.Add(button.Name, Pixels((BitmapSource)button.LargeImage));
                    Check("dark theme icons retain logical button sizes", Buttons(app).All(b => Math.Abs(b.Image.Width - 16) < 0.1 && Math.Abs(b.LargeImage.Width - 32) < 0.1));
                    UIThemeManager.CurrentTheme = UITheme.Light; stage++; break;
                case 9:
                    Check("all icons change with native theme event", Buttons(app).All(b => !darkImages[b.Name].SequenceEqual(Pixels((BitmapSource)b.LargeImage))));
                    Check("light theme icons retain logical button sizes", Buttons(app).All(b => Math.Abs(b.Image.Width - 16) < 0.1 && Math.Abs(b.LargeImage.Width - 32) < 0.1));
                    UIThemeManager.CurrentTheme = originalTheme;
                    app.ActiveUIDocument!.RequestViewChange(new FilteredElementCollector(doc!).OfClass(typeof(View3D)).Cast<View3D>().Single(v => v.Name == "Ribbon Test 3D"));
                    stage++; break;
                case 10:
                    if (doc!.ActiveView.ViewType != ViewType.ThreeD) return;
                    Check("plan-only tools disabled in 3D", !new ViewRangeAvailability().IsCommandAvailable(app, new()) && !new RoomDimensionsAvailability().IsCommandAvailable(app, new()));
                    Check("inspection remains available in 3D", new DocumentAvailability().IsCommandAvailable(app, new()));
                    app.ActiveUIDocument!.RequestViewChange(new FilteredElementCollector(doc).OfClass(typeof(ViewPlan)).Cast<ViewPlan>().Single(v => v.Name == "Foundation Plan"));
                    stage++; break;
                case 11:
                    if (doc!.ActiveView.Name != "Foundation Plan") return;
                    Check("fixture elements preserved", new FilteredElementCollector(doc).GetElementCount() == count);
                    Check("fixture disk hash preserved", Hash() == savedHash);
                    Check("fixture stays unmodified", !doc.IsModified);
                    Check("original application theme restored", UIThemeManager.CurrentTheme == originalTheme);
                    Finish(true); break;
            }
        }
        catch (Exception error) { UIThemeManager.CurrentTheme = originalTheme; Finish(false, error.ToString()); }
    }
    private string Hash()
    { using var file = new FileStream(fixture, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); return Convert.ToHexStringLower(SHA256.HashData(file)); }
    private void Write(string name, object value) => File.WriteAllText(Path.Combine(folder!, name), JsonSerializer.Serialize(value, Protocol.Json));
    private void Finish(bool passed, string? error = null)
    { stopped = true; Write("native-result.json", new { passed, checks, error, fixture, savedHash, count, processId = Environment.ProcessId, originalTheme = originalTheme.ToString() }); }
    public string GetName() => "RevitThyme isolated ribbon qualification";
    public Result OnShutdown(UIControlledApplication application)
    {
        if (folder is not null) { stopped = true; application.Idling -= Idle; pulse?.Dispose(); pulseEvent?.Dispose(); UIThemeManager.CurrentTheme = originalTheme; }
        return Result.Succeeded;
    }
}
