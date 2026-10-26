using System.IO;
using System.Security.Cryptography;
using RevitThyme.Core;

namespace RevitThyme.Addin;

internal static class NativeLifecycleFiles
{
    internal static void AssertLocal(string path)
    {
        string driveName = Path.GetPathRoot(path) ?? throw new ArgumentNullException("driveName");
        if (new DriveInfo(driveName).DriveType != DriveType.Fixed)
            throw new OperationException("unsupported_path", "Use a local fixed-drive RVT; mapped network and removable drives are outside this scope.");
        if (!File.Exists(path)) throw new OperationException("file_missing", "The explicit local RVT does not exist.");
        for (string? current = path; current is not null; current = Path.GetDirectoryName(current))
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new OperationException("unsupported_path", "Use an explicit local RVT without reparse-point aliases.");
    }

    internal static string Hash(string path) => Hash(path, SHA256.HashData);

    internal static string Hash(string path, Func<Stream, byte[]> digest)
    {
        var before = new FileInfo(path);
        long length = before.Length; DateTime stamp = before.LastWriteTimeUtc;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        string hash = Convert.ToHexStringLower(digest(stream));
        before.Refresh();
        if (length != before.Length || stamp != before.LastWriteTimeUtc)
            throw new OperationException("file_changed", "File changed during verification.");
        return hash;
    }
}
