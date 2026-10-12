using RevitThyme.Lab;
using RevitThyme.Native;
using RevitThyme.AdapterChecks;
using System.Security.Cryptography;
using System.Text.Json;
using Range = RevitThyme.Native.Range;

internal static class TestServer
{
    private sealed class Guarded(string path, Session session) : IModel
    {
        public readonly FakeModel Model = new();
        private readonly FixtureGuard guard = new(path, true);
        public string Actual = path;
        public string Id = "first-document";
        private void Check() => guard.Check(Actual, Id, false);
        public Target CurrentTarget() { Check(); return Model.CurrentTarget(); }
        public Facts Capture() { Check(); Model.Target = Model.Target with { Revision = session.Revision }; return Model.Capture(); }
        public RawRange Validate(Facts f, Range r) { Check(); return Model.Validate(f, r); }
        public RawRange Read(Facts f) { Check(); return Model.Read(f); }
        public IChange Begin(Facts f) { Check(); guard.Write(); return Model.Begin(f); }
    }
    public static async Task Run(string root)
    {
        Directory.CreateDirectory(root);
        string fixture = Path.Combine(root, "RevitThyme-Test.rvt"); // No Revit file is created in this offline check.
        var session = new Session(); var model = new Guarded(fixture, session);
        model.Model.UnderlayFixture = null; model.Model.DuringSet = null;
        using var signal = new AutoResetEvent(false); bool stopped = false;
        var owner = new Thread(() =>
        {
            model.Model.OwnerThread = Environment.CurrentManagedThreadId;
            while (!Volatile.Read(ref stopped)) { signal.WaitOne(100); while (session.HasQueued) session.ExecuteOne(model); }
        }) { IsBackground = true };
        owner.Start();
        var target = model.Model.Target;
        string name = $"RevitThymeLab-{target.ProcessId}-{target.SessionId}";
        using var server = new PipeServer(name, target.ProcessId, target.ProcessStartTicks, target.SessionId, session, () => signal.Set());
        string secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        Credential.Store(name, secret); server.SetCredential(secret);
        var running = server.Run(); await server.Ready.WaitAsync(TimeSpan.FromSeconds(5));
        string binding = Path.Combine(root, "binding.json");
        File.WriteAllText(binding, JsonSerializer.Serialize(new { protocol = 2, process_id = target.ProcessId,
            process_start_ticks = target.ProcessStartTicks, session_id = target.SessionId, pipe = name,
            credential_target = name, fixture_path = fixture, allow_writes = true, revit_version = "27.3.0.28" }, Wire.Json));
        Console.WriteLine(JsonSerializer.Serialize(new { binding }));
        try
        {
            string? command;
            while ((command = await Console.In.ReadLineAsync()) is not null)
            {
                if (command == "wrong_fixture") { model.Actual = Path.Combine(root, "Working.rvt"); session.Invalidate(); }
                if (command == "restore") { model.Actual = fixture; session.Invalidate(); }
                if (command == "reopen") { model.Id = "reopened-document"; session.Invalidate(); }
                if (command == "observe") Console.WriteLine(JsonSerializer.Serialize(new { transactions = model.Model.Transactions, calls = model.Model.Trace.Count, cut = model.Model.Current.Cut.OffsetFeet }));
                else Console.WriteLine(JsonSerializer.Serialize(new { command }));
            }
        }
        finally { Volatile.Write(ref stopped, true); signal.Set(); server.Dispose(); await running; owner.Join(2000); Credential.Delete(name); }
    }
}
