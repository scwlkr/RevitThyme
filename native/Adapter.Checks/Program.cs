using RevitThyme.Native;
using RevitThyme.AdapterChecks;
using System.Buffers.Binary;
using System.Text.Json;

if (args.Contains("--serve")) { await FixtureServer.Run(); return; }
int passed = 0;
void Check(bool value, string name) { if (!value) throw new Exception(name); passed++; Console.WriteLine("PASS " + name); }
var model = new FakeModel(); var facts = model.Capture();
Request Request(Operation operation = Operation.Apply, string? id = null) => new(Wire.Protocol, id ?? Guid.NewGuid().ToString(), operation, facts.Capture.Target,
    facts.Capture.SnapshotId, facts.Capture.Original with { Cut = facts.Capture.Original.Cut with { OffsetFeet = 5 } }, 0, "", true);
var result = Apply.Execute(model, facts, Request());
Check(result.Status == Status.AppliedVerified && result.ChangedIds.SequenceEqual(new[] { facts.Capture.Target.ViewId }), "Changed Apply returns independently expected range and exact changed view ID");
Check(model.Current.Cut.OffsetFeet == 5 && model.Transactions == 1, "Exactly one fixture transaction");
Check(model.Trace.IndexOf("commit") < model.Trace.IndexOf("post_commit_read") && model.Trace.IndexOf("post_commit_read") < model.Trace.IndexOf("assimilate"), "Post-commit readback before assimilation");
model = new(); facts = model.Capture();
var unchanged = Request() with { Range = facts.Capture.Original };
result = Apply.Execute(model, facts, unchanged);
Check(result.Status == Status.UnchangedVerified && result.ChangedIds.Length == 0 && model.Transactions == 0, "Exact high-ID/negative/untouched native values open no transaction");
foreach (var stage in new[] { "validate", "read", "begin", "group_start", "transaction_start", "set", "regenerate", "pre_commit_read", "commit", "post_commit_read", "assimilate", "pending", "group_status" })
{
    model = new(); facts = model.Capture(); model.Fail = stage;
    result = Apply.Execute(model, facts, Request());
    var expected = stage is "validate" or "read" ? Status.Rejected : stage == "begin" ? Status.OutcomeUnconfirmed : Status.RollbackConfirmed;
    Check(result.Status == expected && model.Current == model.Original && result.ChangedIds.Length == 0, "Failure truth and preserved original: " + stage);
}
foreach (var stage in new[] { "rollback", "rollback_status", "rollback_read" })
{
    model = new(); facts = model.Capture(); model.Fail = "post_commit_read+" + stage;
    Check(Apply.Execute(model, facts, Request()).Status == Status.OutcomeUnconfirmed, "Rollback failure never claims unchanged: " + stage);
}
var session = new Session(); const string connection = "fixture-connection"; session.Connect(connection);
model = new();
var capture = Request(Operation.Capture) with { Target = null, Range = null, SnapshotId = "", Confirmed = false };
session.Submit(capture, connection);
Check(model.Trace.Count == 0, "Background Submit never calls model API");
session.ExecuteOne(model);
var query = capture with { Operation = Operation.Outcome, RequestId = Guid.NewGuid().ToString(), OutcomeId = capture.RequestId };
facts = session.Submit(query, connection).Capture is {} captured ? new(captured, model.Original, [], DateTime.UtcNow) : throw new Exception("Capture missing");
var apply = Request();
session.Submit(apply, connection); session.ExecuteOne(model);
var repeated = session.Submit(apply, connection);
Check(repeated.Status == Status.AppliedVerified && model.Transactions == 1, "Lost ack then duplicate identical ID never reruns transaction");
Check(session.Submit(apply with { Range = apply.Range! with { Cut = apply.Range!.Cut with { OffsetFeet = 6 } } }, connection).Code == "request_id_conflict", "Changed payload same ID rejected");
query = apply with { Operation = Operation.Outcome, RequestId = Guid.NewGuid().ToString(), Range = null, OutcomeId = apply.RequestId };
Check(session.Submit(query, connection).NativeValues?.Cut.OffsetFeet == 5, "Original outcome query recovers verified native values");
foreach (var mismatch in new[] { facts.Capture.Target with { ProcessId = 1 }, facts.Capture.Target with { ProcessStartTicks = "1" }, facts.Capture.Target with { SessionId = "other" }, facts.Capture.Target with { DocumentId = "other" }, facts.Capture.Target with { ViewId = "other" }, facts.Capture.Target with { Revision = 2 } })
{
    var bad = Request() with { Target = mismatch };
    session.Submit(bad, connection); session.ExecuteOne(model);
    Check(session.Submit(bad, connection).Status == Status.Rejected && model.Transactions == 1, "Wrong target cannot write");
}
var cancel = Request(); session.Submit(cancel, connection);
query = cancel with { Operation = Operation.Cancel, RequestId = Guid.NewGuid().ToString(), OutcomeId = cancel.RequestId, Range = null };
Check(session.Submit(query, connection).Status == Status.Cancelled, "Queued cancellation acknowledged before execution");
session.ExecuteOne(model); Check(model.Transactions == 1, "Cancelled request opens no transaction");
var disconnected = Request(); session.Submit(disconnected, connection); session.Disconnect(connection); session.Connect(connection);
session.ExecuteOne(model); Check(session.Submit(disconnected, connection).Status == Status.Cancelled, "Disconnect removes queued work; reconnect cannot resurrect it");
foreach (var restriction in new[] { "template-controlled", "dependent", "primary-with-dependents", "family", "read-only", "modifiable", "closed-document" })
{
    session = new(); session.Connect(connection); model = new(); model.Restriction = restriction;
    session.Submit(capture, connection); session.ExecuteOne(model);
    Check(session.Submit(capture, connection).Status == Status.Rejected && model.Transactions == 0, "Unsupported state: " + restriction);
    session = new(); session.Connect(connection); model = new();
    session.Submit(capture, connection); session.ExecuteOne(model);
    var beforeRestriction = session.Submit(capture, connection).Capture!;
    facts = new(beforeRestriction, model.Original, [], DateTime.UtcNow); model.Restriction = restriction;
    var restricted = Request(); session.Submit(restricted, connection); session.ExecuteOne(model);
    Check(session.Submit(restricted, connection).Status == Status.Rejected && model.Transactions == 0, "Restriction changed after capture rejects Apply: " + restriction);
}
await PipeChecks.Run(Check);
session = new(); session.Connect(connection); model = new();
session.Submit(capture, connection); session.ExecuteOne(model);
var cap = session.Submit(capture, connection).Capture!;
facts = new(cap, model.Original, [], DateTime.UtcNow);
var executing = Request(); session.Submit(executing, connection);
model.DuringSet = () => {
    var cancelExecuting = executing with { Operation = Operation.Cancel, RequestId = Guid.NewGuid().ToString(), OutcomeId = executing.RequestId, Range = null };
    Check(session.Submit(cancelExecuting, connection).Status == Status.Executing, "Executing cancellation never claims cancelled or rollback");
};
session.ExecuteOne(model);
Check(session.Submit(executing, connection).Status == Status.AppliedVerified && model.Transactions == 1, "Executing request retains its final truthful outcome");
session = new(); session.Connect(connection); model = new();
session.Submit(capture, connection); session.ExecuteOne(model);
cap = session.Submit(capture, connection).Capture!; facts = new(cap, model.Original, [], DateTime.UtcNow);
var stale = Request(); session.Submit(stale, connection); session.Invalidate(); session.ExecuteOne(model);
Check(session.Submit(stale, connection).Status == Status.Rejected && model.Transactions == 0, "Document/geometry invalidation rejects queued stale snapshot before writes");
session = new(); session.Connect(connection);
for (int i = 0; i < 16; i++) session.Submit(capture with { RequestId = Guid.NewGuid().ToString() }, connection);
Check(session.Submit(capture with { RequestId = Guid.NewGuid().ToString() }, connection).Code == "queue_full", "Native queue is bounded and refuses excess work");
session = new(); session.Connect(connection); model = new() { CaptureAge = TimeSpan.FromSeconds(601) };
session.Submit(capture, connection); session.ExecuteOne(model);
cap = session.Submit(capture, connection).Capture!; facts = new(cap, model.Original, [], DateTime.UtcNow);
var expired = Request(); session.Submit(expired, connection); session.ExecuteOne(model);
Check(session.Submit(expired, connection).Status == Status.Rejected && model.Transactions == 0, "Native snapshot expiry cannot write");
session = new(); session.Connect(connection); model = new() { Fail = "capture" };
session.Submit(capture, connection); session.ExecuteOne(model);
Check(session.Submit(capture, connection).Status == Status.Rejected && model.Transactions == 0, "Capture API exception reports failure without mutation");
Console.WriteLine($"M2 Windows orchestration: {passed} passed; actual Revit=false.");
