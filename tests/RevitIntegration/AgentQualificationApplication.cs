using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using RevitThyme.Addin;
using RevitThyme.Codex;
using RevitThyme.Core;

namespace RevitThyme.RevitIntegration;

public sealed class AgentQualificationApplication : IExternalApplication, IExternalEventHandler
{
    private string? folder;
    private string fixture = "", savedHash = "", thread = "";
    private int stage, count;
    private bool stopped;
    private Task? pending;
    private DateTime started;
    private ExternalEvent? pulseEvent;
    private Timer? pulse;
    private readonly List<object> checks = [];
    private JsonElement? lastCall;
    public Result OnStartup(UIControlledApplication app)
    {
        folder = Environment.GetEnvironmentVariable("REVITTHYME_AGENT_TEST_RUN"); if (folder is null) return Result.Succeeded;
        fixture = Path.Combine(folder, "Foundation-Test.rvt");
        if (!Path.IsPathFullyQualified(folder) || File.Exists(fixture) || fixture != Environment.GetEnvironmentVariable("REVITTHYME_FOUNDATION_FIXTURE")) return Result.Failed;
        started = DateTime.UtcNow; pulseEvent = ExternalEvent.Create(this); app.Idling += Idle;
        pulse = new(_ => { if (!stopped) { try { pulseEvent.Raise(); } catch (Exception) { } } }, null, 1000, 250); return Result.Succeeded;
    }
    private void Check(string name, bool passed) { checks.Add(new { name, passed }); Write("progress.json", new { stage, checks }); if (!passed) throw new InvalidOperationException(name); }
    private void Idle(object? sender, IdlingEventArgs e) => Advance((UIApplication)sender!);
    public void Execute(UIApplication app) => Advance(app);
    private void Advance(UIApplication app)
    {
        if (stopped) return;
        try
        {
            if ((DateTime.UtcNow - started).TotalMinutes > 12) throw new TimeoutException("Agent qualification timed out.");
            var host = FoundationApplication.Instance ?? throw new InvalidOperationException("Host missing."); var doc = app.ActiveUIDocument?.Document;
            switch (stage)
            {
                case 0:
                    Check("fresh disposable process", app.Application.Documents.Size == 0); Fixture.Create(app, fixture, true); stage++; break;
                case 1:
                    app.ActiveUIDocument!.RequestViewChange(new FilteredElementCollector(doc!).OfClass(typeof(ViewPlan)).Cast<ViewPlan>().Single(v => v.Name == "Foundation Plan")); stage++; break;
                case 2:
                    if (doc!.ActiveView.Name != "Foundation Plan") return;
                    savedHash = Hash(); count = Count(doc);
                    app.GetDockablePane(new(new Guid("D988D5C7-B1BB-40CE-A636-48C344DA7E68"))).Show();
                    pending = First(host); stage++; break;
                case 3:
                    if (!pending!.IsCompleted) return; pending.GetAwaiter().GetResult();
                    Check("panel Agent edit is independently present in native Revit", doc!.ActiveView.Name == "Agent Native Verified" && Count(doc) == count && Hash() == savedHash);
                    Capture(host); app.PostCommand(RevitCommandId.LookupPostableCommandId(PostableCommand.Undo)); stage++; break;
                case 4:
                    if (doc!.ActiveView.Name != "Foundation Plan") return;
                    Check("one native Undo reverses Agent edit", Count(doc) == count && Hash() == savedHash);
                    pending = Resume(host); stage++; break;
                case 5:
                    if (!pending!.IsCompleted) return; pending.GetAwaiter().GetResult();
                    Check("resumed Agent session edits through new pinned MCP connection", doc!.ActiveView.Name == "Agent Resumed" && Hash() == savedHash);
                    app.PostCommand(RevitCommandId.LookupPostableCommandId(PostableCommand.Undo)); stage++; break;
                case 6:
                    if (doc!.ActiveView.Name != "Foundation Plan") return;
                    Check("resumed edit Undo and original fixture invariants", Count(doc) == count && Hash() == savedHash);
                    pending = Interrupt(host); stage++; break;
                case 7:
                    if (!pending!.IsCompleted) return; pending.GetAwaiter().GetResult();
                    Check("interrupted inspection retains fixture", Count(doc!) == count && doc!.ActiveView.Name == "Foundation Plan" && Hash() == savedHash);
                    Capture(host); host.AgentPanel!.Shutdown(); Finish(true); break;
            }
        }
        catch (Exception e) { Finish(false, e); }
    }
    private async Task First(FoundationApplication host)
    {
        var panel = host.AgentPanel!; await panel.Connect(); var session = panel.Session!;
        var account = await session.Account(); Check("actual runtime has authenticated ChatGPT account", account.GetProperty("account").ValueKind == JsonValueKind.Object && AgentSession.Text(account.GetProperty("account"), "type").StartsWith("chatgpt", StringComparison.OrdinalIgnoreCase));
        session.Event += Trace;
        session.Approval += request => QualifyApproval(host, request);
        var skills = await session.Skills(); Check("bundled ordinary SKILL.md files discovered by runtime", skills.Any(s => s.Name == "inspect-revit") && skills.Any(s => s.Name == "edit-revit"));
        await session.NewConversation(); thread = session.ThreadId!;
        var mcp = await session.McpStatus(); Write("mcp-status.json", mcp);
        var servers = mcp.GetProperty("data").EnumerateArray().ToArray();
        Check("only this panel's native MCP server is connected", servers.Length == 1 && AgentSession.Text(servers[0], "name") == "revitthyme");
        var result = await panel.SendMessage($"You are qualifying only disposable fixture {fixture} in process {host.Binding.ProcessId}. Use get_context and verify that exact path/process and Foundation Plan. Rename the active view to Agent Native Verified with rename_view and a fresh GUID. Refresh get_context and verify the new native name. Report what you verified. This edit is explicitly authorized. Leave the model unsaved, and keep all other elements unchanged.", skills.Single(s => s.Name == "edit-revit"));
        Check("first live streamed turn completed successfully", result.Status == "completed"); Write("thread.json", new { id = thread });
    }
    private async Task Resume(FoundationApplication host)
    {
        await host.AgentPanel!.Connect(); var session = host.AgentPanel.Session!; session.Event += Trace;
        session.Approval += request => QualifyApproval(host, request);
        await session.Resume(thread); Check("stored conversation resumes after client restart", session.ThreadId == thread);
        var result = await host.AgentPanel.SendMessage($"The native Undo restored Foundation Plan. Inspect fresh context and confirm {fixture}, process {host.Binding.ProcessId}. Rename the active view to Agent Resumed with a fresh GUID. Read it back and report the actual name. This edit is explicitly authorized; leave the model unsaved.");
        Check("resumed streamed turn completed", result.Status == "completed");
    }
    private async Task Interrupt(FoundationApplication host)
    {
        var session = host.AgentPanel!.Session!; var response = session.Send("Inspect the active view, then give a detailed explanation of every available RevitThyme operation. Make no changes.");
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (session.TurnId is null && !response.IsCompleted && DateTime.UtcNow < deadline) await Task.Delay(20);
        if (session.TurnId is not null) await session.Interrupt();
        var result = await response; Check("actual runtime interruption resolves turn", result.Status == "interrupted");
    }
    private void Trace(ServerEvent e)
    {
        if (e.Method == "item/started" && AgentSession.Text(e.Parameters.GetProperty("item"), "type") == "mcpToolCall") lastCall = e.Parameters.GetProperty("item").Clone();
        if (e.Method.StartsWith("item/", StringComparison.Ordinal) || e.Method.StartsWith("turn/", StringComparison.Ordinal))
            File.AppendAllText(Path.Combine(folder!, "events.jsonl"), JsonSerializer.Serialize(e, Protocol.Json) + "\n");
    }
    private void QualifyApproval(FoundationApplication host, ServerRequest request)
    {
        Write("approval-" + stage + ".json", new { request.Method, request.Parameters });
        host.AgentPanel!.Dispatcher.BeginInvoke(() =>
        {
            try
            {
                if (lastCall is not { } call || AgentSession.Text(call, "tool") != "rename_view"
                    || AgentSession.Text(call.GetProperty("arguments"), "name") is not ("Agent Native Verified" or "Agent Resumed"))
                    throw new InvalidOperationException("Qualification approves only its explicitly requested fixture rename.");
                // Operate the actual native WPF approval controls after the panel displays them.
                var all = Descendants(host.AgentPanel!).ToArray();
                var form = all.OfType<StackPanel>().Last(p => p.Children.OfType<TextBlock>().Any(t => t.Text == "Your input is needed"));
                foreach (var combo in Descendants(form).OfType<System.Windows.Controls.ComboBox>())
                {
                    object? chosen = combo.Items.Cast<object>().FirstOrDefault(v => v.ToString() is "Accept" or "Allow once" or "Approve this call");
                    if (chosen is null) throw new InvalidOperationException("Unsupported approval choice."); combo.SelectedItem = chosen;
                }
                foreach (var box in Descendants(form).OfType<CheckBox>()) box.IsChecked = true;
                var accept = Descendants(form).OfType<Button>().Single(b => b.Content?.ToString() == "Accept");
                Check("native panel displays explicit approval for authorized fixture rename", form.IsVisible && accept.IsEnabled);
                accept.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            catch (Exception e) { Finish(false, e); }
        });
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject node)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
        { yield return child; foreach (var descendant in Descendants(child)) yield return descendant; }
    }
    private void Capture(FoundationApplication host)
    {
        var panel = host.AgentPanel!; panel.UpdateLayout();
        Check("native dockable panel is visible and laid out", panel.IsVisible && panel.ActualWidth > 100 && panel.ActualHeight > 100);
        var bitmap = new RenderTargetBitmap((int)panel.ActualWidth, (int)panel.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(panel);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(Path.Combine(folder!, "agent-panel.png")); encoder.Save(file);
    }
    private static int Count(Document doc) => new FilteredElementCollector(doc).WhereElementIsNotElementType().GetElementCount();
    private string Hash() { using var file = new FileStream(fixture, FileMode.Open, FileAccess.Read, FileShare.ReadWrite); return Convert.ToHexStringLower(SHA256.HashData(file)); }
    private void Write(string name, object value) => File.WriteAllText(Path.Combine(folder!, name), JsonSerializer.Serialize(value, Protocol.Json));
    private void Finish(bool passed, Exception? error = null) { stopped = true; pulse?.Dispose(); Write("native-result.json", new { passed, checks, error = error?.ToString(), stage }); }
    public string GetName() => "RevitThyme disposable Agent qualification";
    public Result OnShutdown(UIControlledApplication app) { stopped = true; app.Idling -= Idle; pulse?.Dispose(); pulseEvent?.Dispose(); return Result.Succeeded; }
}
