using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace RevitThyme.Addin;

internal static class LocalPipe
{
    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes { public int Length; public IntPtr Descriptor; public int Inherit; }
    public static NamedPipeServerStream Create(string name)
    {
        string sid = WindowsIdentity.GetCurrent().User?.Value ?? throw new InvalidDataException("User SID unavailable.");
        if (!ConvertStringSecurityDescriptorToSecurityDescriptor($"D:P(A;;GA;;;{sid})", 1, out var descriptor, out _))
            throw new IOException("Pipe ACL creation failed.");
        try
        {
            var security = new SecurityAttributes { Length = Marshal.SizeOf<SecurityAttributes>(), Descriptor = descriptor };
            // First owned instance, overlapped duplex, byte mode, reject remote clients.
            var handle = CreateNamedPipe(@"\\.\pipe\" + name, 0x40080003, 0x8, 1,
                Core.Protocol.MaxBytes, Core.Protocol.MaxBytes, 0, ref security);
            if (handle.IsInvalid) { handle.Dispose(); throw new IOException("Owned local pipe creation failed."); }
            return new NamedPipeServerStream(PipeDirection.InOut, true, false, handle);
        }
        finally { LocalFree(descriptor); }
    }
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(string text, uint revision, out IntPtr descriptor, out uint size);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafePipeHandle CreateNamedPipe(string name, uint openMode, uint pipeMode, uint maxInstances,
        int outBuffer, int inBuffer, uint timeout, ref SecurityAttributes attributes);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
}
