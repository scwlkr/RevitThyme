using System.IO;
using System.IO.Pipes;
using RevitThyme.Core;

namespace RevitThyme.Addin;

internal sealed class PipeServer(SessionBinding binding, OperationDispatcher operations, OperationKind? control = null) : IDisposable
{
    private readonly CancellationTokenSource stop = new();
    private NamedPipeServerStream? current;
    public void Start()
    {
        current = LocalPipe.Create(binding.PipeFor(control ?? OperationKind.Inspect));
        // Startup runs on Revit's UI synchronization context. Pipe continuations
        // must remain independent of a blocked native callback.
        _ = Task.Run(RunAsync);
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
                else if (control is { } accepted && input.Request.Operation != accepted)
                    result = OperationResult.Reject(input.Request, "invalid_request", "This control channel accepts only " + accepted + ".");
                else result = await operations.ExecuteAsync(input.Request, deadline.Token).WaitAsync(deadline.Token);
                await Frames.WriteAsync(pipe, result, deadline.Token, ParameterResponseJson.For(input.IncludeParameterIdentity));
            }
            catch (Exception error) when (error is IOException or OperationCanceledException or System.Text.Json.JsonException or InvalidDataException)
            { /* Disconnects never replay requests; the dispatcher retains their outcome. */ }
            finally { pipe.Dispose(); }
            if (!stop.IsCancellationRequested)
            {
                try { current = LocalPipe.Create(binding.PipeFor(control ?? OperationKind.Inspect)); }
                catch (IOException) { if (control is null) operations.RejectPending("bridge_stopped", "Local bridge stopped."); return; }
            }
        }
    }
    public void Dispose() { stop.Cancel(); current?.Dispose(); }
}
