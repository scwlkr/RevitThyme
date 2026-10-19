using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RevitThyme.Core;

namespace RevitThyme.Mcp;

public sealed partial class RevitTools
{
    [McpServerTool(Name = "timberfold_inspect", ReadOnly = false, Destructive = false),
        Description("Inspect captured exterior-wall/planar-roof kit scope, source accounting and fabrication profile; retain private snapshot/diagnostic files. No model edits or cutting files. Physical inches in profile, Revit feet in extraction; incomplete geometry blocks preview. Null scope reports active phase/main-primary options.")]
    public Task<CallToolResult> TimberFoldInspect(string captureId, TimberFoldProfile? profile = null,
        TimberScope? scope = null, CancellationToken cancellationToken = default) =>
        Run(captureId, new(Guid.NewGuid(), OperationKind.InspectTimberFold, TimberFold: new(profile, scope)), cancellationToken);
}
