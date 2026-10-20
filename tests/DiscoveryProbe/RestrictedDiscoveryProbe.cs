using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;

namespace RevitThyme.DiscoveryProbe;

// Independent native fixture preparation/oracle. Toolkit requests never execute here.
public sealed class RestrictedDiscoveryProbe : IExternalApplication, IExternalEventHandler
{
    private string? folder;
    private JsonDocument? plan;
    private ExternalEvent? externalEvent;
    private bool prepared, raised, failed;
    private Document? active, workshared, readOnly, family, linked;
    private RevitLinkInstance? instance;
    private FileAttributes originalAttributes;
    private readonly Dictionary<string, string> hashes = [];
    private string Text(string key) => plan!.RootElement.GetProperty(key).GetString()!;
    private void Write(string name, object value) => File.WriteAllText(Path.Combine(folder!, name), JsonSerializer.Serialize(value));
    private static string Hash(string path)
    { using var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); return Convert.ToHexStringLower(SHA256.HashData(s)); }

    public Result OnStartup(UIControlledApplication app)
    {
        folder = Environment.GetEnvironmentVariable("REVITTHYME_DISCOVERY_PROBE");
        if (folder is null) return Result.Succeeded;
        try
        {
            if (!Path.IsPathFullyQualified(folder)) throw new InvalidDataException("Absolute owned run required.");
            plan = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "plan.json")));
            if (Text("folder") != folder || Text("profile") != "restricted" || Text("mode") != "normal"
                || Environment.GetEnvironmentVariable("REVITTHYME_FOUNDATION_FIXTURE") is not null) throw new InvalidDataException("Normal restricted probe plan required.");
            foreach (string key in new[] { "a", "b", "w", "r", "link", "family" })
                if (!Path.IsPathFullyQualified(Text(key)) || Path.GetDirectoryName(Text(key)) != folder) throw new InvalidDataException("All authored files must stay in owned run.");
            var paths = new[] { "a", "b", "w", "r", "link", "family" }.Select(Text).ToArray();
            if (paths.Distinct(StringComparer.OrdinalIgnoreCase).Count() != paths.Length || paths.Contains(Text("source"), StringComparer.OrdinalIgnoreCase)) throw new InvalidDataException("Fixtures must be distinct from source.");
            foreach (string key in new[] { "source", "a", "b", "r" })
                if (Hash(Text(key)) != Text("sourceSha256")) throw new InvalidDataException("Initial copy/source changed.");
            using (var info = BasicFileInfo.Extract(Text("source")))
                if (info.IsWorkshared || info.Format != "2027") throw new InvalidDataException("Only a Revit 2027 nonworkshared source is eligible; no upgrade or central access.");
            if (Hash(Text("template")) != Text("templateSha256") || new[] { "w", "link", "family" }.Any(k => File.Exists(Text(k)))) throw new InvalidDataException("Preserve existing artifacts/template.");
            originalAttributes = File.GetAttributes(Text("r"));
            Write("original-attributes.json", new { path = Text("r"), attributes = (int)originalAttributes });
            externalEvent = ExternalEvent.Create(this); app.Idling += Idle;
        }
        catch (Exception error) { Fail(error); }
        return Result.Succeeded;
    }
    private void Idle(object? sender, IdlingEventArgs args)
    {
        args.SetRaiseWithoutDelay();
        if (failed || raised || prepared && !Directory.EnumerateFiles(folder!, "command-*.json").Any()) return;
        raised = true; if (externalEvent!.Raise() != ExternalEventRequest.Accepted) raised = false;
    }
    public string GetName() => "Owned restricted-document fixture author";
    public void Execute(UIApplication app)
    {
        raised = false;
        if (failed) return;
        try
        {
            if (!prepared) { Prepare(app); prepared = true; Verify(app); Write("ready.json", Snapshot(app)); return; }
            foreach (string file in Directory.EnumerateFiles(folder!, "command-*.json").Order())
            {
                JsonDocument command;
                try { command = JsonDocument.Parse(File.ReadAllText(file)); }
                catch (Exception error) when (error is IOException or JsonException) { continue; }
                using (command)
                {
                    string id = command.RootElement.GetProperty("id").GetString()!, action = command.RootElement.GetProperty("action").GetString()!;
                    if (!Guid.TryParseExact(id, "D", out _) || Path.GetFileName(file) != "command-" + id + ".json"
                        || action is not ("snapshot" or "close-workshared" or "cleanup")) throw new InvalidDataException("Explicit fixture command required.");
                    Verify(app); var before = Snapshot(app);
                    File.Move(file, Path.Combine(folder!, "intent-" + id + ".json"));
                    if (action is "close-workshared" or "cleanup") Close(workshared, Text("w"), app, false);
                    if (action == "cleanup")
                    {
                        // Only this known disposable R may be dirty after the explicit MCP save-failure experiment.
                        Close(readOnly, Text("r"), app, true); Close(family, Text("family"), app, false);
                        File.SetAttributes(Text("r"), originalAttributes);
                    }
                    Verify(app);
                    Write("receipt-" + id + ".json", new { id, action, before, pid = Environment.ProcessId,
                        start = Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks.ToString(), activePath = app.ActiveUIDocument!.Document.PathName,
                        activeView = app.ActiveUIDocument.Document.ActiveView.UniqueId, documents = Documents(app), hashes, readOnlyAttributes = (int)File.GetAttributes(Text("r")) });
                }
                break;
            }
        }
        catch (Exception error) { Fail(error); }
    }
    private void Prepare(UIApplication app)
    {
        if (app.Application.Documents.Size != 0 || app.ActiveUIDocument is not null) throw new InvalidDataException("Use empty owned Home process.");
        using (var document = app.Application.NewProjectDocument(UnitSystem.Imperial))
        {
            using var options = new SaveAsOptions(); document.SaveAs(Text("link"), options);
            if (!document.Close(false)) throw new InvalidDataException("Link author close refused.");
        }
        active = app.OpenAndActivateDocument(Text("a")).Document;
        AssertProject(active, Text("a"));
        if (!active.Equals(app.ActiveUIDocument?.Document) || app.Application.Documents.Size != 1) throw new InvalidDataException("Exact solitary active A required before authoring.");
        using (var transaction = new Transaction(active, "Author disposable linked fixture"))
        {
            transaction.Start(); using var options = new RevitLinkOptions(false);
            using var load = RevitLinkType.Create(active, ModelPathUtils.ConvertUserVisiblePathToModelPath(Text("link")), options);
            instance = RevitLinkInstance.Create(active, load.ElementId);
            if (transaction.Commit() != TransactionStatus.Committed) throw new InvalidDataException("Link author transaction failed.");
        }
        linked = instance.GetLinkDocument() ?? throw new InvalidDataException("Real linked Document required.");
        active.Save();
        workshared = app.Application.OpenDocumentFile(Text("b"));
        AssertProject(workshared, Text("b"));
        if (!workshared.CanEnableWorksharing()) throw new InvalidDataException("Owned seed cannot enable worksharing.");
        workshared.EnableWorksharing("Shared Levels and Grids", "Owned qualification model");
        using (var options = new SaveAsOptions())
        {
            using var central = new WorksharingSaveAsOptions { SaveAsCentral = true };
            options.SetWorksharingOptions(central); workshared.SaveAs(Text("w"), options);
        }
        File.SetAttributes(Text("r"), originalAttributes | FileAttributes.ReadOnly);
        readOnly = app.Application.OpenDocumentFile(Text("r"));
        family = app.Application.NewFamilyDocument(Text("template"));
        using (var options = new SaveAsOptions()) family.SaveAs(Text("family"), options);
        foreach (string key in new[] { "source", "a", "b", "w", "r", "link", "family", "template" }) hashes.Add(Text(key), Hash(Text(key)));
        Write("authored.json", new { hashes, central = ModelPathUtils.ConvertModelPathToUserVisiblePath(workshared.GetWorksharingCentralModelPath()),
            link = new { hostPath = active.PathName, path = linked.PathName, instance = instance.UniqueId }, readOnlyFile = readOnly.IsReadOnlyFile });
    }
    private object Snapshot(UIApplication app) => new { pid = Environment.ProcessId,
        start = Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks.ToString(), activePath = app.ActiveUIDocument!.Document.PathName,
        activeView = app.ActiveUIDocument.Document.ActiveView.UniqueId, documents = Documents(app), hashes };
    private static object[] Documents(UIApplication app) => app.Application.Documents.Cast<Document>().Select(d => (object)new {
        title = d.Title, path = d.PathName, isActive = d.Equals(app.ActiveUIDocument?.Document), isModified = d.IsModified,
        isFamilyDocument = d.IsFamilyDocument, isLinked = d.IsLinked, isWorkshared = d.IsWorkshared,
        isReadOnly = d.IsReadOnly, isReadOnlyFile = d.IsReadOnlyFile, hasSavedPath = !string.IsNullOrEmpty(d.PathName) }).ToArray();
    private void Verify(UIApplication app)
    {
        var allowed = new[] { active, linked, workshared, readOnly, family };
        if (active is null || !active.IsValidObject || active.PathName != Text("a") || active.IsModified || active.IsWorkshared
            || app.Application.Documents.Cast<Document>().Any(d => !allowed.Any(o => o is { IsValidObject: true } && o.Equals(d)))) throw new InvalidDataException("Unexpected native model/state; preserve it.");
        if (app.ActiveUIDocument?.Document is not { } current || !allowed.Any(o => o is { IsValidObject: true } && o.Equals(current))) throw new InvalidDataException("Unowned active document.");
        if (linked is null || !linked.IsValidObject || !linked.IsLinked || linked.PathName != Text("link")
            || instance?.GetLinkDocument() is not { } actual || !actual.Equals(linked)) throw new InvalidDataException("Native link relationship changed.");
        if (Hash(Text("source")) != Text("sourceSha256") || Hash(Text("b")) != Text("sourceSha256") || Hash(Text("template")) != Text("templateSha256")) throw new InvalidDataException("Source/template changed.");
        foreach (var entry in hashes) if (Hash(entry.Key) != entry.Value) throw new InvalidDataException("Authored saved bytes changed: " + entry.Key);
    }
    private static void Close(Document? document, string path, UIApplication app, bool allowDirty)
    {
        if (document is null || !document.IsValidObject) return;
        if (document.PathName != path || document.Equals(app.ActiveUIDocument?.Document) || !allowDirty && document.IsModified)
            throw new InvalidDataException("Preserve unexpected/active/dirty fixture.");
        if (!document.Close(false)) throw new InvalidDataException("Owned fixture teardown refused.");
    }
    private static void AssertProject(Document document, string path)
    {
        if (!document.IsValidObject || document.PathName != path || document.IsWorkshared || document.IsFamilyDocument || document.IsLinked
            || document.IsReadOnly || document.IsReadOnlyFile || document.IsModifiable || document.IsModified) throw new InvalidDataException("Exact clean editable nonworkshared project required before fixture authoring.");
    }
    private void Fail(Exception error)
    {
        failed = true;
        if (folder is not null && Directory.Exists(folder)) Write("probe-error.json", new { error = error.GetType().Name, error.Message, error.StackTrace, pid = Environment.ProcessId });
    }
    public Result OnShutdown(UIControlledApplication app)
    { if (folder is not null) app.Idling -= Idle; externalEvent?.Dispose(); plan?.Dispose(); return Result.Succeeded; }
}
