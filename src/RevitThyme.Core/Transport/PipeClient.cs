using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace RevitThyme.Core;

public sealed class PipeClient(SessionBinding binding) : IOperationClient
{
    public async Task<OperationResult> ExecuteAsync(OperationRequest request, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("The Revit bridge runs on Windows.");
        binding.AssertProcess();
        var envelope = new BridgeRequest(Protocol.Version, binding.SessionId, request, IncludeParameterIdentity: true);
        if (JsonSerializer.SerializeToUtf8Bytes(envelope, Protocol.Json).Length > Protocol.MaxBytes)
            return OperationResult.Reject(request, "request_too_large", "Request exceeds the 64 KiB bridge limit; nothing was submitted.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        await using var pipe = new NamedPipeClientStream(".", binding.PipeFor(request.Operation), PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        bool sent = false;
        try
        {
            await pipe.ConnectAsync(5000, deadline.Token);
            if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out uint pid) || pid != binding.ProcessId)
                throw new InvalidDataException("Connected pipe does not belong to the selected Revit process.");
            binding.AssertProcess();
            // Even a partial write may reach the host. Never retry an uncertain write automatically.
            sent = true;
            await Frames.WriteAsync(pipe, envelope, deadline.Token);
            var result = await Frames.ReadAsync<OperationResult>(pipe, deadline.Token);
            if (result.Id != request.Id && !(request.Operation == OperationKind.GetOperation && result.Id == request.OperationId))
                throw new InvalidDataException("Response request ID mismatch.");
            return result;
        }
        catch (Exception error) when (error is IOException or OperationCanceledException or TimeoutException)
        {
            if (sent && request.IsWrite)
                return new(request.Id, Outcome.Unconfirmed, "transport_lost",
                    "Connection ended after submission. Inspect Revit before making another edit.");
            throw new IOException("Selected Revit session is unavailable or busy. Check its dialogs and session file.", error);
        }
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint serverProcessId);
}
