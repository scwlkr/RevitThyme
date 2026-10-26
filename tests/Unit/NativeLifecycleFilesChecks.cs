using System.Diagnostics;
using System.Security.Cryptography;
using RevitThyme.Core;
using Files = RevitThyme.Addin.NativeLifecycleFiles;

namespace RevitThyme.Unit;

internal static class NativeLifecycleFilesChecks
{
    public static void Run(Action<string, bool> check)
    {
        string parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
        string directory = Path.Combine(parent, "RevitThyme-lifecycle-files-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string file = Path.Combine(directory, "fixture.rvt");
            File.WriteAllText(file, "abc");
            Files.AssertLocal(file);
            check("local lifecycle file retains the exact lowercase SHA256", Files.Hash(file) == "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");
            using (var writer = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete))
                check("lifecycle hashing coexists with an existing writable file lease", Files.Hash(file) == "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");
            Reject("missing lifecycle file is rejected", "file_missing", "The explicit local RVT does not exist.", () => Files.AssertLocal(Path.Combine(directory, "absent.rvt")));
            DateTime stamp = File.GetLastWriteTimeUtc(file);
            Reject("length-only change during hashing is rejected", "file_changed", "File changed during verification.", () => Files.Hash(file, stream =>
            {
                byte[] hash = SHA256.HashData(stream);
                File.AppendAllText(file, "d"); File.SetLastWriteTimeUtc(file, stamp);
                return hash;
            }));
            File.WriteAllText(file, "abc"); stamp = File.GetLastWriteTimeUtc(file);
            Reject("timestamp-only change during hashing is rejected", "file_changed", "File changed during verification.", () => Files.Hash(file, stream =>
            {
                byte[] hash = SHA256.HashData(stream);
                File.SetLastWriteTimeUtc(file, stamp.AddSeconds(5));
                return hash;
            }));
            File.WriteAllBytes(file, []);
            check("empty lifecycle file still has its standard digest", Files.Hash(file) == "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");
            if (OperatingSystem.IsWindows())
            {
                var mounted = DriveInfo.GetDrives().Select(d => d.Name[0]).ToHashSet();
                char absent = Enumerable.Range('A', 26).Select(n => (char)n).First(c => !mounted.Contains(c));
                Reject("unmounted drive is rejected before checking file existence", "unsupported_path", "Use a local fixed-drive RVT; mapped network and removable drives are outside this scope.",
                    () => Files.AssertLocal(absent + @":\missing.rvt"));
                string junction = Path.Combine(directory, "alias");
                var start = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
                foreach (string argument in new[] { "/d", "/c", "mklink", "/J", junction, directory }) start.ArgumentList.Add(argument);
                using var process = Process.Start(start) ?? throw new InvalidOperationException("Junction creator did not start.");
                string output = process.StandardOutput.ReadToEnd(), error = process.StandardError.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode != 0) throw new InvalidOperationException("Fixture junction creation failed: " + output + error);
                try
                {
                    Reject("a parent reparse alias is rejected", "unsupported_path", "Use an explicit local RVT without reparse-point aliases.",
                        () => Files.AssertLocal(Path.Combine(junction, "fixture.rvt")));
                }
                finally { Directory.Delete(junction); }
            }
            else Console.WriteLine("SKIP Windows drive/junction lifecycle checks on this platform.");
        }
        finally
        {
            if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(directory)), parent, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Fixture cleanup escaped its temporary parent.");
            Directory.Delete(directory, true);
        }

        void Reject(string name, string code, string message, Action action)
        {
            bool rejected = false;
            try { action(); } catch (OperationException error) { rejected = error.Code == code && error.Message == message; }
            check(name, rejected);
        }
    }
}
