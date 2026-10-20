using RevitThyme.Core;

namespace RevitThyme.Unit;

internal static class DocumentLifecycle
{
    public static async Task Run(Action<string, bool> check)
    {
        var model = new LifecycleModel(); var executor = new OperationExecutor();
        var request = new OperationRequest(Guid.NewGuid(), OperationKind.OpenDocument, model.Target,
            DocumentLifecycle: new(model.OtherPath, ExpectedSha256: new string('a', 64)));
        await using var queue = new OperationDispatcher();
        var pending = queue.ExecuteAsync(request);
        check("lifecycle waits for native queue", !pending.IsCompleted && model.Calls == 0);
        check("lifecycle pending retained without new action", (await queue.ExecuteAsync(new(Guid.NewGuid(), OperationKind.GetOperation, OperationId: request.Id))).Code == "pending");
        queue.ExecuteNext(model); var opened = await pending;
        check("background open verifies native completion without transaction", opened is { Outcome: Outcome.Completed, Code: "document_opened", Succeeded: true, DocumentLifecycle.DocumentId: "other" } && model.Calls == 1);
        check("lifecycle recovery and identical replay execute once", await queue.ExecuteAsync(request) == opened && await queue.ExecuteAsync(new(Guid.NewGuid(), OperationKind.GetOperation, OperationId: request.Id)) == opened && model.Calls == 1);
        check("lifecycle conflicting ID does not execute", (await queue.ExecuteAsync(request with { DocumentLifecycle = request.DocumentLifecycle! with { ExpectedSha256 = new string('b', 64) } })).Code == "id_conflict" && model.Calls == 1);
        foreach (var target in new[] { model.Target with { ProcessId = 2 }, model.Target with { ProcessStart = "stale" }, model.Target with { SessionId = "stale" },
            model.Target with { DocumentId = "wrong" }, model.Target with { ViewId = "wrong" }, model.Target with { Revision = 2 } })
            check("lifecycle wrong/stale identity refused before native action", executor.Execute(model, request with { Target = target }).Code == "stale_target" && model.Calls == 1);
        foreach (var options in new[] { new DocumentLifecycleRequest("relative.rvt", ExpectedSha256: new string('a', 64)), new(model.OtherPath),
            new(model.OtherPath, "unexpected", new string('a', 64)), new(model.OtherPath, ExpectedSha256: "short") })
            check("lifecycle invalid file arguments refused", executor.Execute(model, request with { DocumentLifecycle = options }).Code == "invalid_lifecycle" && model.Calls == 1);
        check("lifecycle refuses extra arguments", executor.Execute(model, request with { Name = "unexpected" }).Code == "invalid_request");
        check("lifecycle arguments refused by fast run control", (await queue.ExecuteAsync(request with { Id = Guid.NewGuid(), Operation = OperationKind.GetTimberFoldRun })).Code == "invalid_request");
        var save = request with { Id = Guid.NewGuid(), Operation = OperationKind.SaveDocument, DocumentLifecycle = new(model.OtherPath, "other", new string('a', 64)) };
        check("save refuses inactive document before native access", executor.Execute(model, save).Code == "wrong_document" && model.Calls == 1);
        var activate = request with { Id = Guid.NewGuid(), Operation = OperationKind.ActivateDocument, DocumentLifecycle = new(model.OtherPath, "other") };
        check("activate verifies exact native active document", executor.Execute(model, activate) is { Outcome: Outcome.Completed, Snapshot.Target.DocumentId: "other" });
        model.Modified = true;
        check("save verifies native clean state and resulting hash", executor.Execute(model, save with { Target = model.Target }) is { Outcome: Outcome.Completed, Snapshot.IsModified: false, DocumentLifecycle.IsModified: false });
        model.Fail = true;
        check("native partial lifecycle failure is unconfirmed", executor.Execute(model, save with { Target = model.Target }).Outcome == Outcome.Unconfirmed);
        model.Fail = false; model.BadReadback = true;
        check("wrong native document prevents completed receipt", executor.Execute(model, save with { Target = model.Target }).Outcome == Outcome.Unconfirmed);
        using var cancel = new CancellationTokenSource(); int calls = model.Calls;
        var cancelled = queue.ExecuteAsync(save with { Id = Guid.NewGuid(), Target = model.Target }, cancel.Token); cancel.Cancel(); queue.ExecuteNext(model);
        check("queued lifecycle cancellation has no native side effect", (await cancelled).Code == "cancelled" && model.Calls == calls);
    }
    private sealed class LifecycleModel : IDocumentLifecycleModel
    {
        public Target Target = new(1, "start", "session", "initial", "view", 1);
        public string OtherPath = Path.GetFullPath("other.rvt");
        private string path = Path.GetFullPath("initial.rvt");
        public int Calls; public bool Modified, Fail, BadReadback;
        public Snapshot Capture() => new(Target, "2027", "Fixture", path, false, Modified, "View", "FloorPlan", 1, true, null);
        public string ReadViewName() => "View";
        public IChange BeginChange() => throw new InvalidOperationException("Lifecycle must not create a model transaction.");
        public DocumentLifecycleResult ChangeDocument(OperationKind operation, DocumentLifecycleRequest options)
        {
            Calls++;
            if (Fail) throw new OperationException("lifecycle_unconfirmed", "Native call may have completed.");
            if (operation == OperationKind.ActivateDocument) { Target = Target with { DocumentId = "other", Revision = 3 }; path = OtherPath; }
            if (operation == OperationKind.SaveDocument) Modified = false;
            return new(BadReadback ? "wrong" : "other", options.Path, "Other", operation != OperationKind.OpenDocument, Modified, new string('a', 64), true);
        }
    }
}
