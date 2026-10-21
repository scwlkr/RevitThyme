using System.Text.Json;
using ModelContextProtocol.Client;
using RevitThyme.Core;

namespace RevitThyme.McpTests;

internal static class ResultCheckAgentLive
{
    public static async Task Run(string folder)
    {
        var fixture = new ResultCheckFixture(folder, "agent-preservation");
        try
        {
            await using (var before = await fixture.Client()) await fixture.Context(before);
            await SavedToolAgentLive.Run(folder, requireResultChecks: true);
            await using (var after = await fixture.Client()) await fixture.Preservation(after);
            fixture.Finish(true);
        }
        catch (Exception error) { fixture.Finish(false, error); throw; }
    }
    internal static async Task CheckSensitivity(McpClient client, SavedToolArtifact artifact, string marker, string originalName,
        Action<string, bool> check, Action<string, object> write)
    {
        string name = "Agent Check Sensitivity " + Guid.NewGuid().ToString("N")[..8];
        var context = await Program.Call(client, "get_context");
        var changed = await Program.Call(client, "run_modify", new()
        {
            ["captureId"] = context.Data.GetProperty("captureId").GetString(), ["requestId"] = Guid.NewGuid().ToString(),
            ["source"] = "doc.ActiveView.Name = " + JsonSerializer.Serialize(name) + "; return true;", ["trusted"] = true
        });
        check("sensitivity probe edits only the disposable native view", changed.Result.IsError != true && changed.Data.GetProperty("outcome").GetString() == "Applied");
        bool restored = false;
        try
        {
            context = await Program.Call(client, "get_context");
            var observed = await Program.Call(client, "run_tool", new()
            {
                ["captureId"] = context.Data.GetProperty("captureId").GetString(), ["requestId"] = Guid.NewGuid().ToString(),
                ["toolId"] = artifact.ToolId, ["digest"] = artifact.Digest, ["inputs"] = new { marker }, ["trusted"] = true
            });
            write("generated-check-sensitivity.json", new { changed.Data, observed = observed.Data, observed.Result.IsError });
            check("Agent-generated checks fail when actual native view differs from declared expectation", observed.Result.IsError == true
                && observed.Data.GetProperty("outcome").GetString() == "Inspected"
                && observed.Data.GetProperty("execution").GetProperty("value").GetProperty("viewName").GetString() == name
                && observed.Data.GetProperty("verification").GetProperty("status").GetString() == "failed");
        }
        finally
        {
            context = await Program.Call(client, "get_context");
            var undo = await Program.Call(client, "run_query", new()
            {
                ["captureId"] = context.Data.GetProperty("captureId").GetString(), ["requestId"] = Guid.NewGuid().ToString(), ["trusted"] = true,
                ["source"] = "var undo = RevitCommandId.LookupPostableCommandId(PostableCommand.Undo); if (!uiapp.CanPostCommand(undo)) throw new InvalidOperationException(\"Undo unavailable\"); uiapp.PostCommand(undo); return true;"
            });
            check("sensitivity probe posts one native Undo", undo.Result.IsError != true);
            var deadline = DateTime.UtcNow.AddSeconds(20);
            do
            {
                await Task.Delay(300); context = await Program.Call(client, "get_context");
                restored = context.Result.IsError != true && context.Data.GetProperty("result").GetProperty("snapshot").GetProperty("viewName").GetString() == originalName;
            } while (!restored && DateTime.UtcNow < deadline);
            check("one native Undo restores the sensitivity probe", restored);
        }
    }
}
