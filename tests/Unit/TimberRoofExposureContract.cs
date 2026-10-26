using System.Security.Cryptography;
using System.Text.Json;
using RevitThyme.Core;

namespace RevitThyme.Unit;

internal static class TimberRoofExposureContract
{
    public static void Run(Action<string, bool> check)
    {
        // Full wire-result characterizations complement the independently worked
        // geometry and area assertions in TimberFoldRoofs.
        (string File, string Hash)[] cases =
        [
            ("irregular-roof-envelope.json", "de3d4bc87c08f88717037f16f173f73cecac73017852d6222762381972b09421"),
            ("flat-roof-control.json", "5ee3383fbc1b6c59012662604fe712d50538d30e7d10c78fc824b150313821de"),
            ("gable-roof-control.json", "8f742902f571536c4f6c1002eb276c80b700dce27e039437c7ad1256ecf4b2bd"),
        ];
        foreach (var item in cases)
        {
            using var json = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", item.File)));
            var root = json.RootElement;
            var snapshot = root.GetProperty("snapshot").Deserialize<TimberSnapshot>(Protocol.Json) ?? throw new InvalidDataException("snapshot");
            var profile = root.GetProperty("profile").Deserialize<TimberFoldProfile>(Protocol.Json) ?? throw new InvalidDataException("profile");
            var scope = root.GetProperty("scope").Deserialize<TimberScope>(Protocol.Json) ?? throw new InvalidDataException("scope");
            var fit = TimberFabrication.Fit(snapshot, profile, scope);
            string hash = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(fit, Protocol.Json)));
            check("roof fitting preserves its complete geometry, ledger and diagnostics: " + item.File, hash == item.Hash);
        }
    }
}
