using RevitThyme.Native;
using System.Text.Json;
namespace RevitThyme.AdapterChecks;
internal static class FixtureServer
{
    public static async Task Run()
    {
        string credential = await Console.In.ReadLineAsync() ?? "";
        if (credential.Length != 64) throw new InvalidDataException("Owned test credential required.");
        var session = new Session(); var model = new FakeModel();
        var signal = new AutoResetEvent(false); bool stopped = false, paused = false;
        var owner = new Thread(() =>
        {
            model.OwnerThread = Environment.CurrentManagedThreadId;
            while (!Volatile.Read(ref stopped))
            { signal.WaitOne(100); if (!Volatile.Read(ref paused)) while (session.HasQueued) session.ExecuteOne(model); }
        });
        owner.IsBackground = true; owner.Start();
        string name = $"RevitThyme-{model.Target.ProcessId}-{model.Target.SessionId}";
        using var server = new PipeServer(name, model.Target.ProcessId, model.Target.ProcessStartTicks, model.Target.SessionId, session, () => signal.Set());
        server.SetCredential(credential); var running = server.Run(); await server.Ready.WaitAsync(TimeSpan.FromSeconds(5));
        Console.WriteLine(JsonSerializer.Serialize(new { pipe = name, process_id = model.Target.ProcessId, process_start_ticks = model.Target.ProcessStartTicks, session_id = model.Target.SessionId }, Wire.Json));
        string? command;
        while ((command = await Console.In.ReadLineAsync()) is not null)
        {
            if (command.StartsWith("launch "))
            {
                string exe = command[7..];
                var launch = new DesktopLaunch(exe, name, model.Target.ProcessId, model.Target.ProcessStartTicks, model.Target.SessionId, credential);
                launch.StartInfo.ArgumentList.Add("--remote-debugging-port=0");
                launch.StartInfo.RedirectStandardError = true; launch.StartInfo.RedirectStandardOutput = true;
                var process = launch.Start();
                _ = Task.Run(async () => { string? line; while ((line = await process.StandardError.ReadLineAsync()) is not null)
                    if (line.StartsWith("DevTools listening on ws://127.0.0.1:")) Console.WriteLine(JsonSerializer.Serialize(new { cdp = line[22..], child = process.Id })); });
                _ = process.StandardOutput.ReadToEndAsync();
                continue;
            }
            if (command == "pause") Volatile.Write(ref paused, true);
            if (command == "resume") { Volatile.Write(ref paused, false); signal.Set(); }
            if (command == "invalidate") { session.Invalidate(); model.Target = model.Target with { Revision = session.Revision }; }
            if (command == "disconnect") server.SetCredential(credential);
            if (command == "observe") Console.WriteLine(JsonSerializer.Serialize(new { transactions = model.Transactions, cut = model.Current.Cut.OffsetFeet, trace = model.Trace.ToArray() }));
            else Console.WriteLine(JsonSerializer.Serialize(new { command }));
            if (command == "stop") break;
        }
        Volatile.Write(ref stopped, true); signal.Set(); server.Dispose();
        await running; owner.Join(2000); signal.Dispose();
    }
}
