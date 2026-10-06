using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
namespace RevitThyme.Native;
public sealed class DesktopLaunch : IDisposable
{
    private readonly NamedPipeServerStream channel;
    private readonly string credential;
    public ProcessStartInfo StartInfo { get; }
    public DesktopLaunch(string exe, string nativePipe, int pid, string ticks, string session, string credential)
    {
        this.credential = credential;
        string bootstrap = "RevitThyme-bootstrap-" + Guid.NewGuid();
        // Reserve the ACL-restricted first instance before launching the intended child.
        channel = LocalPipe.Create(bootstrap);
        StartInfo = new(exe) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in new[] { "--revit-pipe", nativePipe, "--revit-session", session, "--revit-pid", pid.ToString(), "--revit-start", ticks, "--revit-bootstrap", bootstrap })
            StartInfo.ArgumentList.Add(arg);
    }
    public Process Start()
    {
        var process = Process.Start(StartInfo) ?? throw new IOException("Desktop launch failed.");
        int child = process.Id;
        _ = Task.Run(async () =>
        {
            using var timeout = new CancellationTokenSource(15_000);
            try
            {
                await channel.WaitForConnectionAsync(timeout.Token);
                if (LocalPipe.ClientProcessId(channel) != child) throw new IOException("Unexpected desktop child.");
                await channel.WriteAsync(Encoding.ASCII.GetBytes(credential + "\n"), timeout.Token);
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException) { }
            finally { channel.Dispose(); }
        });
        return process;
    }
    public void Dispose() => channel.Dispose();
}
