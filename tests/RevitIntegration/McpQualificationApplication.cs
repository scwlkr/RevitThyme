using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using RevitThyme.Addin;
using RevitThyme.Core;
using ParameterValue = RevitThyme.Core.ParameterValue;

namespace RevitThyme.RevitIntegration;

// Disposable native evaluator, never included in product packages. Model checks use direct Revit API.
public sealed class McpQualificationApplication : IExternalApplication, IExternalEventHandler
{
    private string? folder;
    private string fixture = "";
    private string hash = "";
    private string[] walls = [];
    private int elementCount;
    private int stage;
    private ExternalEvent? switchEvent;
    private volatile bool stopped;
    private System.Threading.Timer? pulse;
    private DateTime started;
    private readonly List<object> checks = [];
    public Result OnStartup(UIControlledApplication app)
    {
        folder = Environment.GetEnvironmentVariable("REVITTHYME_MCP_TEST_RUN");
        if (folder is null) return Result.Succeeded;
        fixture = Path.Combine(folder, "Foundation-Test.rvt");
        if (!Path.IsPathFullyQualified(folder) || !Guid.TryParse(Path.GetFileName(folder), out _)
            || fixture != Environment.GetEnvironmentVariable("REVITTHYME_FOUNDATION_FIXTURE") || File.Exists(fixture)) return Result.Failed;
        switchEvent = ExternalEvent.Create(this); app.Idling += Idle; started = DateTime.UtcNow;
        pulse = new System.Threading.Timer(_ => { if (!stopped) { try { switchEvent.Raise(); } catch (Exception) { } } }, null, 1000, 500);
        return Result.Succeeded;
    }
    private void Check(string name, bool value)
    { checks.Add(new { name, passed = value }); if (!value) throw new InvalidOperationException("Native qualification failed: " + name); }
    private void Idle(object? sender, IdlingEventArgs args)
        => Advance((UIApplication)sender!);
    private void Advance(UIApplication app)
    {
        if (stopped) return;
        try
        {
            if ((DateTime.UtcNow - started).TotalMinutes > 45) throw new TimeoutException("MCP qualification timeout.");
            var host = FoundationApplication.Instance ?? throw new InvalidOperationException("Host missing.");
            switch (stage)
            {
                case 0:
                    Check("empty owned Revit process", app.Application.Documents.Size == 0);
                    Check("no document rejected", new OperationExecutor().Execute(new RevitModel(app, host), new(Guid.NewGuid(), OperationKind.Inspect)).Code == "no_document");
                    Fixture.Create(app, fixture, true); stage++; break;
                case 1:
                    var plan = new FilteredElementCollector(app.ActiveUIDocument.Document).OfClass(typeof(ViewPlan)).Cast<ViewPlan>().Single(v => v.Name == "Foundation Plan");
                    app.ActiveUIDocument.RequestViewChange(plan); stage++; break;
                case 2:
                    if (app.ActiveUIDocument.ActiveView.Name != "Foundation Plan") return;
                    var doc = app.ActiveUIDocument.Document; hash = Hash(fixture);
                    elementCount = new FilteredElementCollector(doc).GetElementCount();
                    walls = new FilteredElementCollector(doc).OfClass(typeof(Wall)).Cast<Wall>().OrderBy(w => w.Id.Value).Select(w => w.UniqueId).ToArray();
                    Check("two populated fixture walls", walls.Length == 2);
                    File.WriteAllText(Path.Combine(folder!, "ready.json"), JsonSerializer.Serialize(new { binding = host.Binding, fixture, walls, elementCount, savedHash = hash }, Protocol.Json));
                    stage++; break;
                case 3:
                    if (!File.Exists(Path.Combine(folder!, "mcp-complete.json"))) return;
                    Verify(app, "MCP");
                    File.WriteAllText(Path.Combine(folder!, "mcp-native.json"), JsonSerializer.Serialize(new { passed = true, checks }, Protocol.Json));
                    stage++; break;
                case 4:
                    for (int round = 1; round <= 3; round++)
                    {
                        string ack = Path.Combine(folder!, $"codex-round-{round}.native.json");
                        if (File.Exists(Path.Combine(folder!, $"codex-round-{round}.request.json")) && !File.Exists(ack))
                        {
                            Verify(app, "Codex round " + round);
                            File.WriteAllText(ack, JsonSerializer.Serialize(new { passed = true, checks, host.Binding }, Protocol.Json));
                        }
                    }
                    if (!File.Exists(Path.Combine(folder!, "codex-complete.json"))) return;
                    Verify(app, "Codex");
                    var model = new RevitModel(app, host); var snapshot = model.Capture();
                    var edits = walls.Select((id, i) =>
                    {
                        var p = docParameter(app, id); string key = p.Id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                        return new ParameterEdit(id, key, new("String", Text: p.AsString()), new("String", Text: "Undo " + i));
                    }).ToArray();
                    Check("parameter Undo probe applies", new OperationExecutor().Execute(model, new(Guid.NewGuid(), OperationKind.SetParameters, snapshot.Target, Edits: edits)).Outcome == Outcome.Applied);
                    app.PostCommand(RevitCommandId.LookupPostableCommandId(PostableCommand.Undo)); stage++; break;
                case 5:
                    if (docParameter(app, walls[0]).AsString() != "Original A") return;
                    Check("one native Undo restores entire parameter batch", docParameter(app, walls[1]).AsString() == "Original B");
                    app.PostCommand(RevitCommandId.LookupPostableCommandId(PostableCommand.Redo)); stage++; break;
                case 6:
                    if (docParameter(app, walls[0]).AsString() != "Undo 0") return;
                    Check("one native Redo reapplies entire parameter batch", docParameter(app, walls[1]).AsString() == "Undo 1");
                    app.PostCommand(RevitCommandId.LookupPostableCommandId(PostableCommand.Undo)); stage++; break;
                case 7:
                    if (docParameter(app, walls[0]).AsString() != "Original A") return;
                    Verify(app, "final Undo"); stage++;
                    if (switchEvent!.Raise() is ExternalEventRequest.Denied or ExternalEventRequest.TimedOut) throw new InvalidOperationException("Switch scheduling failed.");
                    break;
            }
        }
        catch (Exception error) { Finish(false, error); }
    }
    private static Parameter docParameter(UIApplication app, string id) => app.ActiveUIDocument.Document.GetElement(id).get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
    private void Verify(UIApplication app, string source)
    {
        var doc = app.ActiveUIDocument.Document;
        Check(source + " exact fixture and native name", doc.PathName == fixture && doc.ActiveView.Name == "Foundation Plan");
        Check(source + " both comment values restored", docParameter(app, walls[0]).AsString() == "Original A" && docParameter(app, walls[1]).AsString() == "Original B");
        Check(source + " Double heights restored", walls.All(id => Math.Abs(doc.GetElement(id).get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM).AsDouble() - 9) < 1e-9));
        Check(source + " Integer values restored", walls.All(id => doc.GetElement(id).get_Parameter(BuiltInParameter.WALL_ATTR_ROOM_BOUNDING).AsInteger() == 1));
        Check(source + " native selection cleared", app.ActiveUIDocument.Selection.GetElementIds().Count == 0);
        Check(source + " element count unchanged", new FilteredElementCollector(doc).GetElementCount() == elementCount);
        Check(source + " saved file unchanged", Hash(fixture) == hash);
    }
    public void Execute(UIApplication app)
    {
        if (stage < 8) { Advance(app); return; }
        if (stage != 8 || stopped) return;
        try
        {
            var host = FoundationApplication.Instance!; var original = app.ActiveUIDocument.Document;
            using (var input = new FileStream(fixture, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var output = new FileStream(Path.Combine(folder!, "Switch-Test.rvt"), FileMode.CreateNew, FileAccess.Write)) input.CopyTo(output);
            app.OpenAndActivateDocument(Path.Combine(folder!, "Switch-Test.rvt"));
            Check("wrong document rejected by native host", new OperationExecutor().Execute(new RevitModel(app, host), new(Guid.NewGuid(), OperationKind.Inspect)).Code == "wrong_fixture");
            original.Close(false); app.OpenAndActivateDocument(fixture);
            Check("reopened document rejected by native host", new OperationExecutor().Execute(new RevitModel(app, host), new(Guid.NewGuid(), OperationKind.Inspect)).Code == "reopened_fixture");
            Finish(true);
        }
        catch (Exception error) { Finish(false, error); }
    }
    private void Finish(bool passed, Exception? error = null)
    {
        stopped = true;
        pulse?.Dispose();
        File.WriteAllText(Path.Combine(folder!, "native-result.json"), JsonSerializer.Serialize(new { passed, checks, fixture, walls, elementCount, savedHash = hash,
            binding = FoundationApplication.Instance?.Binding, build = System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileVersionInfo.FileVersion, error = error?.ToString() }, Protocol.Json));
    }
    public string GetName() => "RevitThyme MCP native qualification";
    private static string Hash(string path)
    { using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(); }
    public Result OnShutdown(UIControlledApplication app) { stopped = true; pulse?.Dispose(); if (folder is not null) app.Idling -= Idle; switchEvent?.Dispose(); return Result.Succeeded; }
}
