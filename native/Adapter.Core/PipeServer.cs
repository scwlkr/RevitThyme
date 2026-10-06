using System.IO.Pipes;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace RevitThyme.Native;
public sealed class PipeServer(string name, int processId, string startTicks, string sessionId, Session session, Action schedule) : IDisposable
{
    private readonly CancellationTokenSource lifetime = new();
    private string credential = "";
    private NamedPipeServerStream? active;
    private string activeConnection = "";
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task Ready => ready.Task;
    public void SetCredential(string value) { credential = value; if (activeConnection.Length > 0) session.Disconnect(activeConnection); active?.Dispose(); session.Invalidate(); }
    public Task Run() => Task.Run(async () =>
    {
        while (!lifetime.IsCancellationRequested)
        {
            string connection = "";
            try
            {
                using var pipe = LocalPipe.Create(name);
                active = pipe;
                ready.TrySetResult();
                await pipe.WaitForConnectionAsync(lifetime.Token);
                // OS-provided PID rejects remote clients (zero/not a local session) before reading credentials.
                if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle.DangerousGetHandle(), out uint pid) || pid == 0
                    || Process.GetProcessById((int)pid).SessionId != Process.GetCurrentProcess().SessionId) throw new InvalidDataException("Local client required.");
                using var helloTimeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); helloTimeout.CancelAfter(5000);
                var hello = await Frames.Read<Hello>(pipe, helloTimeout.Token);
                if (hello.Protocol != 1 || hello.ProcessId != processId || hello.ProcessStartTicks != startTicks || hello.SessionId != sessionId
                    || credential.Length != 64 || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(credential), Encoding.UTF8.GetBytes(hello.Credential))
                    || !Guid.TryParse(hello.ConnectionId, out _)) throw new InvalidDataException("Handshake rejected.");
                connection = hello.ConnectionId; activeConnection = connection; session.Connect(connection);
                await Frames.Write(pipe, new { protocol = 1, session_id = sessionId, process_id = processId, process_start_ticks = startTicks }, lifetime.Token);
                while (!lifetime.IsCancellationRequested)
                {
                    using var frameTimeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); frameTimeout.CancelAfter(30_000);
                    var r = await Frames.Read<Request>(pipe, frameTimeout.Token);
                    Reply reply;
                    try { reply = session.Submit(r, connection); }
                    catch { reply = Reply.Result(r, Status.Rejected, "invalid_request", "Malformed, stale or incompatible native request.", true); }
                    if (reply.Status == Status.Queued) schedule();
                    await Frames.Write(pipe, reply, frameTimeout.Token);
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or OperationCanceledException or System.Text.Json.JsonException or ArgumentException or ObjectDisposedException) { }
            finally { active = null; activeConnection = ""; if (connection.Length > 0) session.Disconnect(connection); }
        }
    });
    public void Dispose() { lifetime.Cancel(); active?.Dispose(); }
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientProcessId(IntPtr pipe, out uint processId);
}
