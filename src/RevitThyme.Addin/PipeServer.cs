using System.IO;
using System.IO.Pipes;
using RevitThyme.Core;

namespace RevitThyme.Addin;

internal sealed class PipeServer(SessionBinding binding, OperationDispatcher operations) : IDisposable
{
    private readonly CancellationTokenSource stop = new();
    private NamedPipeServerStream? current;
    public void Start()
    {
        current = LocalPipe.Create(binding.PipeName); _ = RunAsync();
    }
    private async Task RunAsync()
    {
        while (!stop.IsCancellationRequested)
        {
            var pipe = current!;
            try
            {
                await pipe.WaitForConnectionAsync(stop.Token);
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                deadline.CancelAfter(TimeSpan.FromSeconds(30));
                var input = await Frames.ReadAsync<BridgeRequest>(pipe, deadline.Token);
                OperationResult result;
                if (input.Request is null) throw new InvalidDataException("Request required.");
                if (input.ProtocolVersion != Protocol.Version || input.SessionId != binding.SessionId)
                    result = OperationResult.Reject(input.Request, "wrong_session", "Bridge session or protocol mismatch.");
                else result = await operations.ExecuteAsync(input.Request).WaitAsync(deadline.Token);
                await Frames.WriteAsync(pipe, result, deadline.Token);
            }
            catch (Exception error) when (error is IOException or OperationCanceledException or System.Text.Json.JsonException or InvalidDataException)
            { /* Disconnects never replay requests; the dispatcher retains their outcome. */ }
            finally { pipe.Dispose(); }
            if (!stop.IsCancellationRequested)
            {
                try { current = LocalPipe.Create(binding.PipeName); }
                catch (IOException) { operations.RejectPending("bridge_stopped", "Local bridge stopped."); return; }
            }
        }
    }
    public void Dispose() { stop.Cancel(); current?.Dispose(); }
}
