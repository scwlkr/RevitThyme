using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using RevitThyme.Core;
using RevitThyme.UI;

namespace RevitThyme.UiTests;

internal static class Program
{
    private static int checks;

    [STAThread]
    private static int Main()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        try
        {
            SettingsRequireNewPreview();
            ReconciliationKeepsGeneration();
            UnknownStartKeepsIdentity();
            Console.WriteLine($"WPF behavior checks: {checks} passed.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static void SettingsRequireNewPreview()
    {
        var client = new RunClient(); var window = new TimberFoldWindow(client);
        try
        {
            window.PreviewAsync().GetAwaiter().GetResult();
            Guid original = window.CurrentRun!.RunId;
            var stock = (TextBox)Descendants<Label>(window).Single(l => Equals(l.Content, "Stock width (in)")).Target;
            stock.Text = "12";
            var generation = window.GenerateAsync().GetAwaiter().GetResult();
            Check("changed displayed stock cannot generate a kit from hidden old settings", generation is null &&
                !client.Requests.Any(r => r.Operation == OperationKind.GenerateTimberFold) && window.RetainedRunId == original);
            Check("changed settings disable Generate and explain the required next step",
                !Descendants<Button>(window).Single(b => Equals(b.Content, "_Generate")).IsEnabled &&
                Descendants<TextBlock>(window).Any(t => t.Text.Contains("new preview", StringComparison.OrdinalIgnoreCase)));
            window.PreviewAsync().GetAwaiter().GetResult();
            var preview = client.Requests.Last(r => r.Operation == OperationKind.PreviewTimberFold);
            window.GenerateAsync().GetAwaiter().GetResult();
            var submitted = client.Requests.Last();
            Check("a new preview restores generation using the displayed stock settings", preview.Id != original &&
                preview.TimberFold?.Profile?.StockWidthInches == 12 && submitted.Operation == OperationKind.GenerateTimberFold &&
                submitted.OperationId == preview.Id && submitted.TimberFold is null && window.CurrentRun?.State == TimberRunState.KitReady);
        }
        finally { window.Close(); }
    }

    private static void UnknownStartKeepsIdentity()
    {
        var client = new RunClient { UnknownStart = true }; var window = new TimberFoldWindow(client);
        try
        {
            window.PreviewAsync().GetAwaiter().GetResult(); Guid? original = window.RetainedRunId;
            var frame = new DispatcherFrame(); var wait = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(650) };
            wait.Tick += (_, _) => { wait.Stop(); frame.Continue = false; }; wait.Start(); Dispatcher.PushFrame(frame);
            window.PreviewAsync().GetAwaiter().GetResult();
            Check("unknown starts keep their original ID when run and request lookups are unsettled", original is not null &&
                window.RetainedRunId == original && client.Requests.Count(r => r.Operation == OperationKind.PreviewTimberFold) == 1 &&
                client.Requests.Any(r => r.Operation == OperationKind.GetOperation && r.OperationId == original));
        }
        finally { window.Close(); }
        Check("closing the window never cancels its retained run", client.Requests.All(r => r.Operation != OperationKind.CancelTimberFoldRun));
    }

    private static void ReconciliationKeepsGeneration()
    {
        var client = new RunClient { GenerationUnconfirmed = true }; var window = new TimberFoldWindow(client);
        try
        {
            window.PreviewAsync().GetAwaiter().GetResult(); window.GenerateAsync().GetAwaiter().GetResult();
            Guid generation = window.CurrentRun!.RunId;
            window.ReconcileAsync().GetAwaiter().GetResult();
            var request = client.Requests.Last();
            Check("native window reconciles the original generation with a fresh target and separate run ID",
                request.Operation == OperationKind.ReconcileTimberFold && request.OperationId == generation && request.Id != generation &&
                client.Requests[^2].Operation == OperationKind.Inspect && window.CurrentRun?.GenerationRunId == generation &&
                window.CurrentRun.State == TimberRunState.KitReady && client.Requests.Count(r => r.Operation == OperationKind.GenerateTimberFold) == 1);
        }
        finally { window.Close(); }
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }

    private static void Check(string name, bool passed)
    { if (!passed) throw new InvalidOperationException("FAIL " + name); checks++; Console.WriteLine("PASS " + name); }

    private sealed class RunClient : IOperationClient
    {
        internal readonly List<OperationRequest> Requests = [];
        internal bool UnknownStart, GenerationUnconfirmed;
        private readonly Target target = new(1, "start", "session", "fixture", "view", 1);
        public Task<OperationResult> ExecuteAsync(OperationRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            OperationResult result;
            if (request.Operation == OperationKind.Inspect)
                result = new(request.Id, Outcome.Inspected, "ok", "Context", Snapshot: new(target, "2027", "Fixture", "", false, false, "View", "3D", 1, true, null));
            else if (request.Operation == OperationKind.GetTimberFoldRun || request.Operation == OperationKind.GetOperation)
                result = OperationResult.Reject(request, request.Operation == OperationKind.GetOperation ? "pending" : "not_found", "Start unresolved.");
            else if (UnknownStart) result = new(request.Id, Outcome.Unconfirmed, "transport_lost", "Start unresolved.");
            else result = new(request.Id, Outcome.Inspected, "ok", "Ready", TimberRun: new(request.Id, target,
                request.Operation == OperationKind.PreviewTimberFold ? TimberRunState.PreviewReady :
                    request.Operation == OperationKind.GenerateTimberFold && GenerationUnconfirmed ? TimberRunState.Unconfirmed : TimberRunState.KitReady,
                "Ready", "private-run", null, 4, 1, 12, [], PreviewRunId: request.OperationId,
                NativeCommitted: request.Operation is OperationKind.GenerateTimberFold or OperationKind.ReconcileTimberFold,
                GenerationRunId: request.Operation == OperationKind.ReconcileTimberFold ? request.OperationId : null));
            return Task.FromResult(result);
        }
    }
}
