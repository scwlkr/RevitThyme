using Autodesk.Revit.DB;
using RevitThyme.Core;

namespace RevitThyme.Addin;

internal sealed class RevitChange(Document document, View view) : IChange
{
    private readonly TransactionGroup group = new(document, "RevitThyme Rename View");
    private readonly Transaction transaction = new(document, "Rename view");
    public Completion StartGroup() => Map(group.Start());
    public Completion StartTransaction()
    {
        var status = transaction.Start();
        if (status == TransactionStatus.Started)
            transaction.SetFailureHandlingOptions(transaction.GetFailureHandlingOptions()
                .SetClearAfterRollback(true).SetForcedModalHandling(true).SetFailuresPreprocessor(new FailClosed()));
        return Map(status);
    }
    public void RenameView(string name) => view.Name = name;
    public void Regenerate() => document.Regenerate();
    public Completion Commit() => Map(transaction.Commit());
    public Completion Assimilate() => Map(group.Assimilate());
    public Completion RollBack()
    {
        if (transaction.GetStatus() == TransactionStatus.Pending) return Completion.Other;
        if (transaction.GetStatus() == TransactionStatus.Started && transaction.RollBack() != TransactionStatus.RolledBack)
            return Completion.Other;
        return group.GetStatus() == TransactionStatus.Started ? Map(group.RollBack()) : Map(group.GetStatus());
    }
    public void Dispose() { transaction.Dispose(); group.Dispose(); }
    private static Completion Map(TransactionStatus status) => status switch
    {
        TransactionStatus.Started => Completion.Started, TransactionStatus.Committed => Completion.Committed,
        TransactionStatus.RolledBack => Completion.RolledBack, _ => Completion.Other
    };
    private sealed class FailClosed : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor failures) =>
            failures.GetFailureMessages().Count > 0 ? FailureProcessingResult.ProceedWithRollBack : FailureProcessingResult.Continue;
    }
}
