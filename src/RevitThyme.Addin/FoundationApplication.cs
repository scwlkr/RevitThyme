using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows.Interop;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using RevitThyme.Core;
using RevitThyme.UI;
using RevitThyme.Codex;

namespace RevitThyme.Addin;

public sealed class FoundationApplication : IExternalApplication, IExternalEventHandler
{
    public static FoundationApplication? Instance { get; private set; }
    public OperationDispatcher Operations { get; } = new();
    public SessionBinding Binding { get; private set; } = null!;
    public long Revision { get; private set; }
    public bool TrustedCodeEnabled { get; private set; }
    private readonly Dictionary<Document, string> documents = [];
    private ExternalEvent? externalEvent;
    private PipeServer? server;
    private InspectWindow? window;
    public ViewRangeWindow? ViewRangeWindow { get; private set; }
    public RoomDimensionsWindow? RoomDimensionsWindow { get; private set; }
    public AgentPanel? AgentPanel { get; private set; }
    private UpdateCoordinator? updates;
    private string? bindingPath;
    private string? fixturePath;
    private string? fixtureDocumentId;
    private bool fixtureClosed;
    private bool stopping;

    public Result OnStartup(UIControlledApplication app)
    {
        if (app.ControlledApplication.VersionNumber != "2027") return Result.Failed;
        try
        {
            string session = Guid.NewGuid().ToString();
            TrustedCodeEnabled = Environment.GetEnvironmentVariable("REVITTHYME_TRUSTED_CODE") == "1";
            string start = Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture);
            Binding = new(Protocol.Version, Environment.ProcessId, start, session,
                $"RevitThyme-Foundation-{Environment.ProcessId}-{session}", app.ControlledApplication.VersionNumber);
            fixturePath = Environment.GetEnvironmentVariable("REVITTHYME_FOUNDATION_FIXTURE");
            if (fixturePath is not null && !Path.IsPathFullyQualified(fixturePath)) throw new InvalidDataException("Absolute fixture required.");
            externalEvent = ExternalEvent.Create(this); Operations.Schedule = Schedule;
            server = new(Binding, Operations); server.Start();
            string folder = Environment.GetEnvironmentVariable("REVITTHYME_FOUNDATION_SESSIONS")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RevitThyme", "sessions");
            if (!Path.IsPathFullyQualified(folder)) throw new InvalidDataException("Absolute session directory required.");
            Directory.CreateDirectory(folder); bindingPath = Path.Combine(folder, session + ".json");
            using (var file = new FileStream(bindingPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                JsonSerializer.Serialize(file, Binding, Protocol.Json);
            app.ControlledApplication.DocumentChanged += Changed;
            app.ControlledApplication.DocumentClosing += Closing;
            app.ViewActivated += Activated; app.Idling += Idle;
            try { app.CreateRibbonTab("RevitThyme"); } catch (Autodesk.Revit.Exceptions.ArgumentException) { }
            var panel = app.CreateRibbonPanel("RevitThyme", "Foundation");
            var button = (PushButton)panel.AddItem(new PushButtonData("RevitThyme.InspectView", "Inspect\nView",
                Assembly.GetExecutingAssembly().Location, typeof(InspectCommand).FullName!));
            button.ToolTip = "Inspect the active document and view. Rename a view with verified readback and Revit Undo.";
            var tools = app.GetRibbonPanels("RevitThyme").FirstOrDefault(p => p.Name == "View Tools") ?? app.CreateRibbonPanel("RevitThyme", "View Tools");
            var rangeButton = (PushButton)tools.AddItem(new PushButtonData("RevitThyme.Native.ViewRange", "View\nRange",
                Assembly.GetExecutingAssembly().Location, typeof(ViewRangeCommand).FullName!));
            rangeButton.ToolTip = "Inspect and edit native plan view range with a section preview and one Revit Undo item.";
            var roomButton = (PushButton)tools.AddItem(new PushButtonData("RevitThyme.Native.RoomDimensions", "Room\nDimensions",
                Assembly.GetExecutingAssembly().Location, typeof(RoomDimensionsCommand).FullName!));
            roomButton.ToolTip = "Preview and create width/depth dimensions for a rectangular room using native wall finish faces.";
            AgentPanel = new(Operations, () => AgentConfiguration());
            app.RegisterDockablePane(AgentPane.Id, "RevitThyme Agent", new AgentPane(AgentPanel));
            var agentTools = app.CreateRibbonPanel("RevitThyme", "Agent");
            var agentButton = (PushButton)agentTools.AddItem(new PushButtonData("RevitThyme.Agent", "Connect\nCodex",
                Assembly.GetExecutingAssembly().Location, typeof(AgentCommand).FullName!));
            agentButton.ToolTip = "Chat with Codex in Revit using this process's native MCP connection.";
            updates = new(fixturePath);
            var updateButton = (PushButton)panel.AddItem(new PushButtonData("RevitThyme.Updates", "Check for\nUpdates",
                Assembly.GetExecutingAssembly().Location, typeof(UpdateCommand).FullName!));
            updateButton.ToolTip = "Check the latest stable release and download a verified installer. Install after closing Revit.";
            Instance = this; return Result.Succeeded;
        }
        catch (Exception error)
        {
            Stop(); TaskDialog.Show("RevitThyme", "Foundation startup failed: " + error.Message); return Result.Failed;
        }
    }
    private AgentOptions AgentConfiguration()
    {
        string bundle = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        string home = Environment.GetEnvironmentVariable("REVITTHYME_AGENT_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RevitThyme", "agent");
        if (!Path.IsPathFullyQualified(home)) throw new InvalidDataException("Agent home must be absolute.");
        return new(AgentOptions.FindRuntime(bundle), Path.Combine(bundle, "mcp", "revitthyme-mcp.exe"), bindingPath!,
            Path.Combine(home, "workspace"), Path.Combine(bundle, "skills"), Path.Combine(home, "skills"), TrustedCodeEnabled);
    }
    public InspectWindow Show(UIApplication app)
    {
        if (window is not null) { window.Activate(); return window; }
        window = new(Operations);
        new WindowInteropHelper(window) { Owner = app.MainWindowHandle };
        window.Closed += (_, _) => window = null; window.Show(); return window;
    }
    public ViewRangeWindow ShowViewRange(UIApplication app)
    {
        if (ViewRangeWindow is not null) { ViewRangeWindow.Activate(); return ViewRangeWindow; }
        ViewRangeWindow = new(Operations); new WindowInteropHelper(ViewRangeWindow) { Owner = app.MainWindowHandle };
        ViewRangeWindow.Closed += (_, _) => ViewRangeWindow = null; ViewRangeWindow.Show(); return ViewRangeWindow;
    }
    public RoomDimensionsWindow ShowRoomDimensions(UIApplication app)
    {
        if (RoomDimensionsWindow is not null) { RoomDimensionsWindow.Activate(); return RoomDimensionsWindow; }
        RoomDimensionsWindow = new(Operations); new WindowInteropHelper(RoomDimensionsWindow) { Owner = app.MainWindowHandle };
        RoomDimensionsWindow.Closed += (_, _) => RoomDimensionsWindow = null; RoomDimensionsWindow.Show(); return RoomDimensionsWindow;
    }
    public string DocumentId(Document document)
    {
        if (!documents.TryGetValue(document, out string? id)) documents.Add(document, id = Guid.NewGuid().ToString());
        return id;
    }
    public UpdateWindow ShowUpdates(UIApplication app, UpdateResult? result = null) => updates!.Show(app, result);
    public void AssertDocument(Document document)
    {
        if (fixturePath is null) return;
        string id = DocumentId(document);
        if (!string.Equals(document.PathName, fixturePath, StringComparison.OrdinalIgnoreCase) || document.IsWorkshared)
            throw new OperationException("wrong_fixture", "This test session only accepts its own disposable fixture.");
        if (fixtureClosed || fixtureDocumentId is not null && fixtureDocumentId != id)
            throw new OperationException("reopened_fixture", "The pinned fixture closed; prepare another test session.");
        fixtureDocumentId ??= id;
    }
    private void Changed(object? sender, DocumentChangedEventArgs e) => Revision++;
    private void Closing(object? sender, DocumentClosingEventArgs e)
    {
        if (fixtureDocumentId is not null && fixturePath is not null
            && string.Equals(fixturePath, e.Document.PathName, StringComparison.OrdinalIgnoreCase)) fixtureClosed = true;
        documents.Remove(e.Document); Revision++;
    }
    private void Activated(object? sender, ViewActivatedEventArgs e) => Revision++;
    private void Idle(object? sender, IdlingEventArgs e)
    { if (Operations.HasPending) Schedule(); if (!stopping && sender is UIApplication app) updates?.Idle(app); }
    private void Schedule()
    {
        if (stopping) return;
        try
        {
            if (externalEvent?.Raise() is ExternalEventRequest.Denied or ExternalEventRequest.TimedOut)
                Operations.RejectPending("schedule_failed", "Revit is busy. Inspect again.");
        }
        catch (Exception) { Operations.RejectPending("schedule_failed", "Revit is busy. Inspect again."); }
    }
    public void Execute(UIApplication app) { if (!stopping) Operations.ExecuteNext(new RevitModel(app, this)); }
    public string GetName() => "RevitThyme Foundation operations";
    public Result OnShutdown(UIControlledApplication app)
    {
        app.ControlledApplication.DocumentChanged -= Changed;
        app.ControlledApplication.DocumentClosing -= Closing;
        app.ViewActivated -= Activated; app.Idling -= Idle;
        Stop(); return Result.Succeeded;
    }
    private void Stop()
    {
        stopping = true; Instance = null; Operations.Stop();
        AgentPanel?.Shutdown(); window?.Close(); ViewRangeWindow?.Close(); RoomDimensionsWindow?.Close(); server?.Dispose(); externalEvent?.Dispose();
        updates?.Dispose();
        // Bindings are non-secret historical identities; exited processes fail client verification.
        documents.Clear();
    }
}
