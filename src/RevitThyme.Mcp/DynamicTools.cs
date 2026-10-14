using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RevitThyme.Core;

namespace RevitThyme.Mcp;

public sealed partial class RevitTools
{
    [McpServerTool(Name = "run_query", ReadOnly = false, Destructive = true, Idempotent = true),
        Description("Execute trusted C# method body on Revit's API thread. Variables: doc, uiapp, cancellationToken. Return JSON data up to 8 KiB. Current-document changes always roll back. Full process permissions, NO sandbox or hard timeout; use disposable models. Requires host opt-in and trusted=true. Poll requestId after timeouts; refresh context after execution.")]
    public Task<CallToolResult> RunQuery(string captureId, string requestId, string source, bool trusted = false, int budgetMilliseconds = 10000, CancellationToken cancellationToken = default) =>
        Safe(() => Run(captureId, new(Id(requestId), OperationKind.RunQuery, Code: new(source, trusted, budgetMilliseconds)), cancellationToken));

    [McpServerTool(Name = "run_modify", ReadOnly = false, Destructive = true, Idempotent = true),
        Description("Execute trusted C# method body in a native transaction and one Undo group. Variables: doc, uiapp, cancellationToken. Host owns transaction; do not start/commit your own. Return JSON data; native added/modified/deleted IDs recorded separately. Errors/oversized results roll back. Full process permissions, NO sandbox; use disposable models. Requires host opt-in and trusted=true. Poll requestId on timeout.")]
    public Task<CallToolResult> RunModify(string captureId, string requestId, string source, bool trusted = false, int budgetMilliseconds = 10000, CancellationToken cancellationToken = default) =>
        Safe(() => Run(captureId, new(Id(requestId), OperationKind.RunModify, Code: new(source, trusted, budgetMilliseconds)), cancellationToken));
}
