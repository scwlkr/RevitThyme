using RevitThyme.Core;

namespace RevitThyme.Unit;

internal static class UiLifecycle
{
    public static async Task Run(Action<string, bool> check)
    {
        var model = new UiModel(); var executor = new OperationExecutor();
        var open = new OperationRequest(Guid.NewGuid(), OperationKind.OpenDocument, model.Target, DocumentLifecycle: new(model.Path, ExpectedSha256: model.Hash));
        check("Home snapshot is an explicit valid no-document context", !model.Capture().HasDocument && !model.Capture().CanModify);
        check("background open works from captured Home", executor.Execute(model, open).Code == "document_opened" && !model.Capture().HasDocument);
        var active = open with { Operation = OperationKind.ActivateDocument, DocumentLifecycle = new(model.Path, "doc") };
        check("explicit activation works from Home", executor.Execute(model, active).Code == "document_activated" && model.Capture().HasDocument);
        var view = new OperationRequest(Guid.NewGuid(), OperationKind.ActivateView, model.Target, ElementIds: ["3d"]);
        check("view switch has exact native readback and no transaction", executor.Execute(model, view) is { Code: "view_activated", Snapshot.Target.ViewId: "3d" });
        check("view switch refuses stale document target", executor.Execute(model, view).Code == "stale_target");
        check("view switch rejects unrelated arguments", executor.Execute(model, view with { Target = model.Target, Name = "extra" }).Code == "invalid_request");
        model.Drift = true;
        check("view state drift remains unconfirmed", executor.Execute(model, view with { Target = model.Target }).Outcome == Outcome.Unconfirmed);
        model.Drift = false; model.Modified = false;
        model.Target = model.Target with { DocumentId = "", ViewId = "", Revision = 6 };
        var close = new OperationRequest(Guid.NewGuid(), OperationKind.CloseDocument, model.Target, DocumentLifecycle: new(model.Path, "background", model.Hash));
        await using var queue = new OperationDispatcher();
        var closing = queue.ExecuteAsync(close);
        check("background close retained before execution", !closing.IsCompleted && queue.HasPending && model.CloseCalls == 0);
        check("outcome recovery observes pending close without model callback", (await queue.ExecuteAsync(new(Guid.NewGuid(), OperationKind.GetOperation, OperationId: close.Id))).Code == "pending");
        queue.ExecuteNext(model); var result = await closing;
        check("background close at Home verifies no fallback activation", result is { Code: "document_closed", Outcome: Outcome.Completed, Snapshot.HasDocument: false });
        check("close replay and retained recovery do not close again", await queue.ExecuteAsync(close) == result && await queue.ExecuteAsync(new(Guid.NewGuid(), OperationKind.GetOperation, OperationId: close.Id)) == result && model.CloseCalls == 1);
        check("old context after close is stale", executor.Execute(model, close with { Id = Guid.NewGuid() }).Code == "stale_target");
        check("close requires exact native identity", executor.Execute(model, close with { Target = model.Target, DocumentLifecycle = new(model.Path, ExpectedSha256: model.Hash) }).Code == "invalid_lifecycle");
        check("close requires expected saved hash", executor.Execute(model, close with { Target = model.Target, DocumentLifecycle = new(model.Path, "background") }).Code == "invalid_lifecycle");
        model.Drift = true;
        check("close cannot complete on wrong document readback", executor.Execute(model, close with { Id = Guid.NewGuid(), Target = model.Target }).Outcome == Outcome.Unconfirmed);
        model = new(); await using var cancelled = new OperationDispatcher(); using var token = new CancellationTokenSource();
        var cancelledTask = cancelled.ExecuteAsync(close with { Id = Guid.NewGuid(), Target = model.Target }, token.Token); token.Cancel(); cancelled.ExecuteNext(model);
        check("pre-execution close cancellation has no native side effect", (await cancelledTask).Code == "cancelled" && model.CloseCalls == 0);
        await using var stopped = new OperationDispatcher(); var pending = stopped.ExecuteAsync(close with { Id = Guid.NewGuid(), Target = model.Target }); stopped.Stop();
        check("shutdown rejects close that has not executed", (await pending).Code == "host_stopped" && model.CloseCalls == 0);
        model = new() { Target = model.Target with { DocumentId = "doc", ViewId = "plan" } };
        var handoff = close with { Id = Guid.NewGuid(), Target = model.Target,
            DocumentLifecycle = new(model.Path, "doc", model.Hash, new("other", model.OtherPath, model.Hash)) };
        check("active close verifies the explicitly selected handoff document", executor.Execute(model, handoff) is
            { Code: "document_closed", Outcome: Outcome.Completed, Snapshot.Target.DocumentId: "other" });
        var next = handoff.DocumentLifecycle!.ActivateBeforeClose!;
        model = new() { Target = handoff.Target! };
        foreach (var invalid in new[] { next with { DocumentId = "doc" }, next with { Path = model.Path }, next with { ExpectedSha256 = "short" }, next with { Path = "relative.rvt" }, next with { DocumentId = "" } })
            check("invalid explicit handoff refuses before lifecycle execution", executor.Execute(model, handoff with { DocumentLifecycle = handoff.DocumentLifecycle with { ActivateBeforeClose = invalid } }).Code == "invalid_lifecycle" && model.CloseCalls == 0);
        check("handoff rejected on open and save", executor.Execute(model, open with { Target = model.Target, DocumentLifecycle = open.DocumentLifecycle! with { ActivateBeforeClose = next } }).Code == "invalid_lifecycle"
            && executor.Execute(model, handoff with { Operation = OperationKind.SaveDocument }).Code == "invalid_lifecycle" && model.CloseCalls == 0);
        check("background close cannot unexpectedly activate a document", executor.Execute(model, handoff with { DocumentLifecycle = handoff.DocumentLifecycle with { DocumentId = "background" } }).Code == "invalid_lifecycle" && model.CloseCalls == 0);
        await using var handoffQueue = new OperationDispatcher();
        var handoffPending = handoffQueue.ExecuteAsync(handoff); handoffQueue.ExecuteNext(model); var handedOff = await handoffPending;
        check("handoff close recovery does not repeat activation or close", await handoffQueue.ExecuteAsync(handoff) == handedOff
            && await handoffQueue.ExecuteAsync(new(Guid.NewGuid(), OperationKind.GetOperation, OperationId: handoff.Id)) == handedOff && model.CloseCalls == 1);
        check("changing handoff conflicts with retained original ID", (await handoffQueue.ExecuteAsync(handoff with { DocumentLifecycle = handoff.DocumentLifecycle with { ActivateBeforeClose = next with { DocumentId = "changed" } } })).Code == "id_conflict" && model.CloseCalls == 1);
        model = new() { Target = handoff.Target!, FailAfterActivation = true };
        var uncertain = executor.Execute(model, handoff);
        check("partial handoff is unconfirmed and leaves actual native context for inspection", uncertain.Outcome == Outcome.Unconfirmed && model.Capture().Target.DocumentId == "other");
        model = new() { Target = handoff.Target!, Drift = true };
        check("wrong close receipt cannot confirm a handoff", executor.Execute(model, handoff).Outcome == Outcome.Unconfirmed);
    }
    private sealed class UiModel : IDocumentLifecycleModel, IViewActivationModel
    {
        public Target Target = new(1, "start", "session", "", "", 1);
        public string Path = System.IO.Path.GetFullPath("ui.rvt"), OtherPath = System.IO.Path.GetFullPath("other.rvt"), Hash = new('a', 64);
        private bool handedOff;
        public bool Modified, Drift, FailAfterActivation; public int CloseCalls;
        public Snapshot Capture() => new(Target, "2027", Target.DocumentId == "" ? "" : "Fixture", Target.DocumentId == "" ? "" : handedOff ? OtherPath : Path, false, Modified, Target.ViewId, "FloorPlan", 1, Target.DocumentId != "", null);
        public string ReadViewName() => Target.ViewId;
        public IChange BeginChange() => throw new InvalidOperationException("UI lifecycle must not create transactions.");
        public DocumentLifecycleResult ChangeDocument(OperationKind operation, DocumentLifecycleRequest options)
        {
            if (operation == OperationKind.CloseDocument)
            {
                CloseCalls++; Target = Target with { Revision = Target.Revision + 1 };
                if (options.ActivateBeforeClose is { } next) { handedOff = true; Target = Target with { DocumentId = next.DocumentId, ViewId = "other-view" }; }
                if (FailAfterActivation) throw new OperationException("lifecycle_unconfirmed", "Close cancelled after activation.");
                return new(Drift ? "wrong" : options.DocumentId!, Path, "Fixture", false, false, Hash, true);
            }
            if (operation == OperationKind.ActivateDocument) Target = Target with { DocumentId = "doc", ViewId = "plan", Revision = 2 };
            return new("doc", Path, "Fixture", Target.DocumentId == "doc", false, Hash, true);
        }
        public void ActivateView(string id) { Target = Target with { ViewId = id, Revision = Target.Revision + 1 }; if (Drift) Modified = true; }
    }
}
