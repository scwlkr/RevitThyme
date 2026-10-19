using RevitThyme.Core;

namespace RevitThyme.Unit;

internal static class ViewImages
{
    public static async Task Run(Action<string, bool> check)
    {
        var model = new ImageModel();
        var request = new OperationRequest(Guid.NewGuid(), OperationKind.ExportViewImage, model.Target, ImageExport: new("sheet-unique-id"));
        await using var queue = new OperationDispatcher();
        var task = queue.ExecuteAsync(request);
        check("image export queued without model access", model.Exports == 0 && !task.IsCompleted);
        check("image export pending recoverable without callback", (await queue.ExecuteAsync(new(Guid.NewGuid(), OperationKind.GetOperation, OperationId: request.Id))).Code == "pending");
        queue.ExecuteNext(model);
        var result = await task;
        check("image export completes with unchanged native snapshot and exact image identity", result is { Outcome: Outcome.Unchanged, Code: "image_exported", ImageExport.ViewId: "sheet-unique-id" } && result.Snapshot?.Target == model.Target && model.Exports == 1);
        check("image outcome recovery and replay do not re-export", await queue.ExecuteAsync(request) == result && await queue.ExecuteAsync(new(Guid.NewGuid(), OperationKind.GetOperation, OperationId: request.Id)) == result && model.Exports == 1);
        check("image request GUID conflict rejected", (await queue.ExecuteAsync(request with { ImageExport = new("other") })).Code == "id_conflict" && model.Exports == 1);
        check("image arguments refused by direct run controls before dispatch", (await queue.ExecuteAsync(request with { Id = Guid.NewGuid(), Operation = OperationKind.GetTimberFoldRun })).Code == "invalid_request" && model.Exports == 1);
        var executor = new OperationExecutor();
        foreach (var wrong in new[] { model.Target with { ProcessId = 0 }, model.Target with { ProcessStart = "stale" }, model.Target with { SessionId = "stale" },
            model.Target with { DocumentId = "other" }, model.Target with { ViewId = "other" }, model.Target with { Revision = 2 } })
            check("image wrong/stale target rejected before export", executor.Execute(model, request with { Target = wrong }).Code == "stale_target" && model.Exports == 1);
        foreach (var invalid in new[] { new ViewImageRequest(""), new("sheet", 127), new("sheet", 4097) })
            check("image bounded arguments rejected before export", executor.Execute(model, request with { ImageExport = invalid }).Code == "invalid_image_export" && model.Exports == 1);
        check("image arguments refused on other operations", executor.Execute(model, request with { Operation = OperationKind.Inspect }).Code == "invalid_request");
        check("image refuses unrelated arguments", executor.Execute(model, request with { Name = "bad" }).Code == "invalid_request");
        model.Fault = true;
        check("partial native export is unconfirmed", executor.Execute(model, request).Outcome == Outcome.Unconfirmed);
        model.Fault = false; model.ChangeDuringExport = true;
        check("native state drift prevents image success", executor.Execute(model, request).Outcome == Outcome.Unconfirmed);
        using var cancelled = new CancellationTokenSource();
        var cancelledRequest = request with { Id = Guid.NewGuid(), Target = model.Target };
        var cancelledTask = queue.ExecuteAsync(cancelledRequest, cancelled.Token); cancelled.Cancel(); queue.ExecuteNext(model);
        check("cancelled queued export creates no files", (await cancelledTask).Code == "cancelled" && model.Exports == 3);
    }
    private sealed class ImageModel : IViewImageModel
    {
        public Target Target = new(1, "start", "session", "document", "active", 1);
        public int Exports; public bool Fault, ChangeDuringExport;
        public Snapshot Capture() => new(Target, "2027", "Fixture", "fixture.rvt", false, false, "Active", "FloorPlan", 1, true, null);
        public string ReadViewName() => "Active";
        public IChange BeginChange() => throw new InvalidOperationException("Image export must not open a transaction.");
        public ViewImageResult ExportViewImage(Guid id, ViewImageRequest options)
        {
            Exports++;
            if (Fault) throw new OperationException("export_incomplete", "Partial native export.");
            if (ChangeDuringExport) Target = Target with { Revision = Target.Revision + 1 };
            return new(options.ViewId, 2, "Sheet", "DrawingSheet", "private.png", "hash", 100, 1600, 900);
        }
    }
}
