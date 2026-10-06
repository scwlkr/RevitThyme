using RevitThyme.Native;
using Range = RevitThyme.Native.Range;
namespace RevitThyme.AdapterChecks;
internal sealed class FakeModel : IModel
{
    public Target Target = new(Environment.ProcessId, System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks.ToString(), Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), "view-unique-64", 1);
    public RawRange Original = new(new("4294967301", 8), new("4294967301", 4.000000000000123), new("4294967301", 0), new("4294967301", -1));
    public RawRange Current;
    public string Fail = "";
    public string Restriction = "";
    public readonly List<string> Trace = [];
    public int Transactions;
    public Action? DuringSet;
    public TimeSpan CaptureAge = TimeSpan.Zero;
    public int OwnerThread = Environment.CurrentManagedThreadId;
    public FakeModel() { Current = Original; }
    private void Stage(string stage)
    {
        if (Environment.CurrentManagedThreadId != OwnerThread) throw new InvalidOperationException("API called from I/O thread.");
        Trace.Add(stage); if (Fail.Split('+').Contains(stage)) throw new IOException("Injected " + stage);
    }
    public Target CurrentTarget() { Stage("target"); return Target; }
    public Facts Capture()
    {
        Stage("capture"); if (Restriction.Length > 0) throw new InvalidDataException(Restriction);
        RevitThyme.Native.Plane Describe(RawPlane p, bool allow) => new(p.LevelId, "Fixture Level", 0, p.OffsetFeet, false, allow);
        var range = new Range(Describe(Current.Top, true), Describe(Current.Cut, false), Describe(Current.Bottom, true), Describe(Current.Depth, true));
        double[][] triangle = [[0, 0, 0], [10, 0, 0], [0, 10, 10]];
        var triangles = Enumerable.Range(0, 260).Select(_ => triangle).ToArray();
        return new(new(Guid.NewGuid().ToString(), Target, "Adapter orchestration fixture (no Revit)", "Floor Plan", "floor", range,
            [0, 0, -1, 10, 10, 10], 260, false, ["Offline .NET orchestration fixture; actual Revit API not executed."], 600), Current, triangles, DateTime.UtcNow - CaptureAge);
    }
    public RawRange Validate(Facts facts, Range proposed)
    {
        Stage("validate"); if (Restriction.Length > 0 || facts.Capture.Target != Target) throw new InvalidDataException("Unsupported state.");
        var p = proposed.Planes;
        if (p[1].Unlimited || p[0].OffsetFeet < p[1].OffsetFeet || p[2].OffsetFeet > p[1].OffsetFeet || p[3].OffsetFeet > p[2].OffsetFeet)
            throw new InvalidDataException("Native-invalid fixture range.");
        RawPlane Value(RevitThyme.Native.Plane plane) => new(plane.Unlimited ? "-1" : plane.LevelId, plane.OffsetFeet);
        return new(Value(p[0]), Value(p[1]), Value(p[2]), Value(p[3]));
    }
    public RawRange Read(Facts facts)
    {
        string stage = Trace.Contains("commit") ? "post_commit_read" : Transactions > 0 ? "pre_commit_read" : "read";
        if (Trace.Contains("rollback")) stage = "rollback_read";
        Stage(stage); return Current;
    }
    public IChange Begin(Facts facts) { Stage("begin"); return new FakeChange(this); }
    private sealed class FakeChange(FakeModel model) : IChange
    {
        public Completion StartGroup() { model.Stage("group_start"); return Completion.Started; }
        public Completion StartTransaction() { model.Stage("transaction_start"); model.Transactions++; return Completion.Started; }
        public void Set(RawRange range) { model.Current = range; model.DuringSet?.Invoke(); model.Stage("set"); }
        public void Regenerate() => model.Stage("regenerate");
        public Completion Commit() { model.Stage("commit"); return model.Fail == "pending" ? Completion.Pending : Completion.Committed; }
        public Completion Assimilate() { model.Stage("assimilate"); return model.Fail == "group_status" ? Completion.Other : Completion.Committed; }
        public Completion Rollback() { model.Stage("rollback"); if (model.Fail.Split('+').Contains("rollback_status")) return Completion.Other; model.Current = model.Original; return Completion.RolledBack; }
        public void Dispose() { }
    }
}
