using RevitThyme.Core;

namespace RevitThyme.Unit;

internal static class DocumentDiscovery
{
    public static async Task Run(Action<string, bool> check)
    {
        var model = new DiscoveryModel(); var before = model.Capture();
        var request = new OperationRequest(Guid.NewGuid(), OperationKind.ListDocuments, before.Target, DocumentQuery: new(Limit: 1));
        var result = new OperationExecutor().Execute(model, request);
        check("Home discovery returns stable native identities without activating a document", result is { Outcome: Outcome.Inspected, Documents.Total: 2, Documents.NextOffset: 1 }
            && result.Documents.Items.Single().DocumentId == "a" && result.Documents.InventoryToken.Length == 64 && model.Capture() == before);
        var continuation = request with { Id = Guid.NewGuid(), DocumentQuery = new(1, 1, result.Documents!.InventoryToken) };
        var next = new OperationExecutor().Execute(model, continuation);
        check("continuation completes the unchanged sorted inventory", next.Documents is { Total: 2, NextOffset: null } && next.Documents.Items.Single().DocumentId == "b");
        model.Items = [.. model.Items, new("c", "C", "C.rvt", false, false, false, false, false, false)];
        check("background document open invalidates continuation even without a revision event", new OperationExecutor().Execute(model, continuation).Code == "stale_inventory" && model.Capture() == before);
        model = new(); var executor = new OperationExecutor();
        foreach (var target in new[] { model.Target with { ProcessId = 9 }, model.Target with { ProcessStart = "other" }, model.Target with { SessionId = "other" },
            model.Target with { DocumentId = "foreign" }, model.Target with { ViewId = "foreign" }, model.Target with { Revision = 9 } })
            check("document discovery refuses wrong or stale captured identity", executor.Execute(model, request with { Target = target }).Code == "stale_target");
        check("document discovery requires explicit session capture", executor.Execute(model, request with { Target = null }).Code == "target_required");
        foreach (var query in new DocumentQuery[] { new(-1), new(4097), new(Limit: 0), new(Limit: 101), new(1), new(InventoryToken: "bad") })
            check("document discovery refuses invalid paging inputs", executor.Execute(model, request with { DocumentQuery = query }).Code == "invalid_query");
        check("document discovery refuses unexpected action payloads", executor.Execute(model, request with { Name = "unexpected" }).Code == "invalid_request");
        result = executor.Execute(model, request);
        Array.Reverse(model.Items);
        check("enumeration order does not invalidate a stable page", executor.Execute(model, continuation).Documents?.InventoryToken == result.Documents!.InventoryToken);
        foreach (var changed in new[] { model.Items[0] with { Title = "renamed" }, model.Items[0] with { Path = "other.rvt" }, model.Items[0] with { IsModified = true },
            model.Items[0] with { IsActive = true }, model.Items[0] with { IsReadOnly = true }, model.Items[0] with { IsFamilyDocument = true }, model.Items[0] with { IsLinked = true }, model.Items[0] with { IsWorkshared = true } })
        {
            var original = model.Items[0]; model.Items[0] = changed;
            check("returned metadata change invalidates a continuation", executor.Execute(model, continuation).Code == "stale_inventory"); model.Items[0] = original;
        }
        model.Items = [model.Items[0]];
        check("document close invalidates continuation without hiding missing items", executor.Execute(model, continuation).Code == "stale_inventory");
        model = new(); model.Target = model.Target with { SessionId = "new-session" };
        check("inventory token cannot continue in another native session", executor.Execute(model, continuation with { Target = model.Target }).Code == "stale_inventory");
        model = new(); model.Items = [new("unsaved", "Family", "", false, true, true, false, false, false)];
        var unsaved = executor.Execute(model, request).Documents!.Items.Single();
        check("discovery describes unsaved family state without lifecycle authorization", !unsaved.HasSavedPath && unsaved.IsFamilyDocument && unsaved.IsModified);
        model.Items = [];
        check("Home with no open documents returns an explicit empty inventory", executor.Execute(model, request).Documents is { Total: 0, Items.Length: 0, NextOffset: null });
        model.Items = Enumerable.Range(0, 10).Select(i => new OpenDocumentInfo(i.ToString(), new string('t', 4000), new string('p', 1000), false, false, false, false, false, false)).ToArray();
        var bounded = executor.Execute(model, request with { DocumentQuery = new(Limit: 100) });
        check("document pages shrink without truncating native identity or path", bounded.Documents is { Total: 10, NextOffset: > 0 and < 10 }
            && bounded.Documents.Items.All(d => d.Path.Length == 1000 && d.Title.Length == 4000) && System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(bounded, Protocol.Json).Length <= Protocol.MaxBytes / 2);
        model.Items = [model.Items[0] with { Title = new string('x', 20000) }];
        check("oversized document metadata fails without partial success", executor.Execute(model, request).Code == "inventory_too_large");
        model.Items = Enumerable.Range(0, 4097).Select(i => new OpenDocumentInfo(i.ToString(), "", "", false, false, false, false, false, false)).ToArray();
        check("too many documents fail explicitly instead of omitting identities", executor.Execute(model, request).Code == "inventory_too_large");
        model = new() { ChangeAfterRead = true };
        check("native context changes during enumeration refuse the page", executor.Execute(model, request).Code == "stale_target");
        model = new(); await using var queue = new OperationDispatcher();
        var pending = queue.ExecuteAsync(request);
        check("read discovery queues through the shared native operation service", !pending.IsCompleted);
        queue.ExecuteNext(model); var originalResult = await pending;
        model.Items = [];
        check("retained read recovery returns original point-in-time inventory", await queue.ExecuteAsync(new(Guid.NewGuid(), OperationKind.GetOperation, OperationId: request.Id)) == originalResult
            && await queue.ExecuteAsync(request) == originalResult && originalResult.Documents!.Total == 2);
        check("document query payload is refused by callback-free recovery", (await queue.ExecuteAsync(new(Guid.NewGuid(), OperationKind.GetOperation, OperationId: request.Id, DocumentQuery: new()))).Code == "invalid_request");
    }
    private sealed class DiscoveryModel : IDocumentDiscoveryModel
    {
        public Target Target = new(1, "start", "session", "", "", 0);
        public OpenDocumentInfo[] Items = [new("b", "B", "B.rvt", false, false, false, false, false, false), new("a", "A", "A.rvt", false, false, false, false, false, false)];
        public bool ChangeAfterRead;
        public Snapshot Capture() => new(Target, "2027", "", "", false, false, "", "None", 0, false, "Home");
        public IEnumerable<OpenDocumentInfo> ReadDocuments()
        {
            foreach (var item in Items) yield return item;
            if (ChangeAfterRead) Target = Target with { Revision = Target.Revision + 1 };
        }
        public string ReadViewName() => throw new NotSupportedException();
        public IChange BeginChange() => throw new InvalidOperationException("Document discovery must not start a transaction.");
    }
}
