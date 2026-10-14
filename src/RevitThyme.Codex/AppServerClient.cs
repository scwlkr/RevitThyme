using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text;

namespace RevitThyme.Codex;

public sealed record ServerEvent(string Method, JsonElement Parameters);
public sealed record ServerRequest(JsonElement Id, string Method, JsonElement Parameters);

// One owned stdio process. The read pump never waits for UI approval or another RPC.
public sealed class AppServerClient : IAsyncDisposable
{
    private readonly TextReader reader;
    private readonly TextWriter writer;
    private readonly Process? process;
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> pending = new();
    private readonly SemaphoreSlim writes = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private readonly Task pump;
    private readonly Task? errors;
    private long sequence;
    private bool disposed;
    private int disposing;
    public event Action<ServerEvent>? Notification;
    public event Action<ServerRequest>? Request;
    public event Action<string>? Disconnected;

    public AppServerClient(TextReader reader, TextWriter writer, Process? process = null)
    {
        this.reader = reader; this.writer = writer; this.process = process;
        pump = Task.Run(Read);
        // Drain stderr without logging credentials, login URLs or model data.
        if (process is not null) errors = Task.Run(async () => { while (await process.StandardError.ReadLineAsync(lifetime.Token) is not null) { } });
    }
    public static AppServerClient Start(ProcessStartInfo info)
    {
        info.UseShellExecute = false; info.CreateNoWindow = true;
        info.RedirectStandardInput = info.RedirectStandardOutput = info.RedirectStandardError = true;
        info.StandardInputEncoding = info.StandardOutputEncoding = info.StandardErrorEncoding = new UTF8Encoding(false);
        var process = Process.Start(info) ?? throw new IOException("Codex App Server did not start.");
        return new(process.StandardOutput, process.StandardInput, process);
    }
    public async Task<JsonElement> Call(string method, object? parameters = null, CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        long id = Interlocked.Increment(ref sequence);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending[id] = completion;
        try
        {
            await Send(new { id, method, @params = parameters ?? new { } }, token);
            return await completion.Task.WaitAsync(TimeSpan.FromSeconds(60), token);
        }
        finally { pending.TryRemove(id, out _); }
    }
    public Task Notify(string method, object? parameters = null) => Send(new { method, @params = parameters ?? new { } });
    public Task Respond(ServerRequest request, object result) => Send(new { id = request.Id, result });
    public Task Reject(ServerRequest request) => Send(new { id = request.Id, error = new { code = -32601, message = "This client does not support this request." } });
    private async Task Send(object value, CancellationToken token = default)
    {
        await writes.WaitAsync(token);
        try { await writer.WriteLineAsync(JsonSerializer.Serialize(value).AsMemory(), token); await writer.FlushAsync(token); }
        finally { writes.Release(); }
    }
    private async Task Read()
    {
        string reason = "Codex disconnected. Reconnect before continuing; verify any pending Revit operation.";
        try
        {
            while (await reader.ReadLineAsync(lifetime.Token) is { } line)
            {
                if (line.Length > 4 * 1024 * 1024) throw new InvalidDataException("Codex message exceeded the client limit.");
                using var document = JsonDocument.Parse(line); var root = document.RootElement;
                if (root.TryGetProperty("method", out var method))
                {
                    var parameters = root.TryGetProperty("params", out var p) ? p.Clone() : JsonSerializer.SerializeToElement(new { });
                    if (root.TryGetProperty("id", out var requestId))
                    {
                        var request = new ServerRequest(requestId.Clone(), method.GetString()!, parameters);
                        if (Request is { } handler) handler(request); else await Reject(request);
                    }
                    else Notification?.Invoke(new(method.GetString()!, parameters));
                }
                else if (root.TryGetProperty("id", out var id) && id.TryGetInt64(out long number) && pending.TryRemove(number, out var completion))
                {
                    if (root.TryGetProperty("error", out var error)) completion.TrySetException(new IOException(error.GetProperty("message").GetString()));
                    else completion.TrySetResult(root.GetProperty("result").Clone());
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { reason = "Codex protocol connection failed: " + e.GetType().Name; }
        finally
        {
            disposed = true;
            foreach (var item in pending.Values) item.TrySetException(new IOException(reason));
            pending.Clear(); Disconnected?.Invoke(reason);
        }
    }
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposing, 1) != 0) return;
        disposed = true; lifetime.Cancel();
        // Kill only our child, never another Codex/Revit process.
        if (process is { HasExited: false }) process.Kill(entireProcessTree: true);
        try { await pump.WaitAsync(TimeSpan.FromSeconds(5)); if (errors is not null) await errors; } catch (OperationCanceledException) { } catch (TimeoutException) { }
        process?.Dispose();
    }
}
