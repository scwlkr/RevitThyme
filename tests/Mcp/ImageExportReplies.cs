using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using RevitThyme.Core;
using RevitThyme.Mcp;

namespace RevitThyme.McpTests;

internal static class ImageExportReplies
{
    public static async Task Run()
    {
        string folder = Path.Combine(Path.GetTempPath(), "RevitThyme-image-replies-" + Guid.NewGuid()); Directory.CreateDirectory(folder);
        try
        {
            using var process = Process.GetCurrentProcess(); string sessionId = Guid.NewGuid().ToString();
            var binding = new SessionBinding(Core.Protocol.Version, process.Id, process.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture), sessionId, $"RevitThyme-Foundation-{process.Id}-{sessionId}", "Fake");
            string session = Path.Combine(folder, "session.json"); File.WriteAllText(session, JsonSerializer.Serialize(binding, Core.Protocol.Json));
            string path = Path.Combine(folder, "image.png");
            byte[] png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aY1cAAAAASUVORK5CYII=");
            File.WriteAllBytes(path, png);
            var snapshot = new Snapshot(new(binding.ProcessId, binding.ProcessStart, binding.SessionId, "doc", "active", 1), "Fake", "Fixture", "fixture.rvt", false, false, "Active", "FloorPlan", 1, true, null);
            var reply = new OperationResult(Guid.NewGuid(), Outcome.Unchanged, "image_exported", "Completed", snapshot,
                ImageExport: new("sheet", 2, "Sheet", "DrawingSheet", path, Convert.ToHexString(SHA256.HashData(png)), png.Length, 1, 1));
            var connection = new RevitConnection(new(folder, session), _ => new ReplyClient(snapshot, reply)); var tools = new RevitTools(connection);
            var valid = await tools.GetOperation(reply.Id.ToString(), CancellationToken.None);
            Program.Check("image completion returns JSON structured metadata and inline PNG", valid.IsError != true && valid.StructuredContent?.GetProperty("code").GetString() == "image_exported"
                && valid.Content.OfType<TextContentBlock>().Count() == 1 && valid.Content.OfType<ImageContentBlock>().Single().DecodedData.Span.SequenceEqual(png));
            File.WriteAllBytes(path, new byte[24]);
            var changed = await tools.GetOperation(reply.Id.ToString(), CancellationToken.None);
            Program.Check("changed retained image is a structured MCP error without image bytes", changed.IsError == true && changed.StructuredContent?.GetProperty("code").GetString() == "image_artifact_unavailable"
                && !changed.Content.OfType<ImageContentBlock>().Any() && File.ReadAllBytes(path).Length == 24);
            File.Delete(path);
            var missing = await tools.GetOperation(reply.Id.ToString(), CancellationToken.None);
            Program.Check("missing retained image never triggers regeneration", missing.IsError == true && missing.StructuredContent?.GetProperty("code").GetString() == "image_artifact_unavailable" && !File.Exists(path));
            File.WriteAllBytes(path, png.Select((b, i) => i == 40 ? (byte)(b ^ 1) : b).ToArray());
            var tampered = await tools.GetOperation(reply.Id.ToString(), CancellationToken.None);
            Program.Check("same-size hash mismatch is refused before inline delivery", tampered.IsError == true && tampered.StructuredContent?.GetProperty("code").GetString() == "image_artifact_unavailable");
            string stalePath = Path.Combine(folder, "stale.json"); File.WriteAllText(stalePath, JsonSerializer.Serialize(binding with { ProcessStart = "0" }, Core.Protocol.Json));
            var unpinned = new RevitConnection(new(folder, null), _ => new ReplyClient(snapshot, reply)); var discovery = new RevitTools(unpinned);
            var stale = await discovery.ConnectSession(stalePath, CancellationToken.None);
            var disconnected = await discovery.GetContext(CancellationToken.None);
            Program.Check("stale session errors remain structured without connection fallback", stale.IsError == true && stale.StructuredContent?.GetProperty("code").GetString() == "connection_failed"
                && disconnected.StructuredContent?.GetProperty("code").GetString() == "not_connected");
            Program.Check("discovery skips stale bindings without losing valid sessions", unpinned.Sessions() is [{ Binding: var live }] && live == binding);
        }
        finally { Directory.Delete(folder, recursive: true); }
    }
    private sealed class ReplyClient(Snapshot snapshot, OperationResult reply) : IOperationClient
    {
        public Task<OperationResult> ExecuteAsync(OperationRequest request, CancellationToken cancellationToken = default) => Task.FromResult(request.Operation == OperationKind.Inspect
            ? new OperationResult(request.Id, Outcome.Inspected, "inspected", "Context", snapshot) : reply);
    }
}
