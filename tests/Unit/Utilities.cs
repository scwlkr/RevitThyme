using RevitThyme.Core;

namespace RevitThyme.Unit;

internal static class Utilities
{
    public static void Run(Action<string, bool> check)
    {
        var model = new UtilityModel(); var executor = new OperationExecutor();
        OperationRequest Range() => new(Guid.NewGuid(), OperationKind.SetViewRange, model.Target, ExpectedRange: model.Settings,
            Range: model.Settings with { Cut = new("Current", 5) });
        check("utility rejects stale document before read/write", executor.Execute(model, Range() with { Target = model.Target with { DocumentId = "wrong" } }).Code == "stale_target" && model.Transactions == 0);
        check("range inspection opens no transaction", executor.Execute(model, new(Guid.NewGuid(), OperationKind.GetViewRange, model.Target)).ViewRange?.Settings == model.Settings && model.Transactions == 0);
        check("range rejects missing planes", executor.Execute(model, Range() with { Range = null }).Code == "invalid_range");
        check("range rejects nonfinite offset", executor.Execute(model, Range() with { Range = model.Settings with { Cut = new("Current", double.NaN) } }).Code == "invalid_range");
        check("range requires exact prior native values", executor.Execute(model, Range() with { ExpectedRange = model.Settings with { Cut = new("Current", 3) } }).Code == "stale_range");
        model.Controlled = true;
        check("view template range blocks write", executor.Execute(model, Range()).Code == "write_blocked" && model.Transactions == 0); model.Controlled = false;
        check("range no-op creates no undo item", executor.Execute(model, Range() with { Range = model.Settings }).Outcome == Outcome.Unchanged && model.Transactions == 0);
        check("range applies through shared transaction", executor.Execute(model, Range()).Outcome == Outcome.Applied && model.Settings.Cut.OffsetFeet == 5 && model.Transactions == 1);
        foreach (string fault in new[] { "set", "commit", "readback", "assimilation" })
        {
            model = new() { Fault = fault }; var original = model.Settings;
            check("range rollback after " + fault, executor.Execute(model, Range()).Outcome == Outcome.RolledBack && model.Settings == original);
        }
        model = new() { Fault = "set", RollbackFails = true };
        check("utility uncertain rollback is explicit", executor.Execute(model, Range()).Outcome == Outcome.Unconfirmed);
        model = new();
        OperationRequest Room() => new(Guid.NewGuid(), OperationKind.CreateRoomDimensions, model.Target, ElementIds: ["room"], RoomOptions: new(), RoomPreview: model.Preview);
        check("room preview leaves native model unchanged", executor.Execute(model, new(Guid.NewGuid(), OperationKind.PreviewRoomDimensions, model.Target, ElementIds: ["room"])).Outcome == Outcome.Inspected && model.Transactions == 0);
        check("room edit requires reviewed preview", executor.Execute(model, Room() with { RoomPreview = null }).Code == "stale_room");
        check("forged dimension references rejected", executor.Execute(model, Room() with { RoomPreview = model.Preview with { Fingerprint = "forged" } }).Code == "stale_room");
        check("invalid room inset rejected", executor.Execute(model, Room() with { RoomOptions = new(0) }).Code == "invalid_options");
        model.Existing = [1]; check("partial pair refuses duplication", executor.Execute(model, Room()).Code == "partial_dimensions" && model.Transactions == 0);
        model.Existing = [1, 2]; check("existing pair creates no transaction", executor.Execute(model, Room()).Outcome == Outcome.Unchanged && model.Transactions == 0);
        model = new() { Fault = "second-dimension" };
        check("second dimension failure rolls back first", executor.Execute(model, Room()).Outcome == Outcome.RolledBack && model.Created.Count == 0);
        model = new();
        var roomResult = executor.Execute(model, Room());
        check("room pair verifies both ids before assimilation", roomResult.Outcome == Outcome.Applied && roomResult.ChangedIds?.Length == 2 && model.Transactions == 1);
        check("utility arguments rejected on other operations", executor.Execute(model, new(Guid.NewGuid(), OperationKind.Inspect, RoomOptions: new())).Code == "invalid_request");
        check("architectural entry supports signed eighth inches", Math.Abs(LengthText.Parse("-5' 0 1/8\"") + 5 + 1.0 / 96) < 1e-9);
        check("decimal feet are preserved", LengthText.Parse("4.123456789") == 4.123456789);
        check("metric units round trip", Math.Abs(LengthText.Parse("1524", "mm") - 5) < 1e-9 && LengthText.Format(5, "m") == "1.524");
        check("display carries rounded inches into feet", LengthText.Format(1 - 1.0 / 1000) == "1' 0\"");
        foreach (string bad in new[] { "", "5' 1/0\"", "NaN", "junk" })
        {
            bool rejected = false; try { LengthText.Parse(bad); } catch (FormatException) { rejected = true; }
            check("invalid length rejected: " + bad, rejected);
        }
    }
    private sealed class UtilityModel : IUtilityModel
    {
        public Target Target { get; } = new(1, "start", "session", "document", "view", 1);
        public ViewRangeSettings Settings { get; set; } = new(new("Current", 8), new("Current", 4), new("Current", 0), new("Current", -1));
        public string Fault { get; init; } = "";
        public bool RollbackFails { get; init; }
        public bool Controlled { get; set; }
        public int Transactions { get; set; }
        public long[] Existing { get; set; } = [];
        public List<long> Created { get; } = [];
        private bool changed;
        public RoomDimensionPreview Preview => new("room", "Room", 10, 12, new(), [], Existing, "fingerprint");
        public Snapshot Capture() => new(Target, "2027", "fixture", "fixture.rvt", false, changed, "plan", "FloorPlan", 9, true, null);
        public string ReadViewName() => "plan";
        public IChange BeginChange() => throw new NotSupportedException();
        public ViewRangeInfo ReadViewRange() => new(Fault == "readback" && changed ? Settings with { Cut = new("Current", 1) } : Settings, [], 0, !Controlled, "Template controls range.");
        public RoomDimensionPreview PlanRoomDimensions(string id, RoomDimensionOptions options) => Preview;
        public IUtilityChange BeginUtilityChange(string label) => new Change(this);
        public bool VerifyDimensions(long[] ids, RoomDimensionPreview preview) => ids.Length == 2 && Created.Count == 2;
        public bool ElementsAbsent(long[] ids) => ids.All(id => !Created.Contains(id));
        private sealed class Change(UtilityModel model) : IUtilityChange
        {
            private readonly ViewRangeSettings original = model.Settings;
            public Completion StartGroup() => Completion.Started;
            public Completion StartTransaction() { model.Transactions++; return Completion.Started; }
            public void RenameView(string name) => throw new NotSupportedException();
            public void SetViewRange(ViewRangeSettings s) { model.Settings = s; model.changed = true; if (model.Fault == "set") throw new InvalidOperationException(); }
            public void CreateRoomDimensions(RoomDimensionPreview p, List<long> ids)
            {
                model.Created.Add(1); ids.Add(1);
                if (model.Fault == "second-dimension") throw new InvalidOperationException();
                model.Created.Add(2); ids.Add(2);
            }
            public void Regenerate() { }
            public Completion Commit() => model.Fault == "commit" ? Completion.Other : Completion.Committed;
            public Completion Assimilate() => model.Fault == "assimilation" ? Completion.Other : Completion.Committed;
            public Completion RollBack()
            {
                if (model.RollbackFails) return Completion.Other;
                model.Settings = original; model.Created.Clear(); model.changed = false; return Completion.RolledBack;
            }
            public void Dispose() { }
        }
    }
}
