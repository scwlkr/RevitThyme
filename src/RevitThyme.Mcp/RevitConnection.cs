using System.Text.Json;
using RevitThyme.Core;

namespace RevitThyme.Mcp;

public sealed record SessionInfo(string File, SessionBinding Binding);
public sealed record CaptureResult(string CaptureId, OperationResult Result);

// Native calls serialize with connection changes. Controls snapshot one immutable
// binding/client pair and never hold the native-call gate while awaiting a reply.
public sealed partial class RevitConnection
{
    private readonly ServerSettings settings;
    private readonly Func<SessionBinding, IOperationClient> factory;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly object stateGate = new();
    private readonly Dictionary<string, Snapshot> captures = [];
    private sealed record ConnectionState(SessionBinding Binding, IOperationClient Client);
    private ConnectionState? state;
    public RevitConnection(ServerSettings settings) : this(settings, b => new PipeClient(b)) { }
    public RevitConnection(ServerSettings settings, Func<SessionBinding, IOperationClient> factory)
    {
        this.settings = settings; this.factory = factory;
        if (settings.SessionFile is not null)
        { var binding = SessionBinding.Load(settings.SessionFile); state = new(binding, factory(binding)); }
    }
    public SessionInfo[] Sessions()
    {
        var result = new List<SessionInfo>();
        foreach (string path in Directory.Exists(settings.SessionsDirectory) ? Directory.EnumerateFiles(settings.SessionsDirectory, "*.json").Take(1024) : [])
        {
            try { result.Add(new(path, SessionBinding.Load(path))); }
            catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or InvalidOperationException or JsonException or System.ComponentModel.Win32Exception) { }
        }
        return result.OrderBy(s => s.Binding.ProcessId).ToArray();
    }
    public async Task<SessionBinding> Connect(string sessionFile, CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            if (settings.SessionFile is not null && !string.Equals(Path.GetFullPath(sessionFile), Path.GetFullPath(settings.SessionFile), StringComparison.OrdinalIgnoreCase))
                throw new OperationException("session_pinned", "This server is pinned to its startup session file.");
            var chosen = SessionBinding.Load(sessionFile); var next = factory(chosen);
            // Native handshake must succeed before replacing the current connection.
            var result = await next.ExecuteAsync(new(Guid.NewGuid(), OperationKind.Inspect), token);
            if (result.Code != "no_document" && !result.Succeeded) throw new OperationException(result.Code, result.Message);
            lock (stateGate) state = new(chosen, next);
            captures.Clear(); return chosen;
        }
        finally { gate.Release(); }
    }
    public async Task<CaptureResult> Context(CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            var chosen = RequireClient();
            var result = await chosen.ExecuteAsync(new(Guid.NewGuid(), OperationKind.Inspect), token);
            string id = "";
            if (result.Succeeded && result.Snapshot is { } snapshot)
            {
                if (captures.Count >= 512) captures.Remove(captures.Keys.First());
                id = Guid.NewGuid().ToString(); captures.Add(id, snapshot);
            }
            return new(id, result);
        }
        finally { gate.Release(); }
    }
    public async Task<OperationResult> Execute(string captureId, OperationRequest request, CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            var chosen = RequireClient();
            if (!captures.TryGetValue(captureId, out var snapshot)) throw new OperationException("capture_missing", "Call get_context in this connection first.");
            return await chosen.ExecuteAsync(request with { Target = snapshot.Target,
                ExpectedName = request.Operation == OperationKind.RenameView ? snapshot.ViewName : null }, token);
        }
        finally { gate.Release(); }
    }
    public async Task<OperationResult> Outcome(Guid id, CancellationToken token)
    {
        await gate.WaitAsync(token);
        try { var chosen = RequireClient(); return await chosen.ExecuteAsync(new(Guid.NewGuid(), OperationKind.GetOperation, OperationId: id), token); }
        finally { gate.Release(); }
    }
    private IOperationClient RequireClient()
    {
        lock (stateGate)
        {
            var chosen = state ?? throw new OperationException("not_connected", "List sessions, then explicitly connect_session.");
            chosen.Binding.AssertProcess();
            return chosen.Client;
        }
    }
}
