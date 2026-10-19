using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using RevitThyme.Core;

namespace RevitThyme.UI;

public sealed partial class TimberFoldWindow
{
    private readonly Button preview = new() { Content = "_Preview", Width = 84, Height = 27, Margin = new(8, 0, 0, 0),
        ToolTip = "Fit the current exterior shell, plan stock and retain a digital preview." };
    private readonly Button cancelRun = new() { Content = "_Cancel", Width = 84, Height = 27, Margin = new(8, 0, 0, 0), IsEnabled = false,
        ToolTip = "Request cancellation at the next run boundary; retained files remain available." };
    private readonly TextBox runIdentity = new() { IsReadOnly = true, BorderThickness = new(0), Background = System.Windows.Media.Brushes.Transparent,
        Padding = new(0), Margin = new(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap, IsReadOnlyCaretVisible = true,
        ToolTip = "Retained run ID. Closing this window leaves the run and its files available." };
    private readonly DispatcherTimer runTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private Guid? retainedRunId;
    private bool nativeActionBusy, polling, cancelling, cancellationRequested, windowClosed;
    public TimberRunSummary? CurrentRun { get; private set; }
    public Guid? RetainedRunId => retainedRunId;
    private bool HasActiveRun => retainedRunId is not null && CurrentRun is not { Terminal: true };

    public async Task<OperationResult?> PreviewAsync()
    {
        if (nativeActionBusy || HasActiveRun || windowClosed) return null;
        nativeActionBusy = true; UpdateActions();
        try
        {
            var profile = ReadProfile();
            var context = await client.ExecuteAsync(new(Guid.NewGuid(), OperationKind.Inspect));
            if (windowClosed) return context;
            if (!context.Succeeded || context.Snapshot is not { } snapshot)
            { status.Text = context.Message; return context; }
            retainedRunId = Guid.NewGuid(); CurrentRun = null; cancellationRequested = false;
            runIdentity.Text = $"Run {retainedRunId:D}"; diagnostics.Text = "";
            status.Text = "Starting preview…"; UpdateActions();
            var result = await client.ExecuteAsync(new(retainedRunId.Value, OperationKind.PreviewTimberFold,
                snapshot.Target, TimberFold: new(profile)));
            if (windowClosed) return result;
            if (result.TimberRun is { } run) ShowRun(run);
            else
            {
                status.Text = result.Message;
                if (result.Outcome == Outcome.Unconfirmed) runTimer.Start();
                else { retainedRunId = null; runIdentity.Text = ""; }
            }
            return result;
        }
        catch (Exception error)
        {
            status.Text = retainedRunId is null ? error.Message : $"{error.Message}\nChecking retained run status…";
            if (retainedRunId is not null && !windowClosed) runTimer.Start();
            return null;
        }
        finally { nativeActionBusy = false; UpdateActions(); }
    }

    public async Task<OperationResult?> CancelAsync()
    {
        if (nativeActionBusy || !HasActiveRun || retainedRunId is not { } id || cancelling || cancellationRequested || windowClosed) return null;
        cancelling = true; UpdateActions();
        try
        {
            var result = await client.ExecuteAsync(new(Guid.NewGuid(), OperationKind.CancelTimberFoldRun, OperationId: id));
            if (retainedRunId != id || windowClosed) return result;
            if (result.TimberRun is { } run)
            {
                cancellationRequested = !run.Terminal;
                ShowRun(run);
            }
            else status.Text = result.Message;
            return result;
        }
        catch (Exception error) { status.Text = error.Message; return null; }
        finally { cancelling = false; UpdateActions(); }
    }

    private async void PollRun(object? sender, EventArgs args)
    {
        if (polling || windowClosed || !HasActiveRun || retainedRunId is not { } id) return;
        polling = true;
        try
        {
            var result = await client.ExecuteAsync(new(Guid.NewGuid(), OperationKind.GetTimberFoldRun, OperationId: id));
            if (retainedRunId != id || windowClosed) return;
            if (result.TimberRun is { } run) ShowRun(run);
            else
            {
                status.Text = result.Message;
                if (result.Code == "not_found")
                    await RecoverStartAsync(id);
            }
        }
        catch (Exception error) { if (!windowClosed) status.Text = error.Message; }
        finally { polling = false; UpdateActions(); }
    }

    private async Task RecoverStartAsync(Guid id)
    {
        status.Text = "Start not yet confirmed; checking the original request. Keep this run ID for recovery.";
        var original = await client.ExecuteAsync(new(Guid.NewGuid(), OperationKind.GetOperation, OperationId: id));
        if (retainedRunId != id || windowClosed) return;
        if (original.TimberRun is { } run) ShowRun(run);
        else if (original.Id == id && original.Outcome == Outcome.Rejected)
        {
            // A query's own pending/not_found rejection does not settle the original start.
            runTimer.Stop(); retainedRunId = null; CurrentRun = null;
            status.Text = $"Preview start rejected: {original.Message}\nYou may start a new preview.";
        }
        else status.Text = "Start not yet confirmed. Keep this window open to check again; use the displayed run ID for recovery. " +
            "A new preview remains blocked while the original request is unresolved.";
    }

    private void ShowRun(TimberRunSummary run)
    {
        if (windowClosed) return;
        if (CurrentRun is { } current && current.RunId == run.RunId &&
            ((current.Terminal && !run.Terminal) || current.ElapsedMilliseconds > run.ElapsedMilliseconds)) return;
        CurrentRun = run; retainedRunId = run.RunId;
        runIdentity.Text = $"Run {run.RunId:D}";
        status.Text = $"{run.Stage}{(cancellationRequested && !run.Terminal ? " · cancellation requested" : "")}\n" +
            $"{run.PanelCount} panels · {run.SheetCount} sheets · {run.ElapsedMilliseconds / 1000:0.0} s\n" +
            "Native import and physical cutting/assembly remain outstanding.";
        diagnostics.Text = string.Join(Environment.NewLine, run.Diagnostics.Select(d => $"{d.Severity} [{d.SourceId ?? "scope"}]: {d.Message}"));
        if (run.DiagnosticsTruncated) diagnostics.Text += "\nOpen files for complete diagnostics.";
        if (run.Terminal) runTimer.Stop(); else runTimer.Start();
        UpdateActions();
    }

    private void UpdateActions()
    {
        bool editable = !windowClosed && !nativeActionBusy && !HasActiveRun;
        inspect.IsEnabled = preview.IsEnabled = editable;
        cancelRun.IsEnabled = !windowClosed && !nativeActionBusy && HasActiveRun && !cancelling && !cancellationRequested;
        openFiles.IsEnabled = !windowClosed && (CurrentRun?.Directory is not null || CurrentInspection?.Evidence is not null);
        foreach (var number in numbers.Values) number.IsEnabled = editable;
        rotation.IsEnabled = grain.IsEnabled = measuredStock.IsEnabled = measuredThickness.IsEnabled = editable;
    }

    private void OpenFiles()
    {
        string? directory = CurrentRun?.Directory ?? CurrentInspection?.Evidence?.Directory;
        if (directory is null) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(directory) { UseShellExecute = true }); }
        catch (Exception error) { status.Text = error.Message; }
    }
}
