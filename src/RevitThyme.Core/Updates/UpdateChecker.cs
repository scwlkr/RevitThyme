using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

namespace RevitThyme.Core;

public sealed record UpdateRelease(string Version, string Url, string[] SupportedRevitYears);
public sealed record UpdateResult(string CurrentVersion, string Build, string LatestVersion, string Status, string Message, UpdateRelease Release);

public sealed class UpdateChecker(HttpClient http)
{
    public const string Repository = "https://api.github.com/repos/scwlkr/RevitThyme";
    public static HttpClient CreateClient()
    {
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("RevitThyme/" + ProductVersion.Current);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        return client;
    }
    public async Task<UpdateResult> CheckAsync(string revitYear = "2027", string? version = null, CancellationToken cancellation = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(20)); cancellation = timeout.Token;
        if (version is not null) ProductVersion.ParseStable(version);
        string endpoint = Repository + (version is null ? "/releases/latest" : "/releases/tags/v" + version);
        using var release = JsonDocument.Parse(await Read(endpoint, 128 * 1024, cancellation));
        var root = release.RootElement;
        string tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!tag.StartsWith('v')) throw new InvalidDataException("Release tag must begin with v.");
        string latest = tag[1..]; var latestNumber = ProductVersion.ParseStable(latest);
        if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean() || version is not null && latest != version)
            throw new InvalidDataException("Refused an unpublished or unexpected release.");
        string releaseUrl = "https://github.com/scwlkr/RevitThyme/releases/tag/" + tag;
        if (root.GetProperty("html_url").GetString() != releaseUrl) throw new InvalidDataException("Unexpected release page.");
        var assets = root.GetProperty("assets");
        Asset(assets, $"RevitThyme-{latest}-win-x64.exe", tag);
        Asset(assets, $"RevitThyme-{latest}-win-x64.exe.sha256", tag);
        var metadataAsset = Asset(assets, "release.json", tag);
        byte[] metadataBytes = await Read(metadataAsset.GetProperty("browser_download_url").GetString()!, 64 * 1024, cancellation, redirects: true);
        VerifyDigest(metadataAsset, metadataBytes);
        // PowerShell-generated metadata can contain a UTF-8 BOM.
        ReadOnlyMemory<byte> json = metadataBytes;
        if (metadataBytes.AsSpan().StartsWith(new byte[] { 239, 187, 191 })) json = json[3..];
        using var metadata = JsonDocument.Parse(json); var data = metadata.RootElement;
        if (data.GetProperty("schema_version").GetInt32() != 1 || data.GetProperty("product").GetString() != "RevitThyme"
            || data.GetProperty("version").GetString() != latest || data.GetProperty("platform").GetString() != "win-x64"
            || data.GetProperty("isolated_build").GetBoolean()) throw new InvalidDataException("Unsupported release metadata.");
        string[] years = data.GetProperty("supported_revit_years").EnumerateArray().Select(x => x.GetString() ?? "").ToArray();
        int comparison = latestNumber.CompareTo(ProductVersion.ParseStable(ProductVersion.Current));
        string status = !years.Contains(revitYear) ? "incompatible" : comparison > 0 ? "available" : comparison < 0 ? "ahead" : "current";
        string message = status switch
        {
            "available" => $"RevitThyme {latest} is available.",
            "ahead" => $"This build is ahead of the latest stable release ({latest}).",
            "incompatible" => $"The latest release ({latest}) does not support Revit {revitYear}.",
            _ => $"RevitThyme {ProductVersion.Current} is up to date."
        };
        return new(ProductVersion.Current, ProductVersion.Build, latest, status, message, new(latest, releaseUrl, years));
    }
    public static UpdateResult ForInstalled(UpdateResult release, InstalledToolkit installed)
    {
        if (installed.Status == "invalid") throw new InvalidOperationException(installed.Message);
        int comparison = installed.Version is null ? 1 : ProductVersion.ParseStable(release.LatestVersion).CompareTo(ProductVersion.ParseStable(installed.Version));
        string status = !release.Release.SupportedRevitYears.Contains("2027") ? "incompatible" : installed.Status == "missing" ? "not_installed"
            : comparison > 0 ? "available" : comparison < 0 ? "ahead" : "current";
        string message = status switch
        {
            "not_installed" => $"RevitThyme is not installed. Stable release {release.LatestVersion} is available to install.",
            "available" => $"Update available for installed RevitThyme: {installed.Version} → {release.LatestVersion}.",
            "ahead" => $"Installed RevitThyme {installed.Version} is ahead of stable release {release.LatestVersion}; no automatic downgrade.",
            "incompatible" => release.Message,
            _ => $"Installed RevitThyme {installed.Version} is up to date."
        };
        return release with { CurrentVersion = installed.Version ?? "", Build = installed.Build ?? "", Status = status, Message = message };
    }
    private static JsonElement Asset(JsonElement assets, string name, string tag)
    {
        var matches = assets.EnumerateArray().Where(x => x.GetProperty("name").GetString() == name).ToArray();
        if (matches.Length != 1 || matches[0].GetProperty("browser_download_url").GetString()
            != $"https://github.com/scwlkr/RevitThyme/releases/download/{tag}/{name}")
            throw new InvalidDataException("Missing or unexpected release asset: " + name);
        return matches[0];
    }
    private async Task<byte[]> Read(string url, int limit, CancellationToken cancellation, bool redirects = false)
    {
        for (int hop = 0; hop < 6; hop++)
        {
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellation);
            if (redirects && response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
            {
                var next = new Uri(new Uri(url), response.Headers.Location ?? throw new InvalidDataException("Missing redirect location."));
                if (next.Scheme != "https" || !next.IsDefaultPort || next.UserInfo.Length != 0 || next.Fragment.Length != 0
                    || next.Host != "release-assets.githubusercontent.com" || !next.AbsolutePath.StartsWith("/github-production-release-asset/", StringComparison.Ordinal))
                    throw new InvalidDataException("Unexpected release download redirect.");
                url = next.AbsoluteUri; continue;
            }
            if (response.StatusCode != HttpStatusCode.OK) throw new HttpRequestException($"Update check returned HTTP {(int)response.StatusCode}.");
            if (response.Content.Headers.ContentLength > limit) throw new InvalidDataException("Update response is too large.");
            await using var stream = await response.Content.ReadAsStreamAsync(cancellation);
            using var bytes = new MemoryStream(); byte[] buffer = new byte[8192]; int count;
            while ((count = await stream.ReadAsync(buffer, cancellation)) > 0)
            { if (bytes.Length + count > limit) throw new InvalidDataException("Update response is too large."); bytes.Write(buffer, 0, count); }
            return bytes.ToArray();
        }
        throw new InvalidDataException("Release download exceeded the redirect limit.");
    }
    private static void VerifyDigest(JsonElement asset, byte[] bytes)
    {
        if (!asset.TryGetProperty("digest", out var field) || field.ValueKind == JsonValueKind.Null) return;
        string digest = field.GetString() ?? "";
        string actual = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes));
        if (!string.Equals(digest, actual, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Release metadata SHA256 mismatch.");
    }
}
