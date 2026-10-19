using RevitThyme.Core;

namespace RevitThyme.Unit;

internal static class Program
{
    private static int passed;
    private static int failed;
    public static async Task<int> Main()
    {
        await Operations(); await Boundaries.Run(Check); await ParameterInspectionCli.Run(Check); await ParameterResponseCompatibility.Run(Check);
        Utilities.Run(Check); await Dynamic.Run(Check); await Updates.Run(Check); await InstalledCli.Run(Check);
        TimberFoldOperations.Run(Check);
        TimberFoldInspection.Run(Check);
        TimberFoldFabrication.Run(Check);
        TimberFoldCollisions.Run(Check);
        TimberFoldStock.Run(Check);
        await TimberFoldOperations.Cli(Check);
        Console.WriteLine($"Foundation unit checks: {passed} passed, {failed} failed."); return failed == 0 ? 0 : 1;
    }
    internal static void Check(string name, bool condition)
    {
        if (condition) { passed++; Console.WriteLine("PASS " + name); }
        else { failed++; Console.Error.WriteLine("FAIL " + name); }
    }
    private static async Task Operations()
    {
        var executor = new OperationExecutor();
        var model = new FakeModel();
        OperationRequest Edit(string? name = "Renamed") => new(Guid.NewGuid(), OperationKind.RenameView, model.Target, model.Name, name);
        Check("inspection does not open a transaction", executor.Execute(model, new(Guid.NewGuid(), OperationKind.Inspect)).Outcome == Outcome.Inspected && model.Transactions == 0);
        Check("empty request ID rejected", executor.Execute(model, new(Guid.Empty, OperationKind.Inspect)).Code == "invalid_id");
        Check("inspection edit arguments rejected", executor.Execute(model, new(Guid.NewGuid(), OperationKind.Inspect, Name: "bad")).Code == "invalid_request");
        Check("unknown operation rejected", executor.Execute(model, new(Guid.NewGuid(), (OperationKind)999)).Code == "unknown_operation");
        Check("target required", executor.Execute(model, new(Guid.NewGuid(), OperationKind.RenameView, Name: "Renamed")).Code == "target_required");
        var stale = Edit() with { Target = model.Target with { Revision = 0 } };
        Check("stale revision rejected without transaction", executor.Execute(model, stale).Code == "stale_target" && model.Transactions == 0);
        foreach (var wrong in new[] { model.Target with { ProcessId = 999 }, model.Target with { ProcessStart = "other" },
            model.Target with { SessionId = "other" }, model.Target with { DocumentId = "other" }, model.Target with { ViewId = "other" } })
            Check("wrong target component rejected", executor.Execute(model, Edit() with { Target = wrong }).Code == "stale_target");
        Check("changed name rejected", executor.Execute(model, Edit() with { ExpectedName = "old" }).Code == "stale_target");
        foreach (string? name in new string?[] { null, "", "   ", " leading", "trailing ", new('x', 201) })
            Check("invalid name rejected", executor.Execute(model, Edit(name)).Code == "invalid_name");
        model.Writable = false;
        Check("read-only rejected", executor.Execute(model, Edit()).Code == "write_blocked" && model.Transactions == 0);
        model.Writable = true;
        Check("no-op verifies without transaction", executor.Execute(model, Edit(model.Name)).Outcome == Outcome.Unchanged && model.Transactions == 0);
        var applied = executor.Execute(model, Edit());
        Check("rename has before and verified after values", applied.Outcome == Outcome.Applied && applied.PreviousName == "Foundation Plan" && applied.Snapshot?.ViewName == "Renamed" && applied.ChangedIds is [42]);
        foreach (string fault in new[] { "set", "commit", "post-commit" })
        {
            model = new() { FailSet = fault == "set", FailCommit = fault == "commit", FailAfterCommit = fault == "post-commit" };
            Check("rollback restores name after " + fault, executor.Execute(model, Edit()).Outcome == Outcome.RolledBack && model.Name == "Foundation Plan");
        }
        model = new() { FailSet = true, FailRollback = true };
        Check("failed rollback is unconfirmed", executor.Execute(model, Edit()).Outcome == Outcome.Unconfirmed);
        model = new() { WrongReadback = true };
        Check("unverifiable rollback is unconfirmed", executor.Execute(model, Edit()).Outcome == Outcome.Unconfirmed);
        model = new() { FailBegin = true };
        Check("begin refusal opens no transaction", executor.Execute(model, Edit()).Code == "write_blocked" && model.Transactions == 0);
        model = new();
        var dispatcher = new OperationDispatcher(); var request = Edit();
        var task = dispatcher.ExecuteAsync(request);
        Check("queue does not touch model on caller thread", !task.IsCompleted && model.Transactions == 0);
        var repeat = dispatcher.ExecuteAsync(request); dispatcher.ExecuteNext(model);
        Check("duplicate edit shares a single outcome", task == repeat && (await task).Outcome == Outcome.Applied && model.Transactions == 1);
        Check("completed edit replay does not rerun", (await dispatcher.ExecuteAsync(request)).Outcome == Outcome.Applied && model.Transactions == 1);
        Check("conflicting request ID rejected", (await dispatcher.ExecuteAsync(request with { Name = "Other" })).Code == "id_conflict");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Check("pre-queue cancellation rejected", (await dispatcher.ExecuteAsync(Edit(), cancelled.Token)).Code == "cancelled");
        var pending = dispatcher.ExecuteAsync(new(Guid.NewGuid(), OperationKind.Inspect)); dispatcher.Stop();
        Check("shutdown rejects queued work", (await pending).Code == "host_stopped");
        Check("shutdown rejects new work", (await dispatcher.ExecuteAsync(Edit())).Code == "host_stopped");
        dispatcher = new() { Schedule = () => throw new InvalidOperationException() };
        Check("scheduling failure resolves request", (await dispatcher.ExecuteAsync(Edit())).Code == "schedule_failed");
    }
}
