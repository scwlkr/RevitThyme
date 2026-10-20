using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RevitThyme.Core;

namespace RevitThyme.Mcp;

public sealed partial class RevitTools
{
    [McpServerTool(Name = "list_documents", ReadOnly = true, Idempotent = true),
        Description("Inspect visible open documents in the captured native session, including at Home. Supply request GUID for retained read recovery. Returns exact session-native IDs, title/path and active/modified/family/linked/workshared/read-only/saved-path flags; flags do not authorize lifecycle actions. Page with offset 0, limit 1–100; pass returned inventoryToken for every continuation. Background opens/closes and metadata changes invalidate the token. Fixture-bound sessions expose only their fixture. No file hash, activation, transaction or save. A replay/recovered result is the original point-in-time inventory; use a fresh request/capture for current state.")]
    public Task<CallToolResult> ListDocuments(string captureId, string requestId, int offset = 0, int limit = 50, string? inventoryToken = null, CancellationToken cancellationToken = default) =>
        Safe(() => Run(captureId, new(Id(requestId), OperationKind.ListDocuments, DocumentQuery: new(offset, limit, inventoryToken)), cancellationToken));

    [McpServerTool(Name = "open_document", ReadOnly = false, Destructive = false, Idempotent = true),
        Description("Open one explicit canonical fixed-local-drive Revit 2027 nonworkshared RVT in the background, including from Home/no active document. Requires current get_context capture, request GUID and expected file SHA-256. No activation, upgrade, save, close or model transaction. Returns native document ID/path and verified completion. On interruption recover the original requestId with get_operation; never retry with a new ID.")]
    public Task<CallToolResult> OpenDocument(string captureId, string requestId, string path, string expectedSha256, CancellationToken cancellationToken = default) =>
        Safe(() => Run(captureId, new(Id(requestId), OperationKind.OpenDocument, DocumentLifecycle: new(path, ExpectedSha256: expectedSha256)), cancellationToken));

    [McpServerTool(Name = "activate_document", ReadOnly = false, Destructive = false, Idempotent = true),
        Description("Activate an already-open project by exact native documentId and canonical path. Requires a fresh capture of the CURRENT session, including Home and request GUID. No implicit open, save, close or model transaction. Returns only after exact active document readback. Refresh context afterwards. Recover interrupted requests with get_operation.")]
    public Task<CallToolResult> ActivateDocument(string captureId, string requestId, string documentId, string path, CancellationToken cancellationToken = default) =>
        Safe(() => Run(captureId, new(Id(requestId), OperationKind.ActivateDocument, DocumentLifecycle: new(path, documentId)), cancellationToken));

    [McpServerTool(Name = "save_document", ReadOnly = false, Destructive = false, Idempotent = true),
        Description("Explicitly save ONLY the exact captured active nonworkshared local project: native documentId, canonical existing path and expected current on-disk SHA-256 must agree. A document opened from a read-only file is refused before saving, even if it can be edited in memory. No SaveAs, sync, close or model transaction. Success verifies native IsModified=false and returns the resulting saved hash. Saving is outside Undo; failures may be unconfirmed. Recover original requestId; never repeat an uncertain save under a new ID.")]
    public Task<CallToolResult> SaveDocument(string captureId, string requestId, string documentId, string path, string expectedSha256, CancellationToken cancellationToken = default) =>
        Safe(() => Run(captureId, new(Id(requestId), OperationKind.SaveDocument, DocumentLifecycle: new(path, documentId, expectedSha256)), cancellationToken));

    [McpServerTool(Name = "close_document", ReadOnly = false, Destructive = false, Idempotent = true),
        Description("Close one exact clean nonworkshared local project by documentId/path/expectedSha256. Background close preserves the active context, including Home. To close the captured active document, supply activateBeforeClose with a DIFFERENT already-open clean document's documentId/path/expectedSha256. Synchronously activates that exact document then directly closes the target; no posted native command, implicit open/save/discard or fallback. Last-active close to Home is unsupported. Partial activation/cancelled close is Unconfirmed; inspect and recover the original requestId without resubmission.")]
    public Task<CallToolResult> CloseDocument(string captureId, string requestId, string documentId, string path, string expectedSha256,
        CloseActivationTarget? activateBeforeClose = null, CancellationToken cancellationToken = default) =>
        Safe(() => Run(captureId, new(Id(requestId), OperationKind.CloseDocument, DocumentLifecycle: new(path, documentId, expectedSha256, activateBeforeClose)), cancellationToken));

    [McpServerTool(Name = "activate_view", ReadOnly = false, Destructive = false, Idempotent = true),
        Description("Switch to one explicit printable non-template native view/sheet UniqueId in the captured active nonworkshared document. Requires fresh capture and request GUID. No model transaction, save or fallback to another document; verifies exact active view and unchanged modified state. Refresh context afterwards. Recover original requestId after interruption.")]
    public Task<CallToolResult> ActivateView(string captureId, string requestId, string viewId, CancellationToken cancellationToken = default) =>
        Safe(() => Run(captureId, new(Id(requestId), OperationKind.ActivateView, ElementIds: [viewId]), cancellationToken));
}
