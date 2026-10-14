using RevitThyme.Core;

namespace RevitThyme.McpTests;

internal sealed class FakeGenericModel : IGenericModel
{
    public Target Target { get; set; } = new(1, "start", "session", "doc", "view", 1);
    public Dictionary<string, ParameterValue> Values { get; } = new() { ["1"] = new("String", Text: "original"), ["2"] = new("Double", Number: 9) };
    public string Name { get; set; } = "Plan";
    public string[] Selection { get; set; } = [];
    public int Transactions { get; set; }
    public int Writes { get; set; }
    public int FailWrite { get; set; }
    public bool FailCommit { get; set; }
    public bool FailRollback { get; set; }
    public bool WrongReadback { get; set; }
    public bool ReadOnly { get; set; }
    public Snapshot Capture() => new(Target, "2027", "Fake", "fake.rvt", false, false, Name, "FloorPlan", 42, !ReadOnly, "read only");
    public string ReadViewName() => Name;
    public IChange BeginChange() => new Change(this);
    public IParameterChange BeginParameterChange() => new Change(this);
    public ElementPage ListViews(ElementQuery query) => QueryElements(query);
    public ElementPage QueryElements(ElementQuery query) => new([Info("element")], null);
    public ElementDetail[] InspectElements(string[] ids, int parameterOffset) => ids.Select(id => new ElementDetail(Info(id), [], null)).ToArray();
    public string[] GetSelection() => Selection;
    public void SetSelection(string[] ids) => Selection = ids;
    public ParameterInfo ReadParameter(string elementId, string parameterId) => new(parameterId, "Parameter", "data", ReadOnly,
        WrongReadback && Writes > 0 ? new("String", Text: "wrong") : Values[parameterId]);
    private static ElementInfo Info(string id) => new(id, 42, "Element", "Walls", "-2000011", "Wall", false);
    private sealed class Change(FakeGenericModel model) : IParameterChange
    {
        private readonly Dictionary<string, ParameterValue> original = new(model.Values);
        private readonly string name = model.Name;
        public Completion StartGroup() => Completion.Started;
        public Completion StartTransaction() { model.Transactions++; return Completion.Started; }
        public void RenameView(string value) => model.Name = value;
        public void SetParameter(ParameterEdit edit)
        {
            model.Values[edit.ParameterId] = edit.Value; model.Writes++;
            if (model.Writes == model.FailWrite) throw new InvalidOperationException("Native setter failed.");
        }
        public void Regenerate() { }
        public Completion Commit() => model.FailCommit ? Completion.Other : Completion.Committed;
        public Completion Assimilate() => Completion.Committed;
        public Completion RollBack()
        {
            if (model.FailRollback) return Completion.Other;
            model.Values.Clear(); foreach (var pair in original) model.Values.Add(pair.Key, pair.Value);
            model.Name = name; model.Writes = 0; return Completion.RolledBack;
        }
        public void Dispose() { }
    }
}
