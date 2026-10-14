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

internal static class NativeDynamicExecution
{
    public static OperationResult Run(UIApplication app, RevitModel model, OperationRequest request)
    {
        var watch = Stopwatch.StartNew(); var code = request.Code!;
        string hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(code.Source)));
        var compiled = CSharpCompiler.Compile(code.Source, "Autodesk.Revit.DB.Document doc, Autodesk.Revit.UI.UIApplication uiapp, System.Threading.CancellationToken cancellationToken",
            "using System; using System.Linq; using System.Collections.Generic; using System.Threading; using Autodesk.Revit.DB; using Autodesk.Revit.DB.Architecture; using Autodesk.Revit.UI;",
            [typeof(Document).Assembly, typeof(UIApplication).Assembly]);
        using var submission = compiled.Code;
        CodeResult Report(JsonElement? value = null, CodeChanges? changes = null) => new(hash, watch.ElapsedMilliseconds, value, compiled.Diagnostics, changes);
        if (submission is null) return new(request.Id, Outcome.Rejected, "compile_error", "C# compilation failed; no transaction opened.", Execution: Report());
        var doc = app.ActiveUIDocument.Document;
        var originalIds = Ids(doc);
        using var change = new RevitChange(doc, doc.ActiveView, request.Operation == OperationKind.RunQuery ? "RevitThyme C# Query (rollback)" : "RevitThyme C# Modify");
        var added = new HashSet<long>(); var modified = new HashSet<long>(); var deleted = new HashSet<long>();
        void Changed(object? sender, DocumentChangedEventArgs e)
        {
            if (!doc.Equals(e.GetDocument())) return;
            added.UnionWith(e.GetAddedElementIds().Select(i => i.Value)); modified.UnionWith(e.GetModifiedElementIds().Select(i => i.Value)); deleted.UnionWith(e.GetDeletedElementIds().Select(i => i.Value));
        }
        app.Application.DocumentChanged += Changed;
        try
        {
            Require(change.StartGroup(), Completion.Started); Require(change.StartTransaction(), Completion.Started);
            using var budget = new CancellationTokenSource(code.BudgetMilliseconds);
            var execution = Stopwatch.StartNew();
            object? raw = submission.Run(doc, app, budget.Token);
            // Serialization runs while rollback is still possible, including user getters/converters.
            var value = JsonSerializer.SerializeToElement(raw, Protocol.Json);
            if (Encoding.UTF8.GetByteCount(value.GetRawText()) > DynamicExecutor.MaxValueBytes)
                throw new OperationException("result_too_large", "Return at most 8 KiB of JSON primitives, arrays or records; page large queries.");
            if (execution.ElapsedMilliseconds > code.BudgetMilliseconds || budget.IsCancellationRequested)
                throw new OperationException("budget_exceeded", "Cooperative execution budget exceeded; changes rolled back after code returned.");
            if (!doc.IsValidObject || !doc.Equals(app.ActiveUIDocument?.Document)) throw new InvalidOperationException("Submission changed document context.");
            change.Regenerate();
            if (request.Operation == OperationKind.RunQuery)
            {
                Require(change.RollBack(), Completion.RolledBack); VerifyIds(doc, originalIds);
                return new(request.Id, Outcome.Inspected, "inspected", "C# result serialized; all current-document transaction changes rolled back.", model.Capture(), Execution: Report(value));
            }
            Require(change.Commit(), Completion.Committed);
            // Native DocumentChanged IDs are collected after commit, never taken from script output.
            var changes = new CodeChanges(added.Order().ToArray(), modified.Except(added).Except(deleted).Order().ToArray(), deleted.Order().ToArray());
            if (changes.Added.Length + changes.Modified.Length + changes.Deleted.Length > DynamicExecutor.MaxChangedIds)
                throw new OperationException("changes_too_large", "Change set exceeds 512 native IDs; split the operation.");
            var after = model.Capture(); var report = Report(value, changes);
            var result = new OperationResult(request.Id, Outcome.Applied, "applied", "Trusted C# completed; native changes committed as one Revit Undo item.", after,
                ChangedIds: changes.Added.Concat(changes.Modified).Concat(changes.Deleted).Distinct().ToArray(), Execution: report);
            if (JsonSerializer.SerializeToUtf8Bytes(result, Protocol.Json).Length > Protocol.MaxBytes / 2)
                throw new OperationException("result_too_large", "Execution response exceeds the native result budget.");
            Require(change.Assimilate(), Completion.Committed); return result;
        }
        catch (Exception error)
        {
            string message = error.Message[..Math.Min(240, error.Message.Length)];
            try
            {
                Require(change.RollBack(), Completion.RolledBack); VerifyIds(doc, originalIds);
                return new(request.Id, Outcome.RolledBack, error is OperationException op ? op.Code : error is OperationCanceledException ? "budget_exceeded" : "runtime_error",
                    message, model.Capture(), Execution: Report());
            }
            catch (Exception) { return new(request.Id, Outcome.Unconfirmed, "unconfirmed", "Completion/rollback uncertain. Poll the request ID and inspect before another edit.", Execution: Report()); }
        }
        finally { app.Application.DocumentChanged -= Changed; }
    }
    private static long[] Ids(Document doc)
    {
        using var collector = new FilteredElementCollector(doc);
        return collector.WherePasses(new LogicalOrFilter(new ElementIsElementTypeFilter(), new ElementIsElementTypeFilter(true)))
            .ToElementIds().Select(i => i.Value).Order().ToArray();
    }
    private static void VerifyIds(Document doc, long[] original) { if (!Ids(doc).SequenceEqual(original)) throw new InvalidDataException("Rollback element identity verification failed."); }
    private static void Require(Completion actual, Completion expected) { if (actual != expected) throw new InvalidDataException("Unexpected native transaction status."); }
}
