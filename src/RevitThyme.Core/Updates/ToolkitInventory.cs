using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace RevitThyme.Core;

public sealed record ToolkitBuild(string Version, string Build, string Path);
public sealed record InstalledToolkit(string Status, string? Version, string? Build, string? PackageVersion, string? Path, string Registration, string Message);
public sealed record ToolkitInventory(ToolkitBuild Cli, ToolkitBuild? Development, InstalledToolkit Installed)
{
    public static string UserRoot => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData");
    public static ToolkitInventory Capture() => new(new(ProductVersion.Current, ProductVersion.Build, typeof(ToolkitInventory).Assembly.Location),
        FindDevelopment(), ReadInstalled(System.IO.Path.Combine(UserRoot, @"Roaming\Autodesk\Revit\Addins\2027\RevitThyme.Foundation.addin"),
            System.IO.Path.Combine(UserRoot, @"Local\RevitThyme\setup\receipt.json")));

    public static InstalledToolkit ReadInstalled(string registration, string? receipt = null)
    {
        if (!File.Exists(registration)) return new("missing", null, null, null, null, registration, "RevitThyme is not installed for Revit 2027.");
        try
        {
            using var xml = XmlReader.Create(registration, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 65536 });
            var entries = XDocument.Load(xml).Root?.Elements("AddIn").ToArray();
            var entry = entries is [var single] ? single : throw new InvalidDataException("Expected one owned add-in registration.");
            if ((string?)entry.Attribute("Type") != "Application" || !Guid.TryParse((string?)entry.Element("AddInId"), out var id)
                || id != new Guid("B09CCF69-B916-40B7-8A57-5923E45915D6") || (string?)entry.Element("FullClassName") != "RevitThyme.Addin.FoundationApplication")
                throw new InvalidDataException("The registration belongs to another add-in.");
            string assembly = (string?)entry.Element("Assembly") ?? "";
            if (!System.IO.Path.IsPathFullyQualified(assembly) || System.IO.Path.GetFileName(assembly) != "RevitThyme.Addin.dll") throw new InvalidDataException("Unexpected installed assembly path.");
            string folder = System.IO.Path.GetDirectoryName(assembly)!;
            string manifestPath = System.IO.Path.Combine(folder, "manifest.json");
            byte[] bytes = ReadSmall(manifestPath);
            if (!string.Equals(System.IO.Path.GetFileName(folder), Convert.ToHexStringLower(SHA256.HashData(bytes)), StringComparison.Ordinal))
                throw new InvalidDataException("Installed bundle identity does not match its manifest.");
            if (bytes.AsSpan().StartsWith(new byte[] { 239, 187, 191 })) bytes = bytes[3..];
            using var manifest = JsonDocument.Parse(bytes); var root = manifest.RootElement;
            if (root.GetProperty("revit_year").GetString() != "2027") throw new InvalidDataException("Unexpected installed Revit year.");
            foreach (string name in new[] { "RevitThyme.Addin.dll", "RevitThyme.Core.dll" })
            {
                using var file = File.OpenRead(System.IO.Path.Combine(folder, name));
                if (!string.Equals(Convert.ToHexStringLower(SHA256.HashData(file)), root.GetProperty("files").GetProperty(name).GetString(), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Installed bundle was modified: " + name);
            }
            if (receipt is not null && File.Exists(receipt))
            {
                using var document = JsonDocument.Parse(System.Text.Encoding.UTF8.GetString(ReadSmall(receipt)).TrimStart('\uFEFF'));
                if (!SamePath(document.RootElement.GetProperty("installed").GetString(), folder)
                    || !SamePath(document.RootElement.GetProperty("registration").GetString(), registration))
                    throw new InvalidDataException("Installation receipt and Revit registration disagree.");
            }
            string build = FileVersionInfo.GetVersionInfo(assembly).ProductVersion ?? throw new InvalidDataException("Installed assembly version is missing.");
            string version = root.TryGetProperty("product_version", out var product) ? product.GetString() ?? throw new InvalidDataException("Installed product version is missing.") : build.Split('+', '-')[0];
            ProductVersion.ParseStable(version);
            return new("installed", version, build, root.GetProperty("version").GetString(), folder, registration, "Registered for Revit 2027; an open Revit process may require a restart.");
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or XmlException or JsonException or InvalidOperationException or ArgumentException or KeyNotFoundException or OverflowException or System.ComponentModel.Win32Exception)
        { return new("invalid", null, null, null, null, registration, "Could not verify installed RevitThyme: " + error.Message); }
    }
    private static byte[] ReadSmall(string path)
    { if (new FileInfo(path).Length > 65536) throw new InvalidDataException("Installation metadata is too large."); return File.ReadAllBytes(path); }
    private static bool SamePath(string? first, string second) => first is not null && System.IO.Path.IsPathFullyQualified(first)
        && string.Equals(System.IO.Path.GetFullPath(first), System.IO.Path.GetFullPath(second), StringComparison.OrdinalIgnoreCase);
    private static ToolkitBuild? FindDevelopment()
    {
        string? linked = null;
        string config = System.IO.Path.Combine(UserRoot, @"Local\RevitThyme\cli\development.json");
        if (File.Exists(config))
        {
            try { using var data = JsonDocument.Parse(System.Text.Encoding.UTF8.GetString(ReadSmall(config)).TrimStart('\uFEFF')); linked = data.RootElement.GetProperty("checkout").GetString(); }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or KeyNotFoundException or InvalidOperationException) { }
        }
        foreach (string? start in new[] { System.IO.Path.GetDirectoryName(typeof(ToolkitInventory).Assembly.Location), Environment.CurrentDirectory, linked })
        {
            if (start is not null && ReadDevelopment(start) is { } development) return development;
        }
        return null;
    }
    public static ToolkitBuild? ReadDevelopment(string start)
    {
        if (!System.IO.Path.IsPathFullyQualified(start)) return null;
        try
        {
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                string props = System.IO.Path.Combine(directory.FullName, "Directory.Build.props");
                if (!File.Exists(System.IO.Path.Combine(directory.FullName, "src", "RevitThyme.Cli", "RevitThyme.Cli.csproj")) || !File.Exists(props)) continue;
                using var reader = XmlReader.Create(props, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 65536 });
                string version = XDocument.Load(reader).Descendants("Version").First().Value; ProductVersion.ParseStable(version);
                string build = DevelopmentBuild(directory.FullName, version);
                return new(version, build, directory.FullName);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or XmlException or InvalidOperationException or ArgumentException or OverflowException) { }
        return null;
    }
    private static string DevelopmentBuild(string folder, string version)
    {
        try
        {
            var start = new ProcessStartInfo("git") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            foreach (string arg in new[] { "-C", folder, "rev-parse", "HEAD" }) start.ArgumentList.Add(arg);
            using var process = Process.Start(start)!;
            if (!process.WaitForExit(2000)) { process.Kill(); return version; }
            string revision = process.StandardOutput.ReadToEnd().Trim();
            return process.ExitCode == 0 && revision.Length == 40 && revision.All(Uri.IsHexDigit) ? version + "+" + revision : version;
        }
        catch (System.ComponentModel.Win32Exception) { return version; }
    }
}
