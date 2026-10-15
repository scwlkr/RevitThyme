using System.Security.Cryptography;
using System.Text.Json;
using RevitThyme.Core;
using RevitThyme.Cli;

namespace RevitThyme.Unit;

internal static class InstalledCli
{
    public static async Task Run(Action<string, bool> check)
    {
        string root = Path.Combine(Path.GetTempPath(), "RevitThyme-Installed-" + Guid.NewGuid()); Directory.CreateDirectory(root);
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "src", "RevitThyme.Cli")); File.WriteAllText(Path.Combine(root, "src", "RevitThyme.Cli", "RevitThyme.Cli.csproj"), "fixture");
            string props = Path.Combine(root, "Directory.Build.props");
            foreach (string invalid in new[] { "<damaged", "<Project />", "<Project><Version>bad</Version></Project>" })
            { File.WriteAllText(props, invalid); check("invalid optional checkout metadata stays unavailable", ToolkitInventory.ReadDevelopment(root) is null); }
            string registration = Path.Combine(root, "RevitThyme.Foundation.addin"), receipt = Path.Combine(root, "receipt.json");
            check("unregistered toolkit reports missing", ToolkitInventory.ReadInstalled(registration).Status == "missing");
            string assembly = typeof(ProductVersion).Assembly.Location;
            string hash; using (var input = File.OpenRead(assembly)) hash = Convert.ToHexStringLower(SHA256.HashData(input));
            byte[] manifest = JsonSerializer.SerializeToUtf8Bytes(new { version = "0.9.0-agent", product_version = "0.9.1", revit_year = "2027",
                files = new Dictionary<string, string> { ["RevitThyme.Addin.dll"] = hash, ["RevitThyme.Core.dll"] = hash } });
            string bundle = Path.Combine(root, Convert.ToHexStringLower(SHA256.HashData(manifest))); Directory.CreateDirectory(bundle);
            File.WriteAllBytes(Path.Combine(bundle, "manifest.json"), manifest);
            foreach (string name in new[] { "RevitThyme.Addin.dll", "RevitThyme.Core.dll" }) File.Copy(assembly, Path.Combine(bundle, name));
            string xml = $"<RevitAddIns><AddIn Type=\"Application\"><AddInId>B09CCF69-B916-40B7-8A57-5923E45915D6</AddInId><FullClassName>RevitThyme.Addin.FoundationApplication</FullClassName><Assembly>{System.Security.SecurityElement.Escape(Path.Combine(bundle, "RevitThyme.Addin.dll"))}</Assembly></AddIn></RevitAddIns>";
            File.WriteAllText(registration, xml);
            check("installed public product version separated from package identity", ToolkitInventory.ReadInstalled(registration).Version == "0.9.1");
            File.WriteAllText(receipt, JsonSerializer.Serialize(new { installed = bundle, registration }));
            check("matching optional receipt verified", ToolkitInventory.ReadInstalled(registration, receipt).Status == "installed");
            File.WriteAllText(receipt, JsonSerializer.Serialize(new { installed = root, registration }));
            check("stale receipt fails closed", ToolkitInventory.ReadInstalled(registration, receipt).Status == "invalid");
            File.WriteAllText(registration, xml.Replace("B09CCF69-B916-40B7-8A57-5923E45915D6", Guid.NewGuid().ToString(), StringComparison.Ordinal));
            check("foreign registration is not treated as absent", ToolkitInventory.ReadInstalled(registration).Status == "invalid");
            File.WriteAllText(registration, xml); File.AppendAllText(Path.Combine(bundle, "manifest.json"), " ");
            check("modified manifest identity refused", ToolkitInventory.ReadInstalled(registration).Status == "invalid");
            File.WriteAllBytes(Path.Combine(bundle, "manifest.json"), manifest); File.WriteAllText(Path.Combine(bundle, "RevitThyme.Core.dll"), "changed");
            check("modified installed core refused", ToolkitInventory.ReadInstalled(registration).Status == "invalid");
            File.WriteAllText(registration, "<!DOCTYPE x [<!ENTITY x SYSTEM 'file:///private'>]><RevitAddIns>&x;</RevitAddIns>");
            check("registration external entities refused", ToolkitInventory.ReadInstalled(registration).Status == "invalid");

            var installed = new InstalledToolkit("installed", "0.8.0", "0.8.0+installed", "0.8.0-install", bundle, registration, "");
            ToolkitInventory Inventory() => new(new("0.9.0", "0.9.0+cli", root), new("0.9.0", "0.9.0+development", root), installed);
            Task<UpdateResult> Latest(string? _) => Task.FromResult(new UpdateResult("0.9.0", "0.9.0+cli", "0.9.0", "current", "CLI already current", new("0.9.0", "https://github.com/scwlkr/RevitThyme/releases/tag/v0.9.0", ["2027"])));
            bool invoked = false;
            Task<string> Bootstrap(string version, string? _, bool install) { invoked = version == "0.9.0" && install; return Task.FromResult("Fixture installer completed."); }
            var output = new StringWriter(); await UpdateCli.Run(["version", "--json"], output, inventory: Inventory);
            using (var json = JsonDocument.Parse(output.ToString()))
                check("version JSON carries both checkout and installed versions", json.RootElement.GetProperty("development").GetProperty("version").GetString() == "0.9.0"
                    && json.RootElement.GetProperty("installed").GetProperty("version").GetString() == "0.8.0");
            output = new(); await UpdateCli.Run(["update", "check", "--json"], output, Latest, inventory: Inventory);
            check("current development CLI still offers older installed update", JsonDocument.Parse(output.ToString()).RootElement.GetProperty("status").GetString() == "available");
            await UpdateCli.Run(["update", "install"], new StringWriter(), Latest, Bootstrap, Inventory, () => false);
            check("default install upgrades installed copy even when CLI is current", invoked);
            invoked = false;
            try { await UpdateCli.Run(["update", "install"], new StringWriter(), Latest, Bootstrap, Inventory, () => true); check("needed installation refuses open Revit", false); }
            catch (InvalidOperationException) { check("needed installation refuses open Revit", !invoked); }
            installed = installed with { Version = "0.9.0" };
            await UpdateCli.Run(["update", "install"], new StringWriter(), Latest, Bootstrap, Inventory, () => true);
            check("current installed copy needs no installer even with Revit open", !invoked);
            installed = installed with { Version = "1.0.0" };
            await UpdateCli.Run(["update", "install"], new StringWriter(), Latest, Bootstrap, Inventory, () => false);
            check("newer installed toolkit never automatically downgraded", !invoked);
            installed = installed with { Status = "missing", Version = null, Build = null };
            await UpdateCli.Run(["update", "install"], new StringWriter(), Latest, Bootstrap, Inventory, () => false);
            check("missing installation can be installed from current CLI", invoked);
            installed = installed with { Status = "invalid" }; invoked = false;
            try { await UpdateCli.Run(["update", "install"], new StringWriter(), Latest, Bootstrap, Inventory, () => false); check("invalid registration blocks update", false); }
            catch (InvalidOperationException) { check("invalid registration blocks update", !invoked); }
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
