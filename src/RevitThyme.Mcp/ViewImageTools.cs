using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RevitThyme.Core;

namespace RevitThyme.Mcp;

public sealed partial class RevitTools
{
    [McpServerTool(Name = "export_view_image", ReadOnly = false, Destructive = false, Idempotent = true),
        Description("Export exactly one native view/sheet UniqueId to a private PNG. Requires a fresh capture and request GUID. No activation, model transaction or save. Returns only after native export, PNG decode, hash and state verification. pixelSize 128–4096 (default 1600), longest edge. Output path is host-owned. On timeout/disconnect poll get_operation with the original requestId; never replace an uncertain ID. Outcomes last until Revit session shutdown; identical replay returns original metadata without exporting again.")]
    public Task<CallToolResult> ExportViewImage(string captureId, string requestId, string viewId, int pixelSize = 1600, CancellationToken cancellationToken = default) =>
        Safe(() => Run(captureId, new(Id(requestId), OperationKind.ExportViewImage, ImageExport: new(viewId, pixelSize)), cancellationToken));
}
