using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace RevitThyme.Lab;
internal static class Credential
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Entry
    {
        public uint Flags, Type;
        public string TargetName;
        public string? Comment;
        public long LastWritten;
        public uint BlobSize;
        public IntPtr Blob;
        public uint Persist, AttributeCount;
        public IntPtr Attributes;
        public string? Alias, UserName;
    }
    // CRED_PERSIST_SESSION: Windows removes this entry at logoff; normal lab shutdown deletes it.
    public static void Store(string target, string secret)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(secret);
        var pointer = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, pointer, bytes.Length);
            var entry = new Entry { Type = 1, TargetName = target, BlobSize = (uint)bytes.Length, Blob = pointer, Persist = 1 };
            if (!CredWriteW(ref entry, 0)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot store lab credential.");
        }
        finally { Marshal.Copy(new byte[bytes.Length], 0, pointer, bytes.Length); Marshal.FreeHGlobal(pointer); Array.Clear(bytes); }
    }
    public static void Delete(string target) => _ = CredDeleteW(target, 1, 0);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWriteW(ref Entry entry, uint flags);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDeleteW(string target, uint type, uint flags);
}
