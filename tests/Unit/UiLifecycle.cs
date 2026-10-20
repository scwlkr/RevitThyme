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
    }
    private sealed class UiModel : IDocumentLifecycleModel, IViewActivationModel
    {
        public Target Target = new(1, "start", "session", "", "", 1);
        public string Path = System.IO.Path.GetFullPath("ui.rvt"), Hash = new('a', 64);
        public bool Modified, Drift; public int CloseCalls;
        public Snapshot Capture() => new(Target, "2027", Target.DocumentId == "" ? "" : "Fixture", Target.DocumentId == "" ? "" : Path, false, Modified, Target.ViewId, "FloorPlan", 1, Target.DocumentId != "", null);
        public string ReadViewName() => Target.ViewId;
        public IChange BeginChange() => throw new InvalidOperationException("UI lifecycle must not create transactions.");
        public DocumentLifecycleResult ChangeDocument(OperationKind operation, DocumentLifecycleRequest options)
        {
            if (operation == OperationKind.CloseDocument)
            {
                CloseCalls++; Target = Target with { Revision = Target.Revision + 1 };
                return new(Drift ? "wrong" : options.DocumentId!, Path, "Fixture", false, false, Hash, true);
            }
            if (operation == OperationKind.ActivateDocument) Target = Target with { DocumentId = "doc", ViewId = "plan", Revision = 2 };
            return new("doc", Path, "Fixture", Target.DocumentId == "doc", false, Hash, true);
        }
        public void ActivateView(string id) { Target = Target with { ViewId = id, Revision = Target.Revision + 1 }; if (Drift) Modified = true; }
    }
}
