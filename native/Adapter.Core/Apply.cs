namespace RevitThyme.Native;
public enum Completion { Started, Committed, RolledBack, Pending, Other }
// Only the Revit implementation can call native APIs; the queue invokes it in Execute.
public interface IModel
{
    Target CurrentTarget();
    Facts Capture();
    RawRange Validate(Facts facts, Range requested);
    RawRange Read(Facts facts);
    IChange Begin(Facts facts);
}
public interface IChange : IDisposable
{
    Completion StartGroup();
    Completion StartTransaction();
    void Set(RawRange range);
    void Regenerate();
    Completion Commit();
    Completion Assimilate();
    Completion Rollback();
}
public static class Apply
{
    public static Reply Execute(IModel model, Facts facts, Request request)
    {
        RawRange desired;
        try
        {
            desired = model.Validate(facts, request.Range!);
            if (model.Read(facts) != facts.Original) return Reply.Result(request, Status.Rejected, "stale_original", "Range changed. Refresh.", true);
            if (desired == facts.Original) return Reply.Result(request, Status.UnchangedVerified, "unchanged", "Native values verified; no transaction opened.", values: desired);
        }
        catch (Rejection ex) { return Reply.Result(request, Status.Rejected, ex.Code, ex.Message, true); }
        catch { return Reply.Result(request, Status.Rejected, "native_validation", "Native target or range rejected. Refresh.", true); }
        IChange? change = null;
        bool started = false;
        try
        {
            change = model.Begin(facts);
            // A failed start may have changed native state. Attempt rollback and independent readback.
            started = true;
            Require(change.StartGroup(), Completion.Started);
            Require(change.StartTransaction(), Completion.Started);
            change.Set(desired); change.Regenerate();
            if (model.Read(facts) != desired) throw new InvalidDataException("Pre-commit readback.");
            Require(change.Commit(), Completion.Committed);
            // Must occur before assimilation removes the group rollback opportunity.
            if (model.Read(facts) != desired) throw new InvalidDataException("Post-commit readback.");
            Require(change.Assimilate(), Completion.Committed);
            return Reply.Result(request, Status.AppliedVerified, "applied", "Native range verified before group assimilation; one undo item.", values: desired, changed: [facts.Capture.Target.ViewId]);
        }
        catch
        {
            if (started && change is not null)
            {
                try
                {
                    if (change.Rollback() == Completion.RolledBack && model.Read(facts) == facts.Original)
                        return Reply.Result(request, Status.RollbackConfirmed, "rolled_back", "Rollback and original native values verified.", true, facts.Original);
                }
                catch { /* No evidence of unchanged state. */ }
            }
            return Reply.Result(request, Status.OutcomeUnconfirmed, "outcome_unconfirmed", "Completion or rollback could not be confirmed. Query outcome and refresh; do not retry Apply.", true);
        }
        finally
        {
            // Dispose must never overwrite an honest mutation result with a transport exception.
            try { change?.Dispose(); } catch { }
        }
    }
    private static void Require(Completion actual, Completion expected)
    {
        if (actual != expected) throw new InvalidDataException("Native completion: " + actual);
    }
}
