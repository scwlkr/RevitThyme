using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitThyme.Native;
using RevitThyme.RevitAdapter;
using Range = RevitThyme.Native.Range;

namespace RevitThyme.Qualification;
// Explicit developer qualification only: never copied into the desktop/native payload or registered.
// Invoke in a valid Revit API context on an approved disposable fixture, not through product IPC.
public enum FailurePoint { BeforeSet, AfterSet, AfterRegenerate, BeforeCommit, AfterCommit, PostCommitRead, BeforeAssimilate, AfterAssimilate, NativeWarning, NativeError }
public static class RollbackProbe
{
    public static object InvalidRange(UIApplication app, Host host, string approvedPath, string approvedView)
    {
        var doc = app.ActiveUIDocument.Document;
        if (!string.Equals(doc.PathName, approvedPath, StringComparison.OrdinalIgnoreCase) || doc.ActiveView.UniqueId != approvedView
            || doc.IsWorkshared || doc.IsReadOnly || doc.IsModifiable) throw new InvalidOperationException("Approved disposable target required.");
        var model = new Model(app, host); var facts = model.Capture(); var before = model.Read(facts);
        var proposed = facts.Capture.Original with { Cut = facts.Capture.Original.Cut with { OffsetFeet = 1_000_000 } };
        var request = new Request(Wire.Protocol, Guid.NewGuid().ToString(), Operation.Apply, facts.Capture.Target,
            facts.Capture.SnapshotId, proposed, 0, "", true);
        var reply = Apply.Execute(model, facts, request); var after = model.Read(facts);
        return new { Reply = reply, Before = before, After = after, OriginalRestored = before == after };
    }
    public static object Run(UIApplication app, Host host, string approvedPath, string approvedView, FailurePoint point)
    {
        var doc = app.ActiveUIDocument.Document;
        if (!string.Equals(doc.PathName, approvedPath, StringComparison.OrdinalIgnoreCase) || doc.ActiveView.UniqueId != approvedView
            || doc.IsWorkshared || doc.IsReadOnly || doc.IsModifiable || !Enum.IsDefined(point))
            throw new InvalidOperationException("Approved disposable target required.");
        var native = new Model(app, host); var facts = native.Capture(); var before = native.Read(facts);
        var proposed = facts.Capture.Original with { Cut = facts.Capture.Original.Cut with { OffsetFeet = 5 } };
        if (before.Cut.OffsetFeet == 5) throw new InvalidOperationException("Probe requires a different original Cut.");
        var probe = new ProbeModel(native, doc, point);
        var request = new Request(Wire.Protocol, Guid.NewGuid().ToString(), Operation.Apply, facts.Capture.Target,
            facts.Capture.SnapshotId, proposed, 0, "", true);
        var reply = Apply.Execute(probe, facts, request);
        // Independent production read, outside the injected readback adapter.
        var after = native.Read(facts);
        return new { Point = point.ToString(), Reply = reply, Before = before, After = after, OriginalRestored = before == after, Trace = probe.Trace };
    }
    private sealed class ProbeModel(IModel native, Document doc, FailurePoint point) : IModel
    {
        public readonly List<string> Trace = [];
        private bool committed;
        private bool readFailed;
        public Target CurrentTarget() => native.CurrentTarget();
        public Facts Capture() => native.Capture();
        public RawRange Validate(Facts facts, Range range) => native.Validate(facts, range);
        public RawRange Read(Facts facts)
        {
            if (committed && !readFailed && point == FailurePoint.PostCommitRead)
            { readFailed = true; Trace.Add("injected post-commit read failure"); throw new IOException("Qualification readback failure."); }
            return native.Read(facts);
        }
        public IChange Begin(Facts facts) => new ProbeChange(native.Begin(facts), this, doc, point);
        private sealed class ProbeChange(IChange native, ProbeModel owner, Document doc, FailurePoint point) : IChange
        {
            private void Fail(FailurePoint stage)
            {
                if (point == stage) { owner.Trace.Add("injected " + stage); throw new IOException("Qualification failure: " + stage); }
            }
            private Completion Observe(string stage, Completion value) { owner.Trace.Add(stage + ": " + value); return value; }
            public Completion StartGroup() => Observe("group start", native.StartGroup());
            public Completion StartTransaction() => Observe("transaction start", native.StartTransaction());
            public void Set(RawRange range) { Fail(FailurePoint.BeforeSet); native.Set(range); owner.Trace.Add("native set"); Fail(FailurePoint.AfterSet); }
            public void Regenerate() { native.Regenerate(); owner.Trace.Add("native regenerate"); Fail(FailurePoint.AfterRegenerate); }
            public Completion Commit()
            {
                Fail(FailurePoint.BeforeCommit);
                if (point is FailurePoint.NativeWarning or FailurePoint.NativeError)
                {
                    using var message = new FailureMessage(point == FailurePoint.NativeWarning
                        ? BuiltInFailures.GeneralFailures.GenericWarning : BuiltInFailures.GeneralFailures.GenericError);
                    var expected = point == FailurePoint.NativeWarning ? FailureSeverity.Warning : FailureSeverity.Error;
                    if (message.GetSeverity() != expected) throw new InvalidOperationException("Unexpected native failure severity; no failure posted.");
                    message.SetFailingElement(doc.ActiveView.Id); doc.PostFailure(message);
                    owner.Trace.Add("posted native " + message.GetSeverity());
                }
                var result = Observe("native commit", native.Commit()); owner.committed = result == Completion.Committed;
                Fail(FailurePoint.AfterCommit); return result;
            }
            public Completion Assimilate()
            {
                Fail(FailurePoint.BeforeAssimilate); var result = Observe("native assimilate", native.Assimilate());
                Fail(FailurePoint.AfterAssimilate); return result;
            }
            public Completion Rollback() => Observe("native rollback", native.Rollback());
            public void Dispose() => native.Dispose();
        }
    }
}
