using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RevitThyme.Core;

namespace RevitThyme.Mcp;

[McpServerToolType]
public sealed partial class RevitTools(RevitConnection connection)
{
    [McpServerTool(Name = "list_sessions", ReadOnly = true), Description("Discover live process-bound Revit sessions. Does not select a process.")]
    public CallToolResult ListSessions() => Reply(connection.Sessions());

    [McpServerTool(Name = "connect_session", ReadOnly = true), Description("Explicitly bind to an absolute session file from list_sessions. Captures reset on connection changes; pinned startup cannot switch.")]
    public Task<CallToolResult> ConnectSession(string sessionFile, CancellationToken cancellationToken) => Safe(async () => Reply(await connection.Connect(sessionFile, cancellationToken)));

    [McpServerTool(Name = "get_context", ReadOnly = true), Description("Inspect the native session at Home or the active document/view and get a captureId. Home returns hasDocument=false and empty document/view IDs. Use a fresh capture after every edit, view change or document change.")]
    public Task<CallToolResult> GetContext(CancellationToken cancellationToken) => Safe(async () =>
    {
        var capture = await connection.Context(cancellationToken); return Reply(capture, !capture.Result.Succeeded);
    });

    [McpServerTool(Name = "list_operations", ReadOnly = true), Description("Discover typed operation semantics, limits, units and recovery rules, including trusted C# execution.")]
    public CallToolResult ListOperations() => Reply(new
    {
        operations = Enum.GetNames<OperationKind>(), protocolVersion = Protocol.Version,
        dynamicExecution = "run_query/run_modify accept a C# method body with doc, uiapp, cancellationToken; return JSON data. Host REVITTHYME_TRUSTED_CODE=1 and trusted=true required. Full process trust, no sandbox. Query always rolls back current-document changes. Modify uses one Undo. Cooperative budgets cannot interrupt blocked/infinite code. Never retry with a new requestId after timeout; poll get_operation.",
        limits = new { queryPage = 100, inspectElements = 5, parametersPerPage = 20, parameterBatch = 20, selection = 100,
            sourceBytes = DynamicExecutor.MaxSourceBytes, resultBytes = DynamicExecutor.MaxValueBytes, changedIds = DynamicExecutor.MaxChangedIds },
        parameterUnits = "Double values use Revit internal units (length feet, angles radians). dataType is the native Forge type ID. ElementId parameters are inspection-only.",
        parameterIdentity = "id is the exact document-local edit target. isShared gates sharedGuid; builtInParameter is the native enum name or null. hasValue is native assigned-value state, not a nonempty or cleanup judgment. Compare same-name fields by identity; never edit by name or GUID alone.",
        utilityUnits = "Range offsets, room inset/clearance and dimensions use feet. Range levels: Current, Above, Below, Unlimited, or native Level UniqueId. Native Revit validates floor/ceiling plane order.",
        roomDimensions = "Preview one rectangular room on the active floor plan's level/phase, with four basic native wall finish faces. Send the exact returned preview and its options to create. Tags, existing dimensions and native references are rechecked. No linked/curved/separator/island boundaries.",
        imageExport = "export_view_image exports exactly one explicit view/sheet UniqueId at pixelSize 128–4096, to a host-owned private PNG. No model transaction, save or view activation. Completion requires native return, decoded PNG/hash and unchanged native snapshot. JSON metadata and verified inline PNG are returned, including on get_operation recovery. Same request ID/arguments replays without re-export; different arguments conflict. Outcomes last until native session shutdown. Files are not model content and are not undone.",
        documentLifecycle = "open_document opens a canonical local Revit 2027 nonworkshared RVT in the background using its expected saved SHA-256. activate_document selects an already-open exact documentId/path. save_document saves only the captured active document with matching ID/path and expected disk hash; it verifies IsModified=false and returns the new hash. Accepts a current session capture at Home for open/activate/background close. close_document accepts only an exact clean background document; active close is blocked. activate_view switches to one native view/sheet UniqueId within the captured document. No SaveAs, sync, upgrade, implicit save/discard or model transaction. Completed means verified native lifecycle state, outside model Undo. Recover original requestId through get_operation after interruption; no replacement IDs. Outcomes last until native shutdown; native calls/modal dialogs are not forcibly interruptible.",
        writes = "Use get_context and native UniqueId/parameter Id. set_parameters requires exact Expected values. Supply a unique requestId per new write. Retry only the same ID/arguments; get_operation retrieves retained outcomes. Unconfirmed edits require polling and native inspection.",
        reversibility = "Rename and parameter batches create one Revit Undo item each. Selection is UI state and restored on failure. No implicit save/sync."
    });

    [McpServerTool(Name = "list_views", ReadOnly = true), Description("Page native views with names, types, unique IDs and template flags; validates captured revision.")]
    public Task<CallToolResult> ListViews(string captureId, int offset = 0, int limit = 50, CancellationToken cancellationToken = default) =>
        Run(captureId, new(Guid.NewGuid(), OperationKind.ListViews, Query: new(Offset: offset, Limit: limit)), cancellationToken);

    [McpServerTool(Name = "query_elements", ReadOnly = true), Description("Page document elements by optional native negative category ID and name substring. Returns UniqueId for inspection. Stable pages require unchanged capture.")]
    public Task<CallToolResult> QueryElements(string captureId, string? categoryId = null, string? nameContains = null,
        bool includeTypes = false, int offset = 0, int limit = 50, CancellationToken cancellationToken = default) =>
        Run(captureId, new(Guid.NewGuid(), OperationKind.QueryElements, Query: new(categoryId, nameContains, includeTypes, offset, limit)), cancellationToken);

    [McpServerTool(Name = "inspect_elements", ReadOnly = true), Description("Inspect 1–5 native UniqueIds. Page up to 20 parameters per element with exact local Id, typed raw value, dataType, read-only/truncation flags, isShared, nullable sharedGuid/builtInParameter and native hasValue (assigned, not necessarily nonempty). Follow nextParameterOffset until null.")]
    public Task<CallToolResult> InspectElements(string captureId, string[] elementIds, int parameterOffset = 0, CancellationToken cancellationToken = default) =>
        Run(captureId, new(Guid.NewGuid(), OperationKind.InspectElements, ElementIds: elementIds, ParameterOffset: parameterOffset), cancellationToken);

    [McpServerTool(Name = "get_selection", ReadOnly = true), Description("Read native selected element UniqueIds (up to 100).")]
    public Task<CallToolResult> GetSelection(string captureId, CancellationToken cancellationToken) =>
        Run(captureId, new(Guid.NewGuid(), OperationKind.GetSelection), cancellationToken);

    [McpServerTool(Name = "set_selection", ReadOnly = false, Destructive = false, Idempotent = true), Description("Set 0–100 selected UniqueIds in the captured document, with verified readback. Changes UI selection, no model transaction.")]
    public Task<CallToolResult> SetSelection(string captureId, string requestId, string[] elementIds, CancellationToken cancellationToken) =>
        Safe(() => Run(captureId, new(Id(requestId), OperationKind.SetSelection, ElementIds: elementIds), cancellationToken));

    [McpServerTool(Name = "rename_view", ReadOnly = false, Destructive = false, Idempotent = true), Description("Rename the captured active view through the same Foundation operation as WPF/CLI. Native readback, rollback, one Undo item. Refresh context afterwards.")]
    public Task<CallToolResult> RenameView(string captureId, string requestId, string name, CancellationToken cancellationToken) =>
        Safe(() => Run(captureId, new(Id(requestId), OperationKind.RenameView, Name: name), cancellationToken));

    [McpServerTool(Name = "set_parameters", ReadOnly = false, Destructive = false, Idempotent = true), Description("Atomically edit 1–20 parameters by UniqueId and numeric parameter Id. Each edit requires Expected and Value objects: Storage String/Integer/Double with Text/Integer/Number. Use native internal units. Verifies all values before and after; rolls back the whole batch on failure.")]
    public Task<CallToolResult> SetParameters(string captureId, string requestId, ParameterEdit[] edits, CancellationToken cancellationToken) =>
        Safe(() => Run(captureId, new(Id(requestId), OperationKind.SetParameters, Edits: edits), cancellationToken));

    [McpServerTool(Name = "get_view_range", ReadOnly = true), Description("Read all four native plan range planes, level choices and template edit control. Offsets are feet. Requires a fresh captured active floor/ceiling/structural plan.")]
    public Task<CallToolResult> GetViewRange(string captureId, CancellationToken cancellationToken) =>
        Run(captureId, new(Guid.NewGuid(), OperationKind.GetViewRange), cancellationToken);

    [McpServerTool(Name = "set_view_range", ReadOnly = false, Destructive = false, Idempotent = true), Description("Edit all four native range planes. Supply exact expectedRange from get_view_range and the desired range: top/cut/bottom/depth with level and offsetFeet. Revit validates levels/order, verifies readback, rolls back failures; one Undo item. Refresh afterwards.")]
    public Task<CallToolResult> SetViewRange(string captureId, string requestId, ViewRangeSettings expectedRange, ViewRangeSettings range, CancellationToken cancellationToken) =>
        Safe(() => Run(captureId, new(Id(requestId), OperationKind.SetViewRange, ExpectedRange: expectedRange, Range: range), cancellationToken));

    [McpServerTool(Name = "preview_room_dimensions", ReadOnly = true), Description("Plan width/depth dimensions for one rectangular native room. No model changes. Returns exact face references, lines, lengths, options and existing IDs. Requires an active same-level/phase floor plan; reserves tag space. Use the returned preview for create.")]
    public Task<CallToolResult> PreviewRoomDimensions(string captureId, string roomId, double insetFeet = 1.5, double tagClearanceFeet = 1, CancellationToken cancellationToken = default) =>
        Run(captureId, new(Guid.NewGuid(), OperationKind.PreviewRoomDimensions, ElementIds: [roomId], RoomOptions: new(insetFeet, tagClearanceFeet)), cancellationToken);

    [McpServerTool(Name = "create_room_dimensions", ReadOnly = false, Destructive = false, Idempotent = true), Description("Create the two associative native dimensions from an exact preview_room_dimensions result. Recomputes native boundaries/tags/references, refuses stale previews and partial pairs, avoids duplicates, verifies dimension values/references; one Undo item. Supply a unique requestId.")]
    public Task<CallToolResult> CreateRoomDimensions(string captureId, string requestId, RoomDimensionPreview preview, CancellationToken cancellationToken) =>
        Safe(() => Run(captureId, new(Id(requestId), OperationKind.CreateRoomDimensions, ElementIds: [preview.RoomId], RoomOptions: preview.Options, RoomPreview: preview), cancellationToken));

    [McpServerTool(Name = "get_operation", ReadOnly = true), Description("Recover a retained native result by write requestId. Returns pending/not_found or the original result and original request ID. Never submits the edit again.")]
    public Task<CallToolResult> GetOperation(string requestId, CancellationToken cancellationToken) => Safe(async () =>
    { var result = await connection.Outcome(Id(requestId), cancellationToken); return Reply(result, !result.Succeeded); });

    private Task<CallToolResult> Run(string captureId, OperationRequest request, CancellationToken token) => Safe(async () =>
    { var result = await connection.Execute(captureId, request, token); return Reply(result, !result.Succeeded); });
    private static Guid Id(string text) => Guid.TryParse(text, out var id) && id != Guid.Empty ? id : throw new OperationException("invalid_id", "requestId must be a non-empty GUID.");
    private static async Task<CallToolResult> Safe(Func<Task<CallToolResult>> action)
    {
        try { return await action(); }
        catch (Exception error) when (error is OperationException or IOException or InvalidDataException or ArgumentException or InvalidOperationException or JsonException or System.ComponentModel.Win32Exception or OperationCanceledException)
        { return Reply(new { code = error is OperationException op ? op.Code : error is OperationCanceledException ? "cancelled" : "connection_failed", message = error.Message }, true); }
    }
    private static CallToolResult Reply(object value, bool error = false)
    {
        var json = JsonSerializer.SerializeToElement(value, Protocol.Json);
        var result = new CallToolResult { IsError = error, Content = [new TextContentBlock { Text = json.GetRawText() }], StructuredContent = json };
        if (!error && value is OperationResult { Succeeded: true, ImageExport: { } image })
        {
            try
            {
                using var file = new FileStream(image.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (file.Length != image.Bytes || file.Length is < 24 or > 8388608) throw new InvalidDataException("Image length changed or exceeds 8 MiB.");
                var bytes = new byte[(int)file.Length]; file.ReadExactly(bytes);
                if (!Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).Equals(image.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Image hash changed.");
                result.Content.Add(ImageContentBlock.FromBytes(bytes, "image/png"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                return Reply(new { code = "image_artifact_unavailable", message = "Retained export metadata exists, but its PNG is missing, changed or unreadable. No export was repeated.", operation = value }, true);
            }
        }
        return result;
    }
}
