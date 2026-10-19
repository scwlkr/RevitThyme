using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;

namespace RevitThyme.TimberFold.Managed;

internal static class Program
{
    internal static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static int Main(string[] args)
    {
        string? output = null; bool ownsOutput = false;
        try
        {
            if (args.Length != 5) throw new ArgumentException("Arguments: installed-directory package-directory expected-manifest-sha256 fixtures-directory fresh-output-directory");
            output = Path.GetFullPath(args[4]);
            if (!Path.IsPathFullyQualified(args[4]) || Directory.Exists(output) || File.Exists(output))
                throw new IOException("Qualification output must be a fresh absolute directory; existing evidence is preserved.");
            foreach (string root in new[] { args[0], args[1], args[3] })
            {
                string relative = Path.GetRelativePath(Path.GetFullPath(root), output);
                if (relative == "." || !Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                    throw new IOException("Qualification output must be outside installed/package/fixture roots.");
            }
            Directory.CreateDirectory(output); ownsOutput = true;
            var before = PayloadProof.Validate(args[0], args[1], args[2]);
            string[] runtimePath = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator);
            var system = new[] { Environment.GetFolderPath(Environment.SpecialFolder.Windows), Environment.SystemDirectory,
                Path.Combine(Environment.SystemDirectory, "Wbem") }.Select(Path.GetFullPath).ToArray();
            if (runtimePath.Length == 0 || runtimePath.Any(p => string.IsNullOrWhiteSpace(p) || !system.Contains(Path.GetFullPath(p), StringComparer.OrdinalIgnoreCase)))
                throw new InvalidDataException("Replay PATH must contain only the Windows/system/Wbem directories.");
            var commandProbes = runtimePath.SelectMany(directory => new[] { "python.exe", "python3.exe", "py.exe", "pyrevit.exe" }
                .Select(name => new { Path = Path.Combine(directory, name), Exists = File.Exists(Path.Combine(directory, name)) })).ToArray();
            if (commandProbes.Any(p => p.Exists)) throw new InvalidDataException("Python/pyRevit commands remain visible on the restricted replay PATH.");
            // Keep this bootstrap free of Core type references: validate before loading,
            // then JIT the separately defined public-seam replay after exact binding.
            var loaded = new List<object>();
            foreach (var expected in before.ManagedAssemblies)
            {
                string name = expected.File;
                string path = Path.Combine(before.InstalledDirectory, name);
                var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
                if (!string.Equals(Path.GetFullPath(assembly.Location), path, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Assembly binding escaped the exact installed payload: " + name);
                var version = FileVersionInfo.GetVersionInfo(path);
                string hash = PayloadProof.Hash(path);
                if (!hash.Equals(expected.Sha256, StringComparison.OrdinalIgnoreCase) || assembly.GetName().Version?.ToString(3) != expected.Version)
                    throw new InvalidDataException("Loaded managed hash/version does not match the pinned manifest: " + name);
                string? informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
                if (name == "RevitThyme.Core.dll" && informational?.Split('+')[0] != before.Version)
                    throw new InvalidDataException("Loaded Core stage differs from the pinned manifest.");
                loaded.Add(new { Name = name, assembly.Location, Identity = assembly.FullName, FileSha256 = hash,
                    version.FileVersion, version.ProductVersion,
                    InformationalVersion = informational });
            }
            var replay = FixtureReplay.Run(args[3], output);
            var comparison = CanonicalFiles.Compare(Path.Combine(output, "pass-1"), Path.Combine(output, "pass-2"));
            var after = PayloadProof.Validate(args[0], args[1], args[2]);
            var report = new { Schema = 1, Passed = true, Before = before, After = after, LoadedAssemblies = loaded, RuntimePath = runtimePath,
                Runtime = Environment.Version.ToString(), PythonCommandPathProbes = commandProbes,
                EnvironmentScope = "Other parent environment variables are inherited; command probes describe this PATH, not software installed elsewhere.",
                Replay = replay, Determinism = comparison,
                ActualRevitNativeQualificationOutstanding = true, PhysicalOutstanding = true, ManufacturingReady = false };
            File.WriteAllBytes(Path.Combine(output, "qualification.json"), JsonSerializer.SerializeToUtf8Bytes(report, Json));
            Console.WriteLine($"PASS installed managed payload: four fixtures, two runs, {comparison.Length} identical canonical files. {output}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL " + ex.Message);
            // Never overwrite an existing result when refusing a reused destination.
            if (ownsOutput && output is not null)
                File.WriteAllBytes(Path.Combine(output, "failure.json"), JsonSerializer.SerializeToUtf8Bytes(new { Passed = false, Error = ex.ToString() }, Json));
            return 1;
        }
    }
}
