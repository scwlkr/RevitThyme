using System.Diagnostics;
using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace RevitThyme.McpTests;

internal sealed partial class ElectricalLabLive
{
    private async Task<(CallToolResult Result, JsonElement Data)> RecoverSubmitted(string name, string request, JsonElement initial)
    {
        string id = OriginalGuid(request); var intent = OriginalIntent(id); var watch = Stopwatch.StartNew();
        string receipt = "submitted-recovery-" + id + "-" + Guid.NewGuid() + ".json"; var observations = new List<object>();
        void Persist() => Save(receipt, new { name, originalGuid = id, intent, originalResponse = initial, observations,
            boundary = "Only the original operation outcome is polled. No replacement capture, request, lifecycle operation or dialog handler." });
        Persist(); observer ??= await Client("uncertain submitted outcome recovery");
        while (watch.Elapsed < TimeSpan.FromMinutes(3))
        {
            var remaining = TimeSpan.FromMinutes(3) - watch.Elapsed;
            var callWatch = Stopwatch.StartNew(); var reply = await Program.Call(observer, "get_operation", new() { ["requestId"] = id }).WaitAsync(remaining < TimeSpan.FromSeconds(5) ? remaining : TimeSpan.FromSeconds(5));
            string code = reply.Data.GetProperty("code").GetString()!;
            observations.Add(new { requestedOriginalGuid = id, responseId = reply.Data.TryGetProperty("id", out var responseId) ? responseId.GetString() : null,
                elapsedMilliseconds = watch.Elapsed.TotalMilliseconds, callMilliseconds = callWatch.Elapsed.TotalMilliseconds, reply.Data, reply.Result.IsError }); Persist();
            Log(new { actor = "submitted outcome recovery", name = "get_operation", requestId = id, elapsedMilliseconds = watch.Elapsed.TotalMilliseconds, reply.Data, reply.Result.IsError });
            if (code != "pending")
            {
                if (!reply.Data.TryGetProperty("id", out var terminalId) || terminalId.GetString() != id || !reply.Data.TryGetProperty("outcome", out _))
                    throw new InvalidDataException("Original outcome recovery stopped on " + code + "; no retry or replacement submission.");
                // Return the complete official SDK reply, including any inline image blocks.
                return reply;
            }
            Console.WriteLine("Original " + name + " remains Pending: " + id + "; " + (int)watch.Elapsed.TotalSeconds + "s. Explicit native warning intervention may be required.");
            remaining = TimeSpan.FromMinutes(3) - watch.Elapsed;
            if (remaining <= TimeSpan.Zero) break;
            await Task.Delay(remaining < TimeSpan.FromSeconds(2) ? remaining : TimeSpan.FromSeconds(2));
        }
        throw new TimeoutException("Original operation remains Pending after bounded three-minute recovery: " + id + ". No replacement was submitted.");
    }
}
