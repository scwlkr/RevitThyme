using RevitThyme.Core;

namespace RevitThyme.Unit;

internal sealed class FakeModel : IModel
{
    public string Name { get; set; } = "Foundation Plan";
    public Target Target { get; set; } = new(123, "456", Guid.NewGuid().ToString(), "document", "view", 1);
    public bool Writable { get; set; } = true;
    public int Transactions { get; private set; }
    public bool FailSet { get; set; }
    public bool FailCommit { get; set; }
    public bool FailRollback { get; set; }
    public bool FailAfterCommit { get; set; }
    public bool FailBegin { get; set; }
    public bool WrongReadback { get; set; }
    public bool Committed { get; private set; }
    public Snapshot Capture() => new(Target, "2027", "Fixture", "fixture.rvt", false, Transactions > 0,
        Name, "FloorPlan", 42, Writable, Writable ? null : "Read-only fixture");
    public string ReadViewName()
    {
        if (Committed && FailAfterCommit) return "unexpected";
        return WrongReadback ? "unexpected" : Name;
    }
    public IChange BeginChange()
    {
        if (FailBegin) throw new OperationException("write_blocked", "Blocked");
        return new Change(this);
    }
    private sealed class Change(FakeModel model) : IChange
    {
        private readonly string original = model.Name;
        public Completion StartGroup() => Completion.Started;
        public Completion StartTransaction() { model.Transactions++; return Completion.Started; }
        public void RenameView(string name) { model.Name = name; if (model.FailSet) throw new InvalidOperationException("Native failure"); }
        public void Regenerate() { }
        public Completion Commit() { model.Committed = true; return model.FailCommit ? Completion.Other : Completion.Committed; }
        public Completion Assimilate() => Completion.Committed;
        public Completion RollBack()
        {
            if (model.FailRollback) return Completion.Other;
            model.Name = original; model.Committed = false; return Completion.RolledBack;
        }
        public void Dispose() { }
    }
}
