using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using RevitThyme.Core;
using RevitThyme.Codex;

namespace RevitThyme.UI;

public sealed class AgentPanel : UserControl
{
    private readonly IOperationClient native;
    private readonly Func<AgentOptions> options;
    private AgentSession? session;
    private readonly TextBlock status = new() { Text = "Connect Codex to start.", TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock context = new() { TextWrapping = TextWrapping.Wrap };
    private readonly ComboBox conversations = new() { DisplayMemberPath = "Title", MinWidth = 150 };
    private readonly ComboBox models = new() { MinWidth = 110 };
    private readonly ComboBox skills = new() { DisplayMemberPath = "Name", MinWidth = 120 };
    private readonly StackPanel transcript = new();
    private readonly StackPanel approvals = new();
    private readonly ScrollViewer scroll;
    private readonly TextBox input = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 70, MaxHeight = 150, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxLength = 24000 };
    private readonly Button send = Button("Send");
    private readonly Button stop = Button("Stop");
    private readonly Button connect = Button("Connect Codex");
    private readonly Button login = Button("Sign in");
    private readonly Button cancelLogin = Button("Cancel sign-in");
    private readonly List<Button> idleButtons = [];
    private readonly Dictionary<string, TextBlock> messages = [];
    private readonly Dictionary<string, FrameworkElement> requests = [];
    private readonly Dictionary<string, JsonElement> items = [];
    private string? loginId;
    private bool busy;
    public AgentSession? Session => session;
    public string StatusText => status.Text;

    public AgentPanel(IOperationClient native, Func<AgentOptions> options)
    {
        this.native = native; this.options = options;
        FontFamily = new("Segoe UI"); FontSize = 12; Background = SystemColors.ControlBrush;
        var grid = new Grid { Margin = new(10) };
        foreach (var height in new[] { GridLength.Auto, GridLength.Auto, GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto })
            grid.RowDefinitions.Add(new() { Height = height });
        var connection = Row(connect, login, cancelLogin); Put(grid, connection, 0);
        Put(grid, status, 1); context.Margin = new(0, 5, 0, 8); Put(grid, context, 2);
        var toolbar = new StackPanel();
        var fresh = Button("New"); var resume = Button("Resume"); var refresh = Button("Refresh");
        toolbar.Children.Add(Row(conversations, fresh, resume, refresh)); toolbar.Children.Add(Row(new TextBlock { Text = "Model", VerticalAlignment = VerticalAlignment.Center }, models, new TextBlock { Text = "Skill", VerticalAlignment = VerticalAlignment.Center }, skills));
        Put(grid, toolbar, 3);
        var body = new StackPanel(); body.Children.Add(transcript); body.Children.Add(approvals);
        scroll = new() { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new(0, 8, 0, 8) }; Put(grid, scroll, 4);
        Put(grid, input, 5); Put(grid, Row(send, stop), 6); Content = grid;
        idleButtons.AddRange([connect, login, fresh, resume, refresh]); stop.IsEnabled = false; cancelLogin.IsEnabled = false;
        connect.Click += async (_, _) => await Guard(Connect);
        login.Click += async (_, _) => await Guard(SignIn);
        cancelLogin.Click += async (_, _) => await Guard(async () => { if (loginId is { } id) await RequireSession().CancelLogin(id); loginId = null; cancelLogin.IsEnabled = false; });
        fresh.Click += async (_, _) => await Guard(async () => { await RequireSession().NewConversation(models.SelectedItem as string); ClearTranscript(); await Refresh(); });
        resume.Click += async (_, _) => await Guard(async () =>
        {
            if (conversations.SelectedItem is not Conversation chosen) throw new InvalidOperationException("Select a conversation.");
            var thread = await RequireSession().Resume(chosen.Id, models.SelectedItem as string); ClearTranscript();
            if (thread.TryGetProperty("turns", out var turns)) foreach (var turn in turns.EnumerateArray()) foreach (var item in turn.GetProperty("items").EnumerateArray()) ShowItem(item);
        });
        refresh.Click += async (_, _) => await Guard(Refresh);
        send.Click += async (_, _) => await Guard(Send);
        stop.Click += async (_, _) => { try { await RequireSession().Interrupt(); status.Text = "Stopping… verify any pending Revit operation before continuing."; } catch (Exception e) { status.Text = e.Message; } };
    }
    private static Button Button(string text) => new() { Content = text, Padding = new(8, 4, 8, 4), Margin = new(0, 3, 5, 3) };
    private static WrapPanel Row(params UIElement[] children) { var panel = new WrapPanel(); foreach (var child in children) panel.Children.Add(child); return panel; }
    private static void Put(Grid grid, UIElement control, int row) { Grid.SetRow(control, row); grid.Children.Add(control); }
    private AgentSession RequireSession() => session ?? throw new InvalidOperationException("Connect Codex first.");
    private async Task Guard(Func<Task> action)
    {
        if (busy) return; busy = true; foreach (var button in idleButtons) button.IsEnabled = false; send.IsEnabled = false;
        try { await action(); }
        catch (Exception e) { status.Text = e.Message; }
        finally { busy = false; foreach (var button in idleButtons) button.IsEnabled = true; send.IsEnabled = session is not null; stop.IsEnabled = false; }
    }
    public async Task Connect()
    {
        status.Text = "Connecting…";
        if (session is not null) await session.DisposeAsync();
        ClearApprovals(); var owned = new AgentSession(options()); session = owned;
        owned.Event += e => Dispatcher.BeginInvoke(() => { if (session == owned) Handle(e); });
        owned.Approval += r => Dispatcher.BeginInvoke(() => { if (session == owned) ShowApproval(r); });
        owned.Disconnected += text => Dispatcher.BeginInvoke(() => { if (session == owned) { status.Text = text; ClearApprovals(); } });
        try { await session.Connect(); await Refresh(); await ReadContext(); }
        catch { await session.DisposeAsync(); session = null; throw; }
    }
    private async Task SignIn()
    {
        var result = await RequireSession().Login(); loginId = AgentSession.Text(result, "loginId");
        var uri = new Uri(result.GetProperty("authUrl").GetString()!);
        if (uri.Scheme != "https") throw new InvalidDataException("Codex returned an unsupported sign-in URL.");
        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        status.Text = "Finish ChatGPT sign-in in your browser, then return here."; cancelLogin.IsEnabled = true;
    }
    private async Task Refresh()
    {
        var current = RequireSession(); var account = await current.Account();
        status.Text = account.GetProperty("account").ValueKind == JsonValueKind.Null ? "Sign in with ChatGPT to continue." : "Codex connected.";
        string? chosen = models.SelectedItem as string;
        var list = await current.Models(); var catalog = list.GetProperty("data").EnumerateArray().Select(m => AgentSession.Text(m, "model")).Where(m => m.Length > 0).ToArray();
        models.ItemsSource = catalog; models.SelectedItem = catalog.Contains(chosen) ? chosen : list.GetProperty("data").EnumerateArray().Where(m => m.TryGetProperty("isDefault", out var d) && d.GetBoolean()).Select(m => AgentSession.Text(m, "model")).FirstOrDefault() ?? catalog.FirstOrDefault();
        skills.ItemsSource = (await current.Skills()).Prepend(new AgentSkill("Automatic", "Let Codex choose a skill", "")).ToArray(); skills.SelectedIndex = 0;
        var history = await current.Conversations(); conversations.ItemsSource = history; conversations.SelectedItem = history.FirstOrDefault(c => c.Id == current.ThreadId);
    }
    private async Task ReadContext()
    {
        var result = await native.ExecuteAsync(new(Guid.NewGuid(), OperationKind.Inspect));
        if (!result.Succeeded || result.Snapshot is null) throw new InvalidOperationException(result.Message);
        context.Text = result.Snapshot.DocumentTitle + " · " + result.Snapshot.ViewName;
    }
    public async Task<TurnOutcome> SendMessage(string text, AgentSkill? skill = null)
    {
        await ReadContext(); var current = RequireSession();
        if (current.ThreadId is null) await current.NewConversation(models.SelectedItem as string);
        AddText("You", text); status.Text = "Working…"; stop.IsEnabled = true;
        var result = await current.Send(text, skill); status.Text = result.Status == "completed" ? "Ready" : result.Status + ": " + result.Error;
        stop.IsEnabled = false; await RefreshHistory(); return result;
    }
    private async Task Send()
    {
        string text = input.Text; input.Clear();
        try { await SendMessage(text, skills.SelectedItem is AgentSkill { Path.Length: > 0 } skill ? skill : null); }
        catch { input.Text = text; throw; }
    }
    private async Task RefreshHistory()
    { var history = await RequireSession().Conversations(); conversations.ItemsSource = history; conversations.SelectedItem = history.FirstOrDefault(c => c.Id == session!.ThreadId); }
    private void Handle(ServerEvent e)
    {
        if (e.Parameters.TryGetProperty("threadId", out var id) && id.GetString() != session?.ThreadId) return;
        if (e.Method == "item/agentMessage/delta")
        {
            string itemId = AgentSession.Text(e.Parameters, "itemId");
            if (!messages.TryGetValue(itemId, out var text)) { text = AddText("Codex", ""); messages[itemId] = text; }
            text.Text += AgentSession.Text(e.Parameters, "delta"); scroll.ScrollToEnd();
        }
        else if (e.Method == "item/started") { var item = e.Parameters.GetProperty("item"); items[AgentSession.Text(item, "id")] = item.Clone(); }
        else if (e.Method == "item/completed") ShowItem(e.Parameters.GetProperty("item"));
        else if (e.Method == "serverRequest/resolved")
        { string key = e.Parameters.GetProperty("requestId").ToString(); if (requests.Remove(key, out var element)) approvals.Children.Remove(element); }
        else if (e.Method == "account/login/completed")
        { loginId = null; cancelLogin.IsEnabled = false; status.Text = e.Parameters.GetProperty("success").GetBoolean() ? "Signed in. Refresh to continue." : "Sign-in did not complete."; }
        else if (e.Method == "error") status.Text = "Codex reported an error. " + e.Parameters.ToString();
    }
    private void ShowItem(JsonElement item)
    {
        string type = AgentSession.Text(item, "type"), id = AgentSession.Text(item, "id");
        if (type == "agentMessage")
        { if (messages.TryGetValue(id, out var block)) block.Text = AgentSession.Text(item, "text"); else messages[id] = AddText("Codex", AgentSession.Text(item, "text")); }
        else if (type == "userMessage")
        { if (item.TryGetProperty("content", out var content)) AddText("You", string.Join("\n", content.EnumerateArray().Select(c => AgentSession.Text(c, "text")))); }
        else if (type is "mcpToolCall" or "commandExecution" or "fileChange")
        {
            var detail = new TextBox { Text = JsonSerializer.Serialize(item, new JsonSerializerOptions { WriteIndented = true }), IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 250, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            transcript.Children.Add(new Expander { Header = type == "mcpToolCall" ? AgentSession.Text(item, "tool") + " · " + AgentSession.Text(item, "status") : type, Content = detail, Margin = new(0, 4, 0, 4) });
        }
        scroll.ScrollToEnd();
    }
    private TextBlock AddText(string speaker, string text)
    {
        transcript.Children.Add(new TextBlock { Text = speaker, FontWeight = FontWeights.SemiBold, Margin = new(0, 8, 0, 2) });
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }; transcript.Children.Add(block); scroll.ScrollToEnd(); return block;
    }
    private void ShowApproval(ServerRequest request)
    {
        var owner = RequireSession(); var panel = new StackPanel { Margin = new(0, 8, 0, 8) };
        panel.Children.Add(new TextBlock { Text = "Your input is needed", FontWeight = FontWeights.SemiBold });
        var accept = Button("Accept"); var decline = Button("Decline"); panel.Children.Add(Row(accept, decline));
        Func<object>? response = null;
        try
        {
            if (request.Method is "item/commandExecution/requestApproval" or "item/fileChange/requestApproval")
            {
                panel.Children.Add(new TextBlock { Text = AgentSession.Text(request.Parameters, "reason"), TextWrapping = TextWrapping.Wrap });
                var preview = items.GetValueOrDefault(AgentSession.Text(request.Parameters, "itemId"), request.Parameters);
                panel.Children.Add(new TextBox { Text = preview.ToString(), IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 180, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            }
            response = AgentInputForm.Build(panel, request);
        }
        catch (Exception e) when (e is NotSupportedException or KeyNotFoundException or InvalidOperationException)
        { accept.IsEnabled = false; panel.Children.Add(new TextBlock { Text = e.Message, TextWrapping = TextWrapping.Wrap }); }
        string key = request.Id.ToString(); requests[key] = panel; approvals.Children.Add(panel); scroll.ScrollToEnd();
        accept.Click += async (_, _) =>
        {
            try
            {
                await owner.Respond(request, response!()); approvals.Children.Remove(panel); requests.Remove(key);
            }
            catch (Exception e) { status.Text = "Input could not be sent: " + e.Message; }
        };
        decline.Click += async (_, _) => { try { await owner.Decline(request); approvals.Children.Remove(panel); requests.Remove(key); } catch (Exception e) { status.Text = e.Message; } };
    }
    private void ClearTranscript() { transcript.Children.Clear(); messages.Clear(); items.Clear(); }
    private void ClearApprovals() { approvals.Children.Clear(); requests.Clear(); }
    public void Shutdown() { if (session is { } owned) { session = null; _ = owned.DisposeAsync(); } ClearApprovals(); }
}
