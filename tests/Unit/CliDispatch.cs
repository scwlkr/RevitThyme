using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using RevitThyme.Core;

namespace RevitThyme.Unit;

internal static class CliDispatch
{
    public static async Task Run(Action<string, bool> check)
    {
        string folder = Path.Combine(Path.GetTempPath(), "RevitThyme-CliDispatch-" + Guid.NewGuid());
        Directory.CreateDirectory(folder);
        string session = Path.Combine(folder, "session.json");
        try
        {
            using var process = Process.GetCurrentProcess();
            string id = Guid.NewGuid().ToString();
            var binding = new SessionBinding(Protocol.Version, process.Id,
                process.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture),
                id, $"RevitThyme-Foundation-{process.Id}-{id}", "2027");
            File.WriteAllText(session, JsonSerializer.Serialize(binding, Protocol.Json));
            foreach (string[] command in new string[][] {
                ["inspect"], ["list-documents"], ["inspect-elements", "--element", "element"],
                ["view-range", "get"], ["room-dimensions", "preview", "--room", "room"] })
            {
                var output = new StringWriter(); var error = new StringWriter();
                var client = new RejectedContext();
                int exit = await Cli.Program.RunAsync([.. command, "--session", session], output, error, _ => client);
                bool direct = command[0] == "inspect";
                check("CLI rejected context stops " + string.Join(' ', command), exit == 1
                    && client.Requests is [{ Operation: OperationKind.Inspect }]
                    && (direct ? output.ToString() == "no_document: No document." + Environment.NewLine
                        : JsonSerializer.Deserialize<OperationResult>(output.ToString(), Protocol.Json)?.Code == "no_document")
                    && error.ToString() == (direct || command[0] == "list-documents" ? "No document." + Environment.NewLine : ""));
            }
            foreach (string[] args in new string[][] {
                ["inspect", "--bogus", "value"], ["inspect", "--json", "--json"],
                ["inspect", "--session"], ["unknown"], ["view-range", "unknown"],
                ["list-documents", "--offset", "4097"], ["list-documents", "--limit", "0"],
                ["list-documents", "--request-id", Guid.Empty.ToString()],
                ["inspect-elements", "--element", "element", "--parameter-offset", "1"] })
            {
                int factories = 0; var client = new RejectedContext(); bool rejected = false;
                string[] withSession = args.Contains("--session") ? args : [.. args, "--session", session];
                try { await Cli.Program.RunAsync(withSession, new StringWriter(), new StringWriter(), _ => { factories++; return client; }); }
                catch (ArgumentException) { rejected = true; }
                check("CLI invalid input never reaches host: " + string.Join(' ', args), rejected && client.Requests.Count == 0);
                if (args[0] is "inspect" or "unknown" or "view-range")
                    check("CLI dispatch validates command/options before session factory", factories == 0);
            }
        }
        finally { File.Delete(session); Directory.Delete(folder); }
    }

    private sealed class RejectedContext : IOperationClient
    {
        public List<OperationRequest> Requests { get; } = [];
        public Task<OperationResult> ExecuteAsync(OperationRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(OperationResult.Reject(request, "no_document", "No document."));
        }
    }
}
