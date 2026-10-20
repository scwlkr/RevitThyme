using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;

namespace RevitThyme.DiscoveryProbe;

// Test-only fixture author/oracle. No RevitThyme reference or product lifecycle implementation.
public sealed class DiscoveryProbe : IExternalApplication, IExternalEventHandler
{
    private string? folder;
    private JsonDocument? plan;
    private readonly List<Document> owned = [];
    private Document? active, background, project, family;
    private bool prepared, failed, busy;
    private ExternalEvent? externalEvent;
    private bool raised;
    private string Text(string key) => plan!.RootElement.GetProperty(key).GetString()!;

    public Result OnStartup(UIControlledApplication app)
    {
        folder = Environment.GetEnvironmentVariable("REVITTHYME_DISCOVERY_PROBE");
        if (folder is null) return Result.Succeeded;
        try
        {
            if (!Path.IsPathFullyQualified(folder)) throw new InvalidDataException("Absolute owned folder required.");
            plan = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "plan.json")));
            if (Text("folder") != folder || Text("mode") is not ("normal" or "fixture")) throw new InvalidDataException("Invalid probe plan.");
            foreach (string key in new[] { "a", "b" })
                if (Path.GetDirectoryName(Text(key)) != folder || !Path.IsPathFullyQualified(Text(key))) throw new InvalidDataException("Copies must stay in owned folder.");
            if (Text("a") == Text("b") || Text("source") == Text("a") || Text("source") == Text("b")) throw new InvalidDataException("Copies must be independent.");
            string? fixture = Environment.GetEnvironmentVariable("REVITTHYME_FOUNDATION_FIXTURE");
            if (fixture != (Text("mode") == "fixture" ? Text("a") : null)) throw new InvalidDataException("Fixture environment differs from plan.");
            VerifyFiles();
            externalEvent = ExternalEvent.Create(this);
            app.Idling += Idle;
        }
        catch (Exception error) { Fail(error); }
        return Result.Succeeded;
    }

    private void Idle(object? sender, IdlingEventArgs args)
    {
        if (busy || failed || raised || prepared && !Directory.EnumerateFiles(folder!, "command-*.json").Any()) return;
        raised = true;
        if (externalEvent!.Raise() != ExternalEventRequest.Accepted) raised = false;
    }
    public string GetName() => "Disposable document discovery fixture author";
    public void Execute(UIApplication app)
    {
        raised = false;
        if (busy || failed) return;
        busy = true;
        try
        {
            if (!prepared)
            {
                if (app.Application.Documents.Size != 0 || app.ActiveUIDocument is not null) throw new InvalidDataException("Fixture preparation requires empty owned Home process.");
                active = app.OpenAndActivateDocument(Text("a")).Document; owned.Add(active);
                background = app.Application.OpenDocumentFile(Text("b")); owned.Add(background);
                project = app.Application.NewProjectDocument(UnitSystem.Imperial); owned.Add(project);
                family = app.Application.NewFamilyDocument(Text("template")); owned.Add(family);
                prepared = true;
                VerifyState(app); Write("ready.json", Snapshot(app));
                return;
            }
            foreach (string file in Directory.EnumerateFiles(folder!, "command-*.json").Order())
            {
                JsonDocument request;
                try { request = JsonDocument.Parse(File.ReadAllText(file)); }
                catch (Exception error) when (error is IOException or JsonException) { continue; }
                using (request)
                {
                    string id = request.RootElement.GetProperty("id").GetString()!;
                    string action = request.RootElement.GetProperty("action").GetString()!;
                    if (!Guid.TryParseExact(id, "D", out _) || Path.GetFileName(file) != "command-" + id + ".json"
                        || action is not ("snapshot" or "close-extra" or "cleanup")) throw new InvalidDataException("Invalid explicit fixture command.");
                    VerifyState(app); var before = Snapshot(app);
                    File.Move(file, Path.Combine(folder!, "intent-" + id + ".json"));
                    if (action is "close-extra" or "cleanup") { CloseOwned(project, app); CloseOwned(family, app); }
                    if (action == "cleanup") CloseOwned(background, app);
                    VerifyState(app);
                    Write("receipt-" + id + ".json", new { id, action, before, pid = Environment.ProcessId,
                        start = Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks.ToString(),
                        activePath = active!.PathName, activeView = active.ActiveView.UniqueId, documents = Documents(app) });
                }
                break;
            }
        }
        catch (Exception error) { Fail(error); }
        finally { busy = false; }
    }

    private object Snapshot(UIApplication app) => new { pid = Environment.ProcessId,
        start = Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks.ToString(),
        activePath = app.ActiveUIDocument!.Document.PathName, activeView = app.ActiveUIDocument.Document.ActiveView.UniqueId,
        documents = Documents(app) };
    private static object[] Documents(UIApplication app) => app.Application.Documents.Cast<Document>()
        .Select(d => (object)new { title = d.Title, path = d.PathName, isActive = d.Equals(app.ActiveUIDocument?.Document),
            isModified = d.IsModified, isFamilyDocument = d.IsFamilyDocument, isLinked = d.IsLinked,
            isWorkshared = d.IsWorkshared, isReadOnly = d.IsReadOnly, hasSavedPath = !string.IsNullOrWhiteSpace(d.PathName) }).ToArray();
    private void VerifyState(UIApplication app)
    {
        if (active is null || !active.IsValidObject || !active.Equals(app.ActiveUIDocument?.Document) || active.PathName != Text("a")
            || active.IsModified || active.IsWorkshared || active.IsFamilyDocument) throw new InvalidDataException("Owned A is no longer exact clean active project.");
        if (app.Application.Documents.Cast<Document>().Any(d => !owned.Contains(d))) throw new InvalidDataException("Unexpected document: preserve it.");
        VerifyFiles();
    }
    private void CloseOwned(Document? document, UIApplication app)
    {
        if (document is null || !document.IsValidObject) return;
        if (!owned.Contains(document) || document.Equals(app.ActiveUIDocument?.Document)) throw new InvalidDataException("Cannot close unowned or active document.");
        if (document == background && (document.PathName != Text("b") || document.IsModified)) throw new InvalidDataException("Preserve changed background copy.");
        if (document != background && !string.IsNullOrEmpty(document.PathName)) throw new InvalidDataException("Preserve unexpectedly saved fixture.");
        if (!document.Close(false)) throw new InvalidDataException("Explicit test fixture cleanup was refused.");
    }
    private void VerifyFiles()
    {
        foreach (string key in new[] { "source", "a", "b" })
            if (Hash(Text(key)) != Text("sourceSha256")) throw new InvalidDataException("Saved fixture changed: " + key);
        if (Hash(Text("template")) != Text("templateSha256")) throw new InvalidDataException("Family template changed.");
    }
    private static string Hash(string path)
    { using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); return Convert.ToHexStringLower(SHA256.HashData(stream)); }
    private void Write(string name, object value) => File.WriteAllText(Path.Combine(folder!, name), JsonSerializer.Serialize(value));
    private void Fail(Exception error)
    {
        failed = true;
        if (folder is not null && Directory.Exists(folder)) Write("probe-error.json", new { error = error.GetType().Name, error.Message, error.StackTrace, pid = Environment.ProcessId });
    }
    public Result OnShutdown(UIControlledApplication app)
    {
        if (folder is not null) app.Idling -= Idle;
        externalEvent?.Dispose();
        plan?.Dispose(); return Result.Succeeded;
    }
}
