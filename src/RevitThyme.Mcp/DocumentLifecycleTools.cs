using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RevitThyme.Core;

namespace RevitThyme.Mcp;

public sealed partial class RevitTools
{
    [McpServerTool(Name = "open_document", ReadOnly = false, Destructive = false, Idempotent = true),
        Description("Open one explicit canonical fixed-local-drive Revit 2027 nonworkshared RVT in the background, including from Home/no active document. Requires current get_context capture, request GUID and expected file SHA-256. No activation, upgrade, save, close or model transaction. Returns native document ID/path and verified completion. On interruption recover the original requestId with get_operation; never retry with a new ID.")]
    public Task<CallToolResult> OpenDocument(string captureId, string requestId, string path, string expectedSha256, CancellationToken cancellationToken = default) =>
        Safe(() => Run(captureId, new(Id(requestId), OperationKind.OpenDocument, DocumentLifecycle: new(path, ExpectedSha256: expectedSha256)), cancellationToken));

    [McpServerTool(Name = "activate_document", ReadOnly = false, Destructive = false, Idempotent = true),
        Description("Activate an already-open project by exact native documentId and canonical path. Requires a fresh capture of the CURRENT session, including Home and request GUID. No implicit open, save, close or model transaction. Returns only after exact active document readback. Refresh context afterwards. Recover interrupted requests with get_operation.")]
    public Task<CallToolResult> ActivateDocument(string captureId, string requestId, string documentId, string path, CancellationToken cancellationToken = default) =>
        Safe(() => Run(captureId, new(Id(requestId), OperationKind.ActivateDocument, DocumentLifecycle: new(path, documentId)), cancellationToken));

    [McpServerTool(Name = "save_document", ReadOnly = false, Destructive = false, Idempotent = true),
        Description("Explicitly save ONLY the exact captured active nonworkshared local project: native documentId, canonical existing path and expected current on-disk SHA-256 must agree. No SaveAs, sync, close or model transaction. Success verifies native IsModified=false and returns the resulting saved hash. Saving is outside Undo; failures may be unconfirmed. Recover original requestId; never repeat an uncertain save under a new ID.")]
    public Task<CallToolResult> SaveDocument(string captureId, string requestId, string documentId, string path, string expectedSha256, CancellationToken cancellationToken = default) =>
        Safe(() => Run(captureId, new(Id(requestId), OperationKind.SaveDocument, DocumentLifecycle: new(path, documentId, expectedSha256)), cancellationToken));

    [McpServerTool(Name = "close_document", ReadOnly = false, Destructive = false, Idempotent = true),
        Description("Close exactly one clean BACKGROUND nonworkshared local project by native documentId, canonical path and expected disk hash. Requires fresh get_context capture, including at Home for a background document. Refuses dirty documents; no implicit save or discard. Active documents return close_blocked before side effects; explicitly activate another open document first. Recover original requestId through get_operation, without submitting the close again. No automatic fallback document close.")]
    public Task<CallToolResult> CloseDocument(string captureId, string requestId, string documentId, string path, string expectedSha256, CancellationToken cancellationToken = default) =>
        Safe(() => Run(captureId, new(Id(requestId), OperationKind.CloseDocument, DocumentLifecycle: new(path, documentId, expectedSha256)), cancellationToken));

    [McpServerTool(Name = "activate_view", ReadOnly = false, Destructive = false, Idempotent = true),
        Description("Switch to one explicit printable non-template native view/sheet UniqueId in the captured active nonworkshared document. Requires fresh capture and request GUID. No model transaction, save or fallback to another document; verifies exact active view and unchanged modified state. Refresh context afterwards. Recover original requestId after interruption.")]
    public Task<CallToolResult> ActivateView(string captureId, string requestId, string viewId, CancellationToken cancellationToken = default) =>
        Safe(() => Run(captureId, new(Id(requestId), OperationKind.ActivateView, ElementIds: [viewId]), cancellationToken));
}
