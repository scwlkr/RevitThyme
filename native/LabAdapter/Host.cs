using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using RevitThyme.Native;
using RevitThyme.RevitAdapter;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace RevitThyme.Lab;
public sealed class Host : IExternalApplication, IExternalEventHandler, IHostContext
{
    private readonly Session session = new();
    private readonly Dictionary<Document, string> documents = [];
    private LabConfig? config;
    private FixtureGuard? guard;
    private ExternalEvent? externalEvent;
    private PipeServer? pipe;
    private string? credentialTarget;
    private bool opened, viewRequested, initialized, stopping;
    public int ProcessId { get; } = Environment.ProcessId;
    public string StartTicks { get; } = Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks.ToString();
    public string SessionId { get; } = Guid.NewGuid().ToString();
    public uint Revision => session.Revision;
    public string DocumentId(Document doc)
    {
        if (!documents.TryGetValue(doc, out string? id)) documents.Add(doc, id = Guid.NewGuid().ToString());
        return id;
    }
    public void AssertDocument(Document doc) => guard!.Check(doc.PathName, DocumentId(doc), doc.IsWorkshared);
    public void AssertWrite() => guard!.Write();
    public Result OnStartup(UIControlledApplication app)
    {
        string? path = Environment.GetEnvironmentVariable("REVITTHYME_LAB_CONFIG");
        if (string.IsNullOrWhiteSpace(path)) return Result.Succeeded; // Inert in ordinary working sessions.
        try
        {
            config = LabConfig.Load(path);
            if (FileVersionInfo.GetVersionInfo(Process.GetCurrentProcess().MainModule!.FileName).FileVersion != config.RevitVersion)
                throw new InvalidDataException("This lab run requires the approved exact Revit build.");
            guard = new(config.FixturePath, config.AllowWrites);
            externalEvent = ExternalEvent.Create(this);
            app.ControlledApplication.DocumentChanged += Changed;
            app.ControlledApplication.DocumentClosing += Closing;
            app.ViewActivated += Activated; app.Idling += Idle;
            return Result.Succeeded;
        }
        catch { Stop(); return Result.Failed; }
    }
    private void Idle(object? sender, IdlingEventArgs e)
    {
        if (stopping) return;
        if (!initialized)
        {
            try
            {
                var app = (UIApplication)sender!;
                var settings = config!;
                if (!opened)
                {
                    var document = settings.CreateFixture ? Fixture.Create(app, settings.FixturePath) : app.OpenAndActivateDocument(settings.FixturePath);
                    AssertDocument(document.Document);
                    opened = true; return;
                }
                AssertDocument(app.ActiveUIDocument.Document);
                if (!viewRequested)
                {
                    var view = new FilteredElementCollector(app.ActiveUIDocument.Document).OfClass(typeof(ViewPlan)).Cast<ViewPlan>().Single(v => v.Name == "RevitThyme Lab Plan");
                    // OpenAndActivateDocument temporarily forbids view changes. Wait an idle cycle,
                    // request the asynchronous change, then publish only after Revit activates it.
                    app.ActiveUIDocument.RequestViewChange(view); viewRequested = true; return;
                }
                if (app.ActiveUIDocument.ActiveView.Name != "RevitThyme Lab Plan") return;
                initialized = true;
                string name = $"RevitThymeLab-{ProcessId}-{SessionId}";
                credentialTarget = name;
                string secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
                Credential.Store(name, secret);
                pipe = new(name, ProcessId, StartTicks, SessionId, session, Schedule);
                pipe.SetCredential(secret); _ = pipe.Run();
                // Non-secret discovery is immutable for this run. MCP verifies the OS pipe PID.
                var binding = new { protocol = 2, process_id = ProcessId, process_start_ticks = StartTicks,
                    session_id = SessionId, pipe = name, credential_target = name, fixture_path = settings.FixturePath,
                    allow_writes = settings.AllowWrites, revit_version = settings.RevitVersion };
                using var output = new FileStream(settings.BindingPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                JsonSerializer.Serialize(output, binding, Wire.Json);
            }
            catch (Exception ex)
            {
                try { File.WriteAllText(Path.ChangeExtension(config!.BindingPath, ".error.json"), JsonSerializer.Serialize(new { status = "failed", message = ex.Message }, Wire.Json)); }
                finally { Stop(); }
            }
        }
        if (session.HasQueued) Schedule();
    }
    public void Execute(UIApplication app) { if (!stopping) session.ExecuteOne(new Model(app, this)); }
    public string GetName() => "RevitThyme isolated test connection";
    private void Changed(object? sender, DocumentChangedEventArgs e) => session.Invalidate();
    private void Closing(object? sender, DocumentClosingEventArgs e)
    {
        if (LabConfig.Same(config!.FixturePath, e.Document.PathName)) guard!.Close();
        documents.Remove(e.Document); session.Invalidate();
    }
    private void Activated(object? sender, ViewActivatedEventArgs e) => session.Invalidate();
    private void Schedule()
    {
        if (stopping) return;
        try
        {
            if (externalEvent?.Raise() is ExternalEventRequest.Denied or ExternalEventRequest.TimedOut) session.RejectQueued("schedule_failed");
        }
        catch { session.RejectQueued("schedule_failed"); }
    }
    private void Stop()
    {
        stopping = true; session.Invalidate(); pipe?.Dispose(); externalEvent?.Dispose();
        if (credentialTarget is not null) Credential.Delete(credentialTarget);
    }
    public Result OnShutdown(UIControlledApplication app)
    {
        if (config is not null)
        {
            app.ControlledApplication.DocumentChanged -= Changed;
            app.ControlledApplication.DocumentClosing -= Closing;
            app.ViewActivated -= Activated; app.Idling -= Idle;
        }
        Stop(); return Result.Succeeded;
    }
}
