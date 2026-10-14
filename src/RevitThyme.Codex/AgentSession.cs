using System.Text.Json;

namespace RevitThyme.Codex;

public sealed record AgentSkill(string Name, string Description, string Path);
public sealed record Conversation(string Id, string Title);
public sealed record TurnOutcome(string Status, string? Error);

public sealed class AgentSession : IAsyncDisposable
{
    private readonly AgentOptions options;
    private readonly Func<AgentOptions, AppServerClient> factory;
    private AppServerClient? client;
    private TaskCompletionSource<TurnOutcome>? turn;
    private readonly SemaphoreSlim actions = new(1, 1);
    private bool uncertain;
    public string? ThreadId { get; private set; }
    public string? TurnId { get; private set; }
    public bool Connected => client is not null;
    public event Action<ServerEvent>? Event;
    public event Action<ServerRequest>? Approval;
    public event Action<string>? Disconnected;
    public AgentSession(AgentOptions options, Func<AgentOptions, AppServerClient>? factory = null)
    { this.options = options; this.factory = factory ?? (o => AppServerClient.Start(o.Launch())); }
    public async Task Connect()
    {
        if (client is not null) return;
        var next = factory(options); client = next;
        next.Notification += Handle;
        next.Request += request =>
        {
            if (request.Method is "item/commandExecution/requestApproval" or "item/fileChange/requestApproval" or "mcpServer/elicitation/request" or "item/tool/requestUserInput")
            {
                if (Approval is { } handler) handler(request); else _ = Decline(request);
            }
            else _ = next.Reject(request);
        };
        next.Disconnected += message => { turn?.TrySetException(new IOException(message)); Disconnected?.Invoke(message); };
        try
        {
            await next.Call("initialize", new { clientInfo = new { name = "revitthyme_agent", title = "RevitThyme Agent", version = "0.9.0" } });
            await next.Notify("initialized");
            await next.Call("skills/extraRoots/set", new { extraRoots = new[] { options.BundledSkills, options.PersonalSkills } });
        }
        catch { client = null; await next.DisposeAsync(); throw; }
    }
    private AppServerClient Client => client ?? throw new InvalidOperationException("Connect Codex first.");
    public Task<JsonElement> Account() => Client.Call("account/read", new { refreshToken = false });
    public Task<JsonElement> Login() => Client.Call("account/login/start", new { type = "chatgpt" });
    public Task<JsonElement> CancelLogin(string id) => Client.Call("account/login/cancel", new { loginId = id });
    public Task<JsonElement> Models() => Client.Call("model/list", new { limit = 100 });
    public async Task<AgentSkill[]> Skills()
    {
        var result = await Client.Call("skills/list", new { cwds = new[] { options.Workspace }, forceReload = true });
        return result.GetProperty("data").EnumerateArray().SelectMany(d => d.GetProperty("skills").EnumerateArray())
            .Where(s => s.GetProperty("enabled").GetBoolean()).Select(s => new AgentSkill(Text(s, "name"), Text(s, "description"), Text(s, "path"))).ToArray();
    }
    public async Task<Conversation[]> Conversations()
    {
        var result = await Client.Call("thread/list", new { cwd = options.Workspace, limit = 100 });
        return result.GetProperty("data").EnumerateArray().Select(t => new Conversation(Text(t, "id"),
            string.IsNullOrEmpty(Text(t, "name")) ? Text(t, "preview") : Text(t, "name"))).ToArray();
    }
    private object Settings(string? model) => new { cwd = options.Workspace, model, approvalPolicy = "on-request", sandbox = "read-only",
        developerInstructions = "You are RevitThyme Agent inside Revit. Use only revitthyme MCP for native operations. The server is pinned to this Revit process. Start with get_context; verify the exact active document and view before edits, use fresh captures and request IDs, read back results. User requests authorize their requested model changes; clarify consequential ambiguity. Revit writes are reversible with Undo, never save or sync unless explicitly requested. Explain pending/unconfirmed results and inspect get_operation before retrying. Use bundled or selected skills. Trusted C# is full process trust and requires explicit host opt-in and user intent. Do not use shell or file changes to bypass MCP." };
    public async Task NewConversation(string? model = null)
    {
        await actions.WaitAsync();
        try { RequireIdle(); var result = await Client.Call("thread/start", Settings(model)); ThreadId = Text(result.GetProperty("thread"), "id"); }
        finally { actions.Release(); }
    }
    public async Task<JsonElement> Resume(string id, string? model = null)
    {
        await actions.WaitAsync();
        try
        {
            RequireIdle();
            // Only conversations listed for the dedicated Agent workspace may be resumed.
            if (!(await Conversations()).Any(c => c.Id == id)) throw new InvalidOperationException("Choose a RevitThyme conversation.");
            var settings = JsonSerializer.SerializeToElement(Settings(model));
            var parameters = settings.EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value);
            parameters["threadId"] = id;
            var result = await Client.Call("thread/resume", parameters); ThreadId = id; return result.GetProperty("thread").Clone();
        }
        finally { actions.Release(); }
    }
    public async Task<TurnOutcome> Send(string text, AgentSkill? skill = null)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 24000) throw new ArgumentException("Enter a message of 1–24,000 characters.");
        await actions.WaitAsync(); Task<TurnOutcome> outcome;
        try
        {
            RequireIdle(); if (ThreadId is null) throw new InvalidOperationException("Start a conversation.");
            var inputs = new List<object> { new { type = "text", text } };
            if (skill is not null)
            {
                if (!(await Skills()).Any(s => s == skill)) throw new InvalidOperationException("Selected skill is no longer available.");
                inputs.Add(new { type = "skill", name = skill.Name, path = skill.Path });
            }
            turn = new(TaskCreationOptions.RunContinuationsAsynchronously); outcome = turn.Task;
            try { var result = await Client.Call("turn/start", new { threadId = ThreadId, input = inputs });
                if (!outcome.IsCompleted) TurnId = Text(result.GetProperty("turn"), "id"); }
            catch { uncertain = true; turn?.TrySetResult(new("unconfirmed", "Turn start was not confirmed. Reconnect and inspect the Revit result before retrying.")); throw; }
        }
        finally { actions.Release(); }
        return await outcome;
    }
    public Task<JsonElement> Interrupt() => TurnId is null ? throw new InvalidOperationException("No active turn.") : Client.Call("turn/interrupt", new { threadId = ThreadId, turnId = TurnId });
    public Task<JsonElement> McpStatus() => Client.Call("mcpServerStatus/list", new { limit = 100, threadId = ThreadId });
    public Task Respond(ServerRequest request, object result) => Client.Respond(request, result);
    public Task Decline(ServerRequest request) => request.Method switch
    {
        "mcpServer/elicitation/request" => Client.Respond(request, new { action = "decline" }),
        "item/tool/requestUserInput" => Client.Respond(request, new { answers = new Dictionary<string, object>() }),
        _ => Client.Respond(request, new { decision = "decline" })
    };
    private void Handle(ServerEvent e)
    {
        if (e.Method == "turn/started" && Text(e.Parameters, "threadId") == ThreadId) TurnId = Text(e.Parameters.GetProperty("turn"), "id");
        if (e.Method == "turn/completed" && Text(e.Parameters, "threadId") == ThreadId)
        {
            var result = e.Parameters.GetProperty("turn"); TurnId = null;
            turn?.TrySetResult(new(Text(result, "status"), result.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object ? Text(error, "message") : null));
        }
        Event?.Invoke(e);
    }
    private void RequireIdle() { if (uncertain) throw new InvalidOperationException("Turn start is unconfirmed. Reconnect and verify the native result before retrying."); if (turn is { Task.IsCompleted: false }) throw new InvalidOperationException("Wait for the current reply or stop it first."); }
    public static string Text(JsonElement value, string name) => value.TryGetProperty(name, out var text) && text.ValueKind == JsonValueKind.String ? text.GetString()! : "";
    public async ValueTask DisposeAsync() { if (client is { } owned) { client = null; await owned.DisposeAsync(); } ThreadId = TurnId = null; }
}
