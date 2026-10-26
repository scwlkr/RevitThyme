using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace RevitThyme.Quality;

public sealed record SnapshotFile(string Path, string Sha256);

public static class SourceSnapshot
{
    public static IReadOnlyList<SnapshotFile> Capture(string root)
    {
        var result = Git(root, ["ls-files", "-z", "--cached", "--others", "--exclude-standard"]);
        if (result.Code != 0) throw new IOException("Git source inventory failed.");
        string output = result.Output;
        var paths = output.Split('\0', StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
        var files = new List<SnapshotFile>();
        foreach (string path in paths)
        {
            string full = Resolve(root, path);
            if (!File.Exists(full)) continue; // A local deletion is part of this snapshot.
            if ((File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0) throw new IOException("Source links are not copied: " + path);
            files.Add(new(path, HashFile(full)));
        }
        if (files.Count == 0) throw new InvalidDataException("Git source inventory is empty.");
        return files;
    }

    public static string? Revision(string root)
    {
        var result = Git(root, ["rev-parse", "--verify", "HEAD"]);
        return result.Code == 0 ? result.Output.Trim() : null;
    }

    private static (int Code, string Output) Git(string root, string[] arguments)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = root, RedirectStandardOutput = true,
            RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (string arg in arguments) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new IOException("Cannot inventory Git source.");
        var error = process.StandardError.ReadToEndAsync();
        string output = process.StandardOutput.ReadToEnd(); process.WaitForExit();
        error.GetAwaiter().GetResult();
        return (process.ExitCode, output);
    }

    public static string Resolve(string root, string relative)
    {
        string full = Path.GetFullPath(Path.Combine(root, relative));
        string prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        if (Path.IsPathRooted(relative) || !full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Source path escapes its root: " + relative);
        return full;
    }

    public static string HashFile(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    public static string Digest(IEnumerable<SnapshotFile> files) => SourceAnalysis.Hash(string.Join('\n', files.Select(f => f.Path + ":" + f.Sha256)));

    public static void Copy(string root, string worker, IReadOnlyList<SnapshotFile> files)
    {
        if (Directory.Exists(worker)) throw new IOException("Mutation worker must be a new directory.");
        Directory.CreateDirectory(worker);
        foreach (var file in files)
        {
            string source = Resolve(root, file.Path), target = Resolve(worker, file.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            byte[] bytes = File.ReadAllBytes(source);
            if (Convert.ToHexStringLower(SHA256.HashData(bytes)) != file.Sha256) throw new IOException("Source changed while copying: " + file.Path);
            File.WriteAllBytes(target, bytes);
        }
    }

    public static void Verify(string root, IReadOnlyList<SnapshotFile> expected)
    {
        if (Digest(Capture(root)) != Digest(expected)) throw new IOException("Source changed during quality analysis; recollect evidence.");
    }
}
