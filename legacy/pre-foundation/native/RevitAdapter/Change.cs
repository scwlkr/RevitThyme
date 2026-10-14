using Autodesk.Revit.DB;
using RevitThyme.Native;
namespace RevitThyme.RevitAdapter;
internal sealed class Change(Document doc, ViewPlan view) : IChange
{
    private readonly TransactionGroup group = new(doc, "RevitThyme Visual View Range");
    private readonly Transaction transaction = new(doc, "Set plan view range");
    public Completion StartGroup() => Map(group.Start());
    public Completion StartTransaction()
    {
        var status = transaction.Start();
        if (status == TransactionStatus.Started)
            transaction.SetFailureHandlingOptions(transaction.GetFailureHandlingOptions().SetClearAfterRollback(true)
                .SetForcedModalHandling(true).SetFailuresPreprocessor(new FailClosed()));
        return Map(status);
    }
    public void Set(RawRange values)
    {
        using var range = view.GetViewRange(); var planes = new[] { values.Top, values.Cut, values.Bottom, values.Depth };
        for (int i = 0; i < planes.Length; i++)
        { range.SetLevelId(Model.Planes[i], new ElementId(long.Parse(planes[i].LevelId))); range.SetOffset(Model.Planes[i], planes[i].OffsetFeet); }
        view.SetViewRange(range);
    }
    public void Regenerate() => doc.Regenerate();
    public Completion Commit() => Map(transaction.Commit());
    public Completion Assimilate() => Map(group.Assimilate());
    public Completion Rollback()
    {
        if (transaction.GetStatus() == TransactionStatus.Pending) return Completion.Pending;
        if (transaction.GetStatus() == TransactionStatus.Started && transaction.RollBack() != TransactionStatus.RolledBack) return Completion.Other;
        if (group.GetStatus() == TransactionStatus.Started) return Map(group.RollBack());
        return Map(group.GetStatus());
    }
    public void Dispose() { transaction.Dispose(); group.Dispose(); }
    private static Completion Map(TransactionStatus status) => status switch
    {
        TransactionStatus.Started => Completion.Started, TransactionStatus.Committed => Completion.Committed,
        TransactionStatus.RolledBack => Completion.RolledBack, TransactionStatus.Pending => Completion.Pending, _ => Completion.Other
    };
    private sealed class FailClosed : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor failures)
        {
            // Conservative M2 policy: rollback any native failure, including warnings; never silently delete them.
            return failures.GetFailureMessages().Count > 0 ? FailureProcessingResult.ProceedWithRollBack : FailureProcessingResult.Continue;
        }
    }
}
