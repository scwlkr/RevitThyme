using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using RevitThyme.Core;
using RevitThyme.Execution;

namespace RevitThyme.Addin;

/// <summary>Observation has no active transaction. A surrounding group discards independently committed checker transactions.</summary>
internal static class NativeObservationExecution
{
    public static OperationResult Run(UIApplication app, RevitModel model, OperationRequest request)
    {
        var watch = Stopwatch.StartNew(); var code = request.Code!;
        string hash = ResultChecks.Hash(code.Source);
        var compiled = CSharpCompiler.Compile(code.Source,
            "Autodesk.Revit.DB.Document doc, Autodesk.Revit.UI.UIApplication uiapp, System.Threading.CancellationToken cancellationToken, System.Text.Json.JsonElement data",
            "using System; using System.Linq; using System.Collections.Generic; using System.Threading; using System.Text.Json; using Autodesk.Revit.DB; using Autodesk.Revit.DB.Architecture; using Autodesk.Revit.UI;",
            [typeof(Document).Assembly, typeof(UIApplication).Assembly]);
        using var submission = compiled.Code;
        CodeResult Report(JsonElement? value = null) => new(hash, watch.ElapsedMilliseconds, value, compiled.Diagnostics, BeforeTarget: request.Target);
        if (submission is null) return new(request.Id, Outcome.Rejected, "compile_error", "Observation compilation failed.", Execution: Report());
        var doc = app.ActiveUIDocument.Document; var before = model.Capture();
        var ids = Ids(doc); bool changed = false;
        void Changed(object? sender, DocumentChangedEventArgs e)
        {
            if (doc.Equals(e.GetDocument()) && (e.GetAddedElementIds().Count != 0 || e.GetModifiedElementIds().Count != 0 || e.GetDeletedElementIds().Count != 0)) changed = true;
        }
        app.Application.DocumentChanged += Changed;
        using var group = new TransactionGroup(doc, "RevitThyme observation guard");
        try
        {
            if (doc.IsModifiable || group.Start() != TransactionStatus.Started) throw new InvalidOperationException("Observation requires no active transaction.");
            using var budget = new CancellationTokenSource(code.BudgetMilliseconds);
            var execution = Stopwatch.StartNew();
            var raw = submission.Run(doc, app, budget.Token, code.ObservationData ?? JsonSerializer.SerializeToElement(new { }));
            var value = JsonSerializer.SerializeToElement(raw, Protocol.Json);
            if (Encoding.UTF8.GetByteCount(value.GetRawText()) > DynamicExecutor.MaxValueBytes)
                throw new OperationException("result_too_large", "Observation JSON exceeds 8 KiB.");
            if (execution.ElapsedMilliseconds > code.BudgetMilliseconds || budget.IsCancellationRequested)
                throw new OperationException("budget_exceeded", "Observation exceeded its cooperative budget.");
            if (changed || doc.IsModifiable || !doc.Equals(app.ActiveUIDocument?.Document) || doc.ActiveView.UniqueId != before.Target.ViewId)
                throw new OperationException("observation_mutated", "Checker attempted a native transaction or changed native context; result cannot be certified.");
            RollBack();
            var after = model.Capture();
            if (changed || after.Target != before.Target)
                throw new OperationException("observation_mutated", "Native changes occurred during observation; result cannot be certified.");
            return new(request.Id, Outcome.Inspected, "observed", "Read-only native observation completed outside an active transaction.", after, Execution: Report(value));
        }
        catch (Exception error)
        {
            try
            {
                RollBack();
                return new(request.Id, Outcome.RolledBack, error is OperationException op ? op.Code : "runtime_error",
                    error.Message[..Math.Min(240, error.Message.Length)], model.Capture(), Execution: Report());
            }
            catch (Exception) { return new(request.Id, Outcome.Unconfirmed, "unconfirmed", "Checker rollback/context uncertain. Inspect native state; no verification pass.", Execution: Report()); }
        }
        finally { app.Application.DocumentChanged -= Changed; }
        void RollBack()
        {
            if (group.GetStatus() == TransactionStatus.Started && group.RollBack() != TransactionStatus.RolledBack)
                throw new InvalidDataException("Observation guard rollback failed.");
            if (!Ids(doc).SequenceEqual(ids) || doc.IsModifiable) throw new InvalidDataException("Observation guard identity/transaction verification failed.");
        }
    }
    private static long[] Ids(Document doc)
    {
        using var collector = new FilteredElementCollector(doc);
        return collector.WherePasses(new LogicalOrFilter(new ElementIsElementTypeFilter(), new ElementIsElementTypeFilter(true)))
            .ToElementIds().Select(i => i.Value).Order().ToArray();
    }
}
