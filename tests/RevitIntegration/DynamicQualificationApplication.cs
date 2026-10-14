using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using RevitThyme.Addin;
using RevitThyme.CabinEval;
using RevitThyme.Core;

namespace RevitThyme.RevitIntegration;

public sealed class DynamicQualificationApplication : IExternalApplication, IExternalEventHandler
{
    private string? folder;
    private string fixture = "", savedHash = "";
    private int stage, count;
    private bool stopped;
    private DateTime started;
    private ExternalEvent? pulseEvent;
    private System.Threading.Timer? pulse;
    private readonly List<object> checks = [];
    public Result OnStartup(UIControlledApplication app)
    {
        folder = Environment.GetEnvironmentVariable("REVITTHYME_DYNAMIC_TEST_RUN");
        if (folder is null) return Result.Succeeded;
        fixture = Path.Combine(folder, "Foundation-Test.rvt");
        if (!Path.IsPathFullyQualified(folder) || !Guid.TryParse(Path.GetFileName(folder), out _) || File.Exists(fixture)
            || fixture != Environment.GetEnvironmentVariable("REVITTHYME_FOUNDATION_FIXTURE")) return Result.Failed;
        started = DateTime.UtcNow; pulseEvent = ExternalEvent.Create(this); app.Idling += Idle;
        pulse = new(_ => { if (!stopped) { try { pulseEvent.Raise(); } catch (Exception) { } } }, null, 1000, 500);
        return Result.Succeeded;
    }
    private void Check(string name, bool passed) { checks.Add(new { name, passed }); if (!passed) throw new InvalidOperationException(name); }
    private void Idle(object? sender, IdlingEventArgs e) => Advance((UIApplication)sender!);
    public void Execute(UIApplication app) => Advance(app);
    private void Advance(UIApplication app)
    {
        if (stopped) return;
        try
        {
            if ((DateTime.UtcNow - started).TotalMinutes > 20) throw new TimeoutException("Dynamic qualification timed out.");
            var host = FoundationApplication.Instance ?? throw new InvalidOperationException("Host missing.");
            var doc = app.ActiveUIDocument?.Document;
            switch (stage)
            {
                case 0:
                    Check("fresh empty owned process", app.Application.Documents.Size == 0); Fixture.Create(app, fixture); stage++; break;
                case 1:
                    app.ActiveUIDocument!.RequestViewChange(new FilteredElementCollector(doc!).OfClass(typeof(ViewPlan)).Cast<ViewPlan>().Single(v => v.Name == "Foundation Plan")); stage++; break;
                case 2:
                    if (doc!.ActiveView.Name != "Foundation Plan") return;
                    savedHash = Hash(); count = Count(doc);
                    Smoke(app, host, doc);
                    Check("smoke restores fixture identities and saved file", Count(doc) == count && doc.ActiveView.Name == "Foundation Plan" && Hash() == savedHash);
                    Write("ready.json", new { host.Binding, fixture, count, savedHash }); stage++; break;
                case 3:
                    if (!File.Exists(Path.Combine(folder!, "smoke-complete.json"))) return;
                    Check("MCP modify creates two native walls and renames view", Count(doc!) > count && Walls(doc!).Length == 2 && doc!.ActiveView.Name == "Dynamic modified");
                    app.PostCommand(RevitCommandId.LookupPostableCommandId(PostableCommand.Undo)); stage++; break;
                case 4:
                    if (Walls(doc!).Length != 0) return;
                    Check("single native Undo restores complete dynamic modification", Count(doc!) == count && doc!.ActiveView.Name == "Foundation Plan");
                    app.PostCommand(RevitCommandId.LookupPostableCommandId(PostableCommand.Redo)); stage++; break;
                case 5:
                    if (Walls(doc!).Length != 2) return;
                    Check("single native Redo restores dynamic modification", doc!.ActiveView.Name == "Dynamic modified");
                    app.PostCommand(RevitCommandId.LookupPostableCommandId(PostableCommand.Undo)); stage++; break;
                case 6:
                    if (Walls(doc!).Length != 0) return;
                    Check("empty model evaluator awards no geometry/BIM/parametric points", CabinEvaluator.Evaluate(doc!).Items.Where(i => i.Area != "Integrity").All(i => !i.Passed));
                    Write("cabin-ready.json", new { passed = true }); stage++; break;
                case 7:
                    if (!File.Exists(Path.Combine(folder!, "dynamic-mcp-complete.json"))) return;
                    Check("MCP cabin has four actual native walls", Walls(doc!).Length == 4);
                    var report = CabinEvaluator.Evaluate(doc!); Write("cabin-score.json", report);
                    Check("independent evaluator uses full 100-point rubric and restores probe", report.Maximum == 100 && report.ProbeRestored);
                    Check("baseline has actual native geometry and BIM content", report.Items.Where(i => i.Area is "Geometry" or "BIM").All(i => i.Passed));
                    Check("baseline length parameter actually drives native walls and dimensions", report.Items.Any(i => i.Name == "Walls respond to length 18, retain depth and height" && i.Passed)
                        && report.Items.Any(i => i.Name == "Associative dimensions read 18 and 16" && i.Passed));
                    // Negative control: independently remove the floor, rescore, then restore.
                    using (var g = new TransactionGroup(doc!, "Evaluator negative control"))
                    {
                        g.Start(); using (var t = new Transaction(doc!, "Remove floor temporarily")) { t.Start(); doc!.Delete(new FilteredElementCollector(doc).OfClass(typeof(Floor)).FirstElementId()); t.Commit(); }
                        var negative = CabinEvaluator.Evaluate(doc!); Write("cabin-negative-score.json", negative);
                        Check("evaluator detects missing native geometry", negative.Score < report.Score && !negative.Items.Single(i => i.Name == "Native floor covers footprint").Passed);
                        Check("negative control restores model", g.RollBack() == TransactionStatus.RolledBack);
                    }
                    Check("saved disposable fixture unchanged", Hash() == savedHash);
                    // Preserve a separate result artifact, never save over the original fixture.
                    doc!.SaveAs(Path.Combine(folder!, "Cabin-Result.rvt"), new SaveAsOptions { OverwriteExistingFile = false });
                    Write("cabin-artifact.json", new { path = doc.PathName, sha256 = FileHash(doc.PathName), report.Score });
                    Finish(true); break;
            }
        }
        catch (Exception e) { Finish(false, e); }
    }
    private void Smoke(UIApplication app, FoundationApplication host, Document doc)
    {
        var model = new RevitModel(app, host); var executor = new OperationExecutor();
        OperationResult Run(string source, bool modify = false, int budget = 10000) => executor.Execute(model,
            new(Guid.NewGuid(), modify ? OperationKind.RunModify : OperationKind.RunQuery, model.Capture().Target, Code: new(source, true, budget)));
        var invalid = Run("return no_such_symbol;");
        Check("real Roslyn reports mapped compile diagnostics", invalid.Code == "compile_error" && invalid.Execution!.Diagnostics!.Any(d => d.Line == 1));
        var query = Run("doc.ActiveView.Name = \"Temporary query\"; return new { title = doc.ActiveView.Name, count = new FilteredElementCollector(doc).GetElementCount() };");
        Write("native-query-result.json", query);
        Check("query returns detached JSON and rolls back parameter edits", query.Succeeded && query.Execution!.Value!.Value.GetProperty("title").GetString() == "Temporary query" && doc.ActiveView.Name == "Foundation Plan");
        var failed = Run("doc.ActiveView.Name = \"Failure test\"; throw new InvalidOperationException(\"injected failure\");", true);
        Check("runtime exception rolls back native modification", failed.Outcome == Outcome.RolledBack && failed.Code == "runtime_error" && doc.ActiveView.Name == "Foundation Plan");
        var huge = Run("doc.ActiveView.Name = \"Oversized\"; return new string('x', 9000);", true);
        Check("oversized result rolls back before commit", huge.Code == "result_too_large" && huge.Outcome == Outcome.RolledBack && doc.ActiveView.Name == "Foundation Plan");
        var serialization = Run("doc.ActiveView.Name = \"Bad JSON\"; return double.NaN;", true);
        Check("serialization failure rolls back native edit", serialization.Code == "runtime_error" && doc.ActiveView.Name == "Foundation Plan");
        var budget = Run("doc.ActiveView.Name = \"Budget\"; System.Threading.Thread.Sleep(150); return 1;", true, 100);
        Check("cooperative budget rolls back after code returns", budget.Code == "budget_exceeded" && doc.ActiveView.Name == "Foundation Plan");
        var cancelled = Run("cancellationToken.WaitHandle.WaitOne(150); cancellationToken.ThrowIfCancellationRequested(); return 1;", true, 100);
        Check("cooperative cancellation exception rolls back", cancelled.Code == "budget_exceeded" && cancelled.Outcome == Outcome.RolledBack);
        var large = Run("for(int i=0;i<513;i++) Level.Create(doc,100+i*10); return true;", true);
        Check("oversized native change set rolls back after commit", large.Code == "changes_too_large" && large.Outcome == Outcome.RolledBack && Count(doc) == count);
        var made = Run("Level.Create(doc,100); Level.Create(doc,110); return true;", true);
        var added = made.Execution?.Changes?.Added ?? [];
        Check("native added IDs come from committed document event", made.Outcome == Outcome.Applied && added.Length == 2 && added.All(id => doc.GetElement(new ElementId(id)) is Level));
        var removed = Run(string.Join(" ", added.Select(id => "doc.Delete(new ElementId(" + id + "));")) + " return true;", true);
        Check("native deleted IDs come from committed document event", removed.Outcome == Outcome.Applied && removed.Execution!.Changes!.Deleted.SequenceEqual(added) && Count(doc) == count);
        Check("query side effect created no surviving elements", Count(doc) == count);
    }
    private static Wall[] Walls(Document doc) => new FilteredElementCollector(doc).OfClass(typeof(Wall)).Cast<Wall>().ToArray();
    private static int Count(Document doc) => new FilteredElementCollector(doc).GetElementCount();
    private string Hash() => FileHash(fixture);
    private static string FileHash(string path) { using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); return Convert.ToHexString(SHA256.HashData(stream)); }
    private void Write(string name, object value) => File.WriteAllText(Path.Combine(folder!, name), JsonSerializer.Serialize(value, Protocol.Json));
    private void Finish(bool passed, Exception? error = null)
    {
        stopped = true; pulse?.Dispose(); Write("native-result.json", new { passed, stage, checks, fixture, count, savedHash, binding = FoundationApplication.Instance?.Binding, error = error?.ToString() });
    }
    public string GetName() => "RevitThyme dynamic execution qualification";
    public Result OnShutdown(UIControlledApplication app) { stopped = true; pulse?.Dispose(); if (folder is not null) app.Idling -= Idle; pulseEvent?.Dispose(); return Result.Succeeded; }
}
