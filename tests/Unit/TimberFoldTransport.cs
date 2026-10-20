using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using RevitThyme.Core;

namespace RevitThyme.Unit;

internal static class TimberFoldTransport
{
    public static async Task Run(Action<string, bool> check)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var process = Process.GetCurrentProcess();
        string id = Guid.NewGuid().ToString("D"), name = $"RevitThyme-Foundation-{process.Id}-{id}";
        var binding = new SessionBinding(Protocol.Version, process.Id,
            process.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture), id, name, "2027");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var ordinary = Server(name);
        await using var status = Server(name + "-status");
        await using var cancel = Server(name + "-cancel");
        await using var outcome = Server(name + "-outcome");
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var normalServer = Respond(ordinary, true);
        var statusServer = Respond(status, false);
        var cancelServer = Respond(cancel, false);
        var outcomeServer = Respond(outcome, false);
        var client = new PipeClient(binding);
        var pendingNative = client.ExecuteAsync(new(Guid.NewGuid(), OperationKind.Inspect), deadline.Token);
        await held.Task.WaitAsync(deadline.Token);
        bool responsive = false;
        try
        {
            using var shortDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            var controls = await Task.WhenAll(
                client.ExecuteAsync(new(Guid.NewGuid(), OperationKind.GetTimberFoldRun, OperationId: Guid.NewGuid()), shortDeadline.Token),
                client.ExecuteAsync(new(Guid.NewGuid(), OperationKind.GetOperation, OperationId: Guid.NewGuid()), shortDeadline.Token),
                client.ExecuteAsync(new(Guid.NewGuid(), OperationKind.CancelTimberFoldRun, OperationId: Guid.NewGuid()), shortDeadline.Token));
            responsive = controls.All(r => r.Succeeded) && !pendingNative.IsCompleted;
        }
        catch (IOException) { }
        finally { release.TrySetResult(); }
        check("retained operation outcome, run status and cancellation bypass an occupied native-operation pipe", responsive);
        await pendingNative;
        deadline.Cancel();
        foreach (var task in new[] { normalServer, statusServer, cancelServer, outcomeServer })
            try { await task; } catch (OperationCanceledException) { }

        async Task Respond(NamedPipeServerStream pipe, bool hold)
        {
            await pipe.WaitForConnectionAsync(deadline.Token);
            var input = await Frames.ReadAsync<BridgeRequest>(pipe, deadline.Token);
            if (hold) { held.TrySetResult(); await release.Task.WaitAsync(deadline.Token); }
            await Frames.WriteAsync(pipe, new OperationResult(input.Request.Id, Outcome.Inspected, "observed", "Observed."), deadline.Token);
        }
        static NamedPipeServerStream Server(string pipeName) => new(pipeName, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
    }
}
