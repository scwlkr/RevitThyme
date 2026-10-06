using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using RevitThyme.Native;

namespace RevitThyme.RevitAdapter;
public sealed class Host : IExternalApplication, IExternalEventHandler
{
    internal static Host? Instance;
    internal Session Session { get; } = new();
    internal int ProcessId { get; } = Environment.ProcessId;
    internal string StartTicks { get; } = Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks.ToString();
    internal string SessionId { get; } = Guid.NewGuid().ToString();
    private ExternalEvent? externalEvent;
    private PipeServer? pipe;
    private bool stopping;
    private string PipeName => $"RevitThyme-{ProcessId}-{SessionId}";
    public Result OnStartup(UIControlledApplication app)
    {
        var version = FileVersionInfo.GetVersionInfo(Process.GetCurrentProcess().MainModule!.FileName).FileVersion;
        if (version != "27.2.0.39") return Result.Failed;
        Instance = this; externalEvent = ExternalEvent.Create(this);
        pipe = new(PipeName, ProcessId, StartTicks, SessionId, Session, Schedule);
        _ = pipe.Run();
        app.ControlledApplication.DocumentChanged += Changed;
        app.ControlledApplication.DocumentClosing += Closing;
        app.ViewActivated += Activated; app.Idling += Idle;
        var panel = app.CreateRibbonPanel("RevitThyme Native Preview");
        panel.AddItem(new PushButtonData("RevitThyme.ViewRange", "Visual\nView Range", Assembly.GetExecutingAssembly().Location, typeof(Launch).FullName!));
        return Result.Succeeded;
    }
    public Result OnShutdown(UIControlledApplication app)
    {
        stopping = true; Instance = null;
        app.ControlledApplication.DocumentChanged -= Changed;
        app.ControlledApplication.DocumentClosing -= Closing;
        app.ViewActivated -= Activated; app.Idling -= Idle;
        Session.Invalidate(); pipe?.Dispose(); externalEvent?.Dispose(); return Result.Succeeded;
    }
    private void Changed(object? sender, DocumentChangedEventArgs e) => Session.Invalidate();
    private void Closing(object? sender, DocumentClosingEventArgs e) => Session.Invalidate();
    private void Activated(object? sender, ViewActivatedEventArgs e) => Session.Invalidate();
    private void Idle(object? sender, IdlingEventArgs e) { if (Session.HasQueued) Schedule(); }
    private void Schedule()
    {
        if (stopping) return;
        try
        {
            var status = externalEvent?.Raise();
            if (status is ExternalEventRequest.Denied or ExternalEventRequest.TimedOut) Session.RejectQueued("schedule_failed");
        }
        catch { Session.RejectQueued("schedule_failed"); }
    }
    public void Execute(UIApplication app) { if (!stopping) Session.ExecuteOne(new Model(app, this)); }
    public string GetName() => "RevitThyme bounded Visual View Range";
    internal void Open()
    {
        string folder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        string exe = Path.GetFullPath(Path.Combine(folder, "..", "..", "RevitThyme.exe"));
        if (!File.Exists(exe)) throw new InvalidDataException("Use the complete packaged bundle; desktop executable missing.");
        string credential = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        pipe!.SetCredential(credential);
        var launch = new DesktopLaunch(exe, PipeName, ProcessId, StartTicks, SessionId, credential);
        // The one-shot channel closes itself after PID-verified delivery or timeout.
        try { launch.Start().Dispose(); } catch { launch.Dispose(); throw; }
    }
}
