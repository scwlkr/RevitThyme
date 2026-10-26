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
        foreach (var invalid in new[]
        {
            Range() with { Query = new() }, Range() with { Edits = [] },
            Range() with { Name = "unexpected" }, Range() with { ExpectedName = "unexpected" },
            Range() with { ElementIds = ["room"] }, Range() with { RoomOptions = new() },
            Range() with { RoomPreview = model.Preview }, Range() with { ParameterOffset = 1 },
            new OperationRequest(Guid.NewGuid(), OperationKind.GetViewRange, model.Target, Range: model.Settings)
        })
            check("utility argument rejection precedes native reads: " + invalid.Operation,
                executor.Execute(model, invalid).Code == "invalid_request" && model.Reads == 0 && model.Transactions == 0);
        check("stale target precedes malformed range validation", executor.Execute(model,
            Range() with { Target = model.Target with { Revision = 0 }, Range = null }).Code == "stale_target" && model.Reads == 0);
        check("range inspection opens no transaction", executor.Execute(model, new(Guid.NewGuid(), OperationKind.GetViewRange, model.Target)).ViewRange?.Settings == model.Settings && model.Transactions == 0);
        check("range rejects missing planes", executor.Execute(model, Range() with { Range = null }).Code == "invalid_range");
        check("range rejects nonfinite offset", executor.Execute(model, Range() with { Range = model.Settings with { Cut = new("Current", double.NaN) } }).Code == "invalid_range");
        check("range requires exact prior native values", executor.Execute(model, Range() with { ExpectedRange = model.Settings with { Cut = new("Current", 3) } }).Code == "stale_range");
        model.Controlled = true;
        check("view template range blocks write", executor.Execute(model, Range()).Code == "write_blocked" && model.Transactions == 0); model.Controlled = false;
        check("range no-op creates no undo item", executor.Execute(model, Range() with { Range = model.Settings }).Outcome == Outcome.Unchanged && model.Transactions == 0);
        check("range applies through shared transaction", executor.Execute(model, Range()).Outcome == Outcome.Applied && model.Settings.Cut.OffsetFeet == 5 && model.Transactions == 1);
        model = new();
        var boundaryRange = model.Settings with { Top = new(new string('L', 200), 1000000) };
        check("range accepts its exact documented level and offset limits", executor.Execute(model,
            Range() with { Range = boundaryRange }).Outcome == Outcome.Applied && model.Settings == boundaryRange);
        foreach (string fault in new[] { "set", "commit", "readback", "assimilation" })
        {
            model = new() { Fault = fault }; var original = model.Settings;
            check("range rollback after " + fault, executor.Execute(model, Range()).Outcome == Outcome.RolledBack && model.Settings == original);
        }
        model = new() { Fault = "set", RollbackFails = true };
        check("utility uncertain rollback is explicit", executor.Execute(model, Range()).Outcome == Outcome.Unconfirmed);
        model = new() { Fault = "begin" };
        check("native transaction refusal does not claim a rollback", executor.Execute(model, Range()).Code == "write_blocked" && model.Transactions == 0);
        model = new() { Fault = "operation" };
        var rejectedRange = model.Settings;
        var nativeRejection = executor.Execute(model, Range());
        check("verified rollback retains the native operation error", nativeRejection is
            { Outcome: Outcome.RolledBack, Code: "native_range_rejected", Message: "Native range refused. Original state verified." }
            && model.Settings == rejectedRange && model.Transactions == 1);
        model = new();
        OperationRequest Room() => new(Guid.NewGuid(), OperationKind.CreateRoomDimensions, model.Target, ElementIds: ["room"], RoomOptions: new(), RoomPreview: model.Preview);
        foreach (var invalid in new[] { Room() with { ExpectedRange = model.Settings }, Room() with { Range = model.Settings } })
            check("room rejects range edit values before planning", executor.Execute(model, invalid).Code == "invalid_request" && model.Reads == 0);
        string maximumId = new('R', 200);
        check("room preview passes through an exact maximum-length native id", executor.Execute(model,
            new(Guid.NewGuid(), OperationKind.PreviewRoomDimensions, model.Target, ElementIds: [maximumId])).Outcome == Outcome.Inspected
            && model.PlannedRoomId == maximumId && model.Transactions == 0);
        foreach (double boundary in new[] { .25, 20 })
        {
            var options = new RoomDimensionOptions(boundary, boundary);
            check("room preview accepts exact inset and tag-clearance limits", executor.Execute(model,
                new(Guid.NewGuid(), OperationKind.PreviewRoomDimensions, model.Target, ElementIds: ["room"], RoomOptions: options)).Outcome == Outcome.Inspected
                && model.PlannedOptions == options && model.Transactions == 0);
        }
        check("room preview leaves native model unchanged", executor.Execute(model, new(Guid.NewGuid(), OperationKind.PreviewRoomDimensions, model.Target, ElementIds: ["room"])).Outcome == Outcome.Inspected && model.Transactions == 0);
        check("room edit requires reviewed preview", executor.Execute(model, Room() with { RoomPreview = null }).Code == "stale_room");
        check("forged dimension references rejected", executor.Execute(model, Room() with { RoomPreview = model.Preview with { Fingerprint = "forged" } }).Code == "stale_room");
        check("invalid room inset rejected", executor.Execute(model, Room() with { RoomOptions = new(0) }).Code == "invalid_options");
        foreach (double clearance in new[] { double.NaN, .249, 20.001 })
            check("invalid room tag clearance is rejected: " + clearance, executor.Execute(model,
                Room() with { RoomOptions = new(TagClearanceFeet: clearance) }).Code == "invalid_options");
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
        public int Reads { get; private set; }
        public string? PlannedRoomId { get; private set; }
        public RoomDimensionOptions? PlannedOptions { get; private set; }
        public int Transactions { get; set; }
        public long[] Existing { get; set; } = [];
        public List<long> Created { get; } = [];
        private bool changed;
        public RoomDimensionPreview Preview => new("room", "Room", 10, 12, new(), [], Existing, "fingerprint");
        public Snapshot Capture() => new(Target, "2027", "fixture", "fixture.rvt", false, changed, "plan", "FloorPlan", 9, true, null);
        public string ReadViewName() => "plan";
        public IChange BeginChange() => throw new NotSupportedException();
        public ViewRangeInfo ReadViewRange() { Reads++; return new(Fault == "readback" && changed ? Settings with { Cut = new("Current", 1) } : Settings, [], 0, !Controlled, "Template controls range."); }
        public RoomDimensionPreview PlanRoomDimensions(string id, RoomDimensionOptions options) { Reads++; PlannedRoomId = id; PlannedOptions = options; return Preview; }
        public IUtilityChange BeginUtilityChange(string label) => Fault == "begin" ? throw new InvalidOperationException() : new Change(this);
        public bool VerifyDimensions(long[] ids, RoomDimensionPreview preview) => ids.Length == 2 && Created.Count == 2;
        public bool ElementsAbsent(long[] ids) => ids.All(id => !Created.Contains(id));
        private sealed class Change(UtilityModel model) : IUtilityChange
        {
            private readonly ViewRangeSettings original = model.Settings;
            public Completion StartGroup() => Completion.Started;
            public Completion StartTransaction() { model.Transactions++; return Completion.Started; }
            public void RenameView(string name) => throw new NotSupportedException();
            public void SetViewRange(ViewRangeSettings s)
            {
                model.Settings = s; model.changed = true;
                if (model.Fault == "set") throw new InvalidOperationException();
                if (model.Fault == "operation") throw new OperationException("native_range_rejected", "Native range refused.");
            }
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
