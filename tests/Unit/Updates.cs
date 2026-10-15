using System.Net;
using System.Text;
using System.Text.Json;
using RevitThyme.Core;
using RevitThyme.Cli;

namespace RevitThyme.Unit;

internal static class Updates
{
    public static async Task Run(Action<string, bool> check)
    {
        using var handler = new ReleaseHandler(); using var http = new HttpClient(handler);
        var checker = new UpdateChecker(http);
        check("newer stable release offered", (await checker.CheckAsync()).Status == "available");
        handler.Version = ProductVersion.Current;
        check("same version is current", (await checker.CheckAsync()).Status == "current");
        handler.Version = "0.8.1";
        check("development build does not offer downgrade", (await checker.CheckAsync()).Status == "ahead");
        handler.Year = "2028";
        check("incompatible Revit year reported", (await checker.CheckAsync()).Status == "incompatible");
        handler.Year = "2027"; handler.Version = "99.0.0";
        foreach (string fault in new[] { "draft", "prerelease", "asset", "duplicate", "metadata", "digest", "oversize", "redirect", "http" })
        {
            handler.Fault = fault;
            try { await checker.CheckAsync(); check("update rejects " + fault, false); }
            catch (Exception error) when (error is InvalidDataException or HttpRequestException) { check("update rejects " + fault, true); }
        }
        handler.Fault = "";
        try { await checker.CheckAsync(version: "0.9.0"); check("pinned release mismatch rejected", false); }
        catch (InvalidDataException) { check("pinned release mismatch rejected", true); }
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        try { await checker.CheckAsync(cancellation: cancellation.Token); check("update check cancellation", false); }
        catch (OperationCanceledException) { check("update check cancellation", true); }
        foreach (string invalid in new[] { "v0.9.0", "0.9", "00.9.0", "0.9.0-beta", "0.9.0/evil", "999999999999.0.0" })
        {
            try { ProductVersion.ParseStable(invalid); check("invalid release version rejected", false); }
            catch (Exception error) when (error is ArgumentException or OverflowException) { check("invalid release version rejected", true); }
        }
        string directory = Path.Combine(Path.GetTempPath(), "RevitThyme-Updates-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "last-check.txt"); var now = DateTimeOffset.UtcNow;
            check("first daily attempt claimed", DailyUpdateCheck.Claim(path, now));
            check("second Revit window skips daily attempt", !DailyUpdateCheck.Claim(path, now.AddHours(23)));
            check("next day retries even after network failure", DailyUpdateCheck.Claim(path, now.AddDays(1)));
            File.WriteAllText(path, "damaged"); check("damaged cache repaired", DailyUpdateCheck.Claim(path, now));
            using (var locked = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                check("concurrent daily claim stays quiet", !DailyUpdateCheck.Claim(path, now));
            var output = new StringWriter(); string? invokedVersion = null;
            Task<string> Download(string version, string? destination, bool install) { invokedVersion = version; return Task.FromResult(Path.Combine(destination!, "setup.exe")); }
            Task<UpdateResult> Available(string? version) => checker.CheckAsync(version: version);
            ToolkitInventory Inventory() => new(new(ProductVersion.Current, ProductVersion.Build, directory), null,
                new("installed", ProductVersion.Current, ProductVersion.Build, "0.9.0-agent", directory, "fixture.addin", ""));
            await UpdateCli.Run(["update", "download", "--destination", directory], output, Available, Download, Inventory);
            check("CLI download pins the checked release", invokedVersion == "99.0.0" && output.ToString().Contains("Verified installer:", StringComparison.Ordinal));
            invokedVersion = null; handler.Version = "0.8.1";
            await UpdateCli.Run(["update", "download", "--destination", directory], new StringWriter(), Available, Download, Inventory);
            check("CLI latest update does not downgrade", invokedVersion is null);
            await UpdateCli.Run(["update", "download", "--destination", directory, "--version", "0.8.1"], new StringWriter(), Available, Download, Inventory);
            check("CLI explicit version supports deliberate download", invokedVersion == "0.8.1");
            output = new(); await UpdateCli.Run(["update", "check", "--json"], output, Available, inventory: Inventory);
            check("CLI check returns structured version and state", JsonDocument.Parse(output.ToString()).RootElement.GetProperty("status").GetString() == "ahead");
            output = new(); await ProgramVersion(output);
            check("CLI version distinguishes executing and installed builds", output.ToString().Contains("CLI: " + ProductVersion.Build, StringComparison.Ordinal)
                && output.ToString().Contains("Development:", StringComparison.Ordinal) && output.ToString().Contains("Installed RevitThyme", StringComparison.Ordinal));
            foreach (string[] args in new[] { new[] { "update", "download" }, new[] { "update", "check", "--json", "--json" }, new[] { "update", "check", "--source", "http://other" } })
            {
                try { await UpdateCli.Run(args, new StringWriter(), Available, inventory: Inventory); check("CLI malformed update refused", false); }
                catch (ArgumentException) { check("CLI malformed update refused", true); }
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
    private static Task<int> ProgramVersion(TextWriter output) => RevitThyme.Cli.Program.RunAsync(["--version"], output, new StringWriter());
    private sealed class ReleaseHandler : HttpMessageHandler
    {
        public string Version = "99.0.0", Year = "2027", Fault = "";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            if (Fault == "http") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            bool metadata = request.RequestUri!.Host != "api.github.com";
            if (metadata && Fault == "redirect")
            { var redirect = new HttpResponseMessage(HttpStatusCode.Redirect); redirect.Headers.Location = new("http://evil.test/file"); return Task.FromResult(redirect); }
            string tag = "v" + Version;
            object Asset(string name) => new { name, browser_download_url = $"https://github.com/scwlkr/RevitThyme/releases/download/{tag}/{name}", digest = Fault == "digest" ? "sha256:" + new string('0', 64) : null };
            var assets = new List<object> { Asset($"RevitThyme-{Version}-win-x64.exe"), Asset($"RevitThyme-{Version}-win-x64.exe.sha256"), Asset("release.json") };
            if (Fault == "asset") assets.RemoveAt(0); if (Fault == "duplicate") assets.Add(Asset("release.json"));
            string json = metadata ? JsonSerializer.Serialize(new { schema_version = 1, product = "RevitThyme", version = Fault == "metadata" ? "0.0.0" : Version,
                platform = "win-x64", isolated_build = false, supported_revit_years = new[] { Year } })
                : JsonSerializer.Serialize(new { tag_name = tag, html_url = $"https://github.com/scwlkr/RevitThyme/releases/tag/{tag}", draft = Fault == "draft", prerelease = Fault == "prerelease", assets });
            if (Fault == "oversize") json = new('x', 256 * 1024);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }
}
