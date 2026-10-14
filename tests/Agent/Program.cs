using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using RevitThyme.Codex;
using RevitThyme.Core;

if (args is ["--probe", var executable, var mcp, var bundled, var folder])
{
    Directory.CreateDirectory(folder); string id = Guid.NewGuid().ToString();
    using var process = System.Diagnostics.Process.GetCurrentProcess();
    var binding = new SessionBinding(Protocol.Version, process.Id, process.StartTime.ToUniversalTime().Ticks.ToString(), id, $"RevitThyme-Foundation-{process.Id}-{id}", "2027");
    string file = Path.Combine(folder, "session.json"); File.WriteAllText(file, JsonSerializer.Serialize(binding, Protocol.Json));
    await using var live = new AgentSession(new(executable, mcp, file, Path.Combine(folder, "workspace"), bundled, Path.Combine(folder, "skills")));
    await live.Connect(); var account = await live.Account();
    Console.WriteLine("Runtime authentication: " + (account.GetProperty("account").ValueKind == JsonValueKind.Null ? "sign-in required" : AgentSession.Text(account.GetProperty("account"), "type")));
    var models = await live.Models(); Console.WriteLine("Runtime models: " + models.GetProperty("data").GetArrayLength());
    var skills = await live.Skills(); Console.WriteLine("Bundled skills: " + string.Join(", ", skills.Where(s => s.Path.StartsWith(bundled, StringComparison.OrdinalIgnoreCase)).Select(s => s.Name)));
    var status = await live.McpStatus(); File.WriteAllText(Path.Combine(folder, "mcp.json"), status.ToString());
    Console.WriteLine("Runtime MCP servers: " + string.Join(", ", status.GetProperty("data").EnumerateArray().Select(s => AgentSession.Text(s, "name"))));
    var login = await live.Login(); string loginId = AgentSession.Text(login, "loginId");
    if (!Uri.TryCreate(AgentSession.Text(login, "authUrl"), UriKind.Absolute, out var authUrl) || authUrl.Scheme != "https" || string.IsNullOrEmpty(loginId)) throw new InvalidOperationException("ChatGPT login protocol failed.");
    await live.CancelLogin(loginId); Console.WriteLine("Runtime ChatGPT browser-login initiation and cancellation passed (no browser consent performed).");
    return;
}

int count = 0;
void Check(string name, bool passed) { if (!passed) throw new InvalidOperationException(name); Console.WriteLine("PASS " + name); count++; }
var wire = new Wire(); await using var rpc = new AppServerClient(wire, wire.Output);
var seen = new TaskCompletionSource<ServerRequest>(TaskCreationOptions.RunContinuationsAsynchronously);
rpc.Request += r => seen.TrySetResult(r);
var delta = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
rpc.Notification += e => delta.TrySetResult(e.Method);
var first = rpc.Call("first"); var second = rpc.Call("second");
var a = await wire.Out.Reader.ReadAsync(); var b = await wire.Out.Reader.ReadAsync();
wire.Feed(new { id = b.GetProperty("id"), result = new { value = 2 } });
wire.Feed(new { method = "item/agentMessage/delta", @params = new { delta = "stream" } });
wire.Feed(new { id = "approval-1", method = "item/commandExecution/requestApproval", @params = new { command = "test" } });
wire.Feed(new { id = a.GetProperty("id"), result = new { value = 1 } });
Check("out-of-order RPC responses preserve correlation", (await first).GetProperty("value").GetInt32() == 1 && (await second).GetProperty("value").GetInt32() == 2);
Check("streaming does not block on unanswered approval", await delta.Task == "item/agentMessage/delta");
await rpc.Respond(await seen.Task, new { decision = "decline" });
var approval = await wire.Out.Reader.ReadAsync(); Check("approval echoes opaque server ID and explicit decision", approval.GetProperty("id").GetString() == "approval-1" && approval.GetProperty("result").GetProperty("decision").GetString() == "decline");
var failure = rpc.Call("bad"); var bad = await wire.Out.Reader.ReadAsync(); wire.Feed(new { id = bad.GetProperty("id"), error = new { code = -1, message = "injected" } });
try { await failure; throw new Exception("not rejected"); } catch (IOException e) { Check("RPC error is surfaced to caller", e.Message == "injected"); }
using var cancel = new CancellationTokenSource(); var cancelled = rpc.Call("cancel", token: cancel.Token); var late = await wire.Out.Reader.ReadAsync(); cancel.Cancel();
try { await cancelled; throw new Exception("not cancelled"); } catch (OperationCanceledException) { Check("caller cancellation releases pending RPC", true); }
wire.Feed(new { id = late.GetProperty("id"), result = new { } });
var unfinished = rpc.Call("unfinished"); await wire.Out.Reader.ReadAsync(); wire.In.Writer.Complete();
try { await unfinished; throw new Exception("not disconnected"); } catch (IOException) { Check("EOF fails outstanding requests instead of hanging", true); }

var sessionWire = new Wire();
var fake = Task.Run(async () =>
{
    await foreach (var message in sessionWire.Out.Reader.ReadAllAsync())
    {
        if (!message.TryGetProperty("method", out var method)) continue;
        string name = method.GetString()!; if (!message.TryGetProperty("id", out var id)) continue;
        object response = name switch
        {
            "thread/start" => new { thread = new { id = "thread-1" } },
            "thread/list" => new { data = new[] { new { id = "thread-1", preview = "Fixture chat" } } },
            "thread/resume" => new { thread = new { id = "thread-1", turns = Array.Empty<object>() } },
            "skills/list" => new { data = new[] { new { skills = new[] { new { name = "inspect-revit", description = "Inspect", path = "C:/skills/inspect-revit/SKILL.md", enabled = true } } } } },
            "turn/start" => new { turn = new { id = "turn-1" } },
            "account/login/start" => new { loginId = "login-1", authUrl = "https://auth.openai.com/test" },
            _ => new { }
        };
        if (name == "initialize") Check("stable initialization identifies RevitThyme", message.GetProperty("params").GetProperty("clientInfo").GetProperty("name").GetString() == "revitthyme_agent");
        if (name == "turn/start")
        {
            Check("selected ordinary skill is sent as typed input", message.GetProperty("params").GetProperty("input")[1].GetProperty("type").GetString() == "skill");
            // completion before turn/start response is legal and must not resurrect the turn.
            sessionWire.Feed(new { method = "turn/completed", @params = new { threadId = "thread-1", turn = new { id = "turn-1", status = "failed", error = new { message = "fixture failure" } } } });
        }
        sessionWire.Feed(new { id, result = response });
    }
});
var options = new AgentOptions("C:/codex.exe", "C:/mcp.exe", "C:/session.json", "C:/agent/workspace", "C:/bundle/skills", "C:/agent/skills");
await using (var session = new AgentSession(options, _ => new(sessionWire, sessionWire.Output)))
{
    await session.Connect(); Check("ChatGPT sign-in uses app-server account flow", (await session.Login()).GetProperty("loginId").GetString() == "login-1");
    await session.NewConversation(); Check("conversation can be listed and resumed", (await session.Conversations()).Single().Id == "thread-1" && (await session.Resume("thread-1")).GetProperty("id").GetString() == "thread-1");
    try { await session.Resume("unrelated-thread"); throw new Exception("not rejected"); } catch (InvalidOperationException) { Check("unrelated sessions cannot be resumed", true); }
    var result = await session.Send("Inspect", (await session.Skills()).Single());
    Check("failed turn is never reported as success and early completion clears turn", result.Status == "failed" && result.Error == "fixture failure" && session.TurnId is null);
    sessionWire.In.Writer.Complete();
}
sessionWire.Out.Writer.Complete(); await fake;
var rejectWire = new Wire(); await using var reject = new AppServerClient(rejectWire, rejectWire.Output);
rejectWire.Feed(new { id = 99, method = "unknown/serverRequest", @params = new { } });
var rejection = await rejectWire.Out.Reader.ReadAsync(); Check("unsupported server requests receive a protocol rejection", rejection.GetProperty("id").GetInt32() == 99 && rejection.GetProperty("error").GetProperty("code").GetInt32() == -32601);
rejectWire.In.Writer.Complete();
Console.WriteLine($"{count} Agent checks passed.");

sealed class Wire : TextReader
{
    public Channel<string> In { get; } = Channel.CreateUnbounded<string>();
    public Channel<JsonElement> Out { get; } = Channel.CreateUnbounded<JsonElement>();
    public TextWriter Output { get; }
    public Wire() => Output = new Writer(Out);
    public void Feed(object value) => In.Writer.TryWrite(JsonSerializer.Serialize(value));
    public override async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
    { try { return await In.Reader.ReadAsync(cancellationToken); } catch (ChannelClosedException) { return null; } }
    private sealed class Writer(Channel<JsonElement> channel) : TextWriter
    {
        public override Encoding Encoding => Encoding.UTF8;
        public override Task WriteLineAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken = default)
        { channel.Writer.TryWrite(JsonSerializer.Deserialize<JsonElement>(buffer.ToString())); return Task.CompletedTask; }
    }
}
