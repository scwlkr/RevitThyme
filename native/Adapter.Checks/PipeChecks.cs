using System.IO.Pipes;
using System.Text.Json;
using System.Buffers.Binary;
using RevitThyme.Native;
namespace RevitThyme.AdapterChecks;
internal static class PipeChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        var model = new FakeModel(); var session = new Session();
        string name = $"RevitThyme-{Environment.ProcessId}-{model.Target.SessionId}";
        using var server = new PipeServer(name, model.Target.ProcessId, model.Target.ProcessStartTicks, model.Target.SessionId, session, () => { });
        string credential = new('a', 64); server.SetCredential(credential); var running = server.Run();
        using var timeout = new CancellationTokenSource(75000);
        async Task<NamedPipeClientStream> Connect()
        {
            var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await client.ConnectAsync(timeout.Token); return client;
        }
        var hello = new Hello(Wire.Protocol, model.Target.ProcessId, model.Target.ProcessStartTicks, model.Target.SessionId, credential, Guid.NewGuid().ToString());
        foreach (var bad in new[] { hello with { Credential = new('b', 64) }, hello with { ProcessStartTicks = "0" }, hello with { SessionId = Guid.NewGuid().ToString() }, hello with { Protocol = 1 } })
        {
            using var client = await Connect(); await Frames.Write(client, bad, timeout.Token);
            bool closed = false;
            try { await Frames.Read<JsonElement>(client, timeout.Token); } catch (IOException) { closed = true; }
            check(closed && !session.HasQueued, "Actual Windows pipe rejects unauthorized/mismatched handshake before API enqueue");
        }
        using (var client = await Connect())
        {
            await Frames.Write(client, hello, timeout.Token);
            var ack = await Frames.Read<JsonElement>(client, timeout.Token);
            check(ack.GetProperty("session_id").GetString() == model.Target.SessionId, "Actual current-user local pipe session handshake");
            var oversized = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(oversized, 65537);
            await client.WriteAsync(oversized, timeout.Token);
            bool closed = false; try { await Frames.Read<JsonElement>(client, timeout.Token); } catch (IOException) { closed = true; }
            check(closed && !session.HasQueued, "Pipe rejects oversized header before body allocation/API");
        }
        using (var client = await Connect())
        {
            await Frames.Write(client, hello, timeout.Token); await Frames.Read<JsonElement>(client, timeout.Token);
            var arbitrary = System.Text.Encoding.UTF8.GetBytes("{\"protocol\":1,\"execute_code\":\"no\"}");
            var frame = new byte[arbitrary.Length + 4]; BinaryPrimitives.WriteInt32LittleEndian(frame, arbitrary.Length); arbitrary.CopyTo(frame, 4);
            await client.WriteAsync(frame, timeout.Token);
            bool closed = false; try { await Frames.Read<JsonElement>(client, timeout.Token); } catch (IOException) { closed = true; }
            check(closed && !session.HasQueued, "Closed framed DTO rejects arbitrary methods and missing fields");
        }
        using (var client = await Connect())
        {
            await Frames.Write(client, hello, timeout.Token); await Frames.Read<JsonElement>(client, timeout.Token);
            await Task.Delay(31_000, timeout.Token);
            bool alive = false;
            try
            {
                var capture = new Request(Wire.Protocol, Guid.NewGuid().ToString(), Operation.Capture, null, "", null, 0, "", false);
                await Frames.Write(client, capture, timeout.Token);
                alive = (await Frames.Read<Reply>(client, timeout.Token)).Status == Status.Queued;
            }
            catch (IOException) { }
            check(alive, "Authenticated Windows pipe survives idle beyond 30 seconds and can enqueue capture");
            await client.WriteAsync(new byte[] { 1 }, timeout.Token);
            bool closed = false;
            try { await Frames.Read<JsonElement>(client, timeout.Token); } catch (IOException) { closed = true; }
            check(closed, "Started but incomplete native header still times out and disconnects");
        }
        check(model.Trace.Count == 0, "Pipe reader never calls model API on background thread");
        server.Dispose(); await running;
    }
}
