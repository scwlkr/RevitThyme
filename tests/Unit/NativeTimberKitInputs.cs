using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using RevitThyme.Addin;
using RevitThyme.Core;

namespace RevitThyme.Unit;

internal static class NativeTimberKitInputs
{
    private const string RequestError = "Native finalization requires its exact host-owned run directory and bounded plan.";
    private const string PlanError = "Native kit plan is incomplete, unverified or differs from its request.";
    private const string GuideError = "Native guide inventory or labels are invalid.";

    internal static void Run(Action<string, bool> check)
    {
        using var fixture = new Fixture();
        var request = fixture.Request(fixture.Plan);
        var result = fixture.Read(request);
        check("real fitted plan, cutting readback and guide inputs survive native input validation",
            JsonSerializer.Serialize(result, Protocol.Json) == JsonSerializer.Serialize(fixture.Plan, Protocol.Json));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        bool cancellation = false;
        try { NativeTimberKitFiles.Read(request with { RunId = Guid.Empty }, cancelled.Token, _ => throw new Exception("early resolver")); }
        catch (OperationCanceledException) { cancellation = true; }
        check("cancellation precedes request validation and directory resolution", cancellation);
        foreach (var (name, invalid) in new (string, TimberNativeKitRequest)[]
        {
            ("empty run", request with { RunId = Guid.Empty }),
            ("empty plan", request with { PlanJson = [] }),
            ("relative directory", request with { Directory = "relative" }),
            ("another directory", request with { Directory = Path.GetTempPath() })
        }) Reject(check, name, () => fixture.Read(invalid), RequestError);
        Reject(check, "missing plan", () => fixture.Read(request with { PlanJson = [.. "null"u8] }), "Native kit plan is missing.");
        Reject(check, "changed seal", () => fixture.Read(request with { InputSealSha256 = new string('0', 64) }),
            "Verified generation input seal changed before native execution or assimilation.");
        foreach (var (name, invalid) in new (string, TimberNativeKitPlan)[]
        {
            ("schema", fixture.Plan with { Schema = 2 }),
            ("run identity", fixture.Plan with { RunId = Guid.NewGuid() }),
            ("preview identity", fixture.Plan with { PreviewRunId = Guid.Empty }),
            ("source target", fixture.Plan with { SourceContext = fixture.Plan.SourceContext with { Target = fixture.Plan.SourceContext.Target with { Revision = 100 } } }),
            ("source bytes", fixture.Plan with { SourceSnapshotJson = [] }),
            ("fingerprint length", fixture.Plan with { SourceFingerprint = "short" }),
            ("profile", fixture.Plan with { Profile = fixture.Plan.Profile with { ScaleDenominator = 0 } }),
            ("fit state", fixture.Plan with { Fitted = fixture.Plan.Fitted with { State = TimberFitState.Blocked } }),
            ("cut inventory", fixture.Plan with { CutFiles = [] }),
            ("panel inventory", fixture.Plan with { Fitted = fixture.Plan.Fitted with { Draft = fixture.Plan.Fitted.Draft with { Panels = [] } } })
        }) Reject(check, name, () => fixture.Read(fixture.Request(invalid)), PlanError);
        Reject(check, "missing original source", () => fixture.Read(fixture.Request(fixture.Plan with { SourceSnapshotJson = [.. "null"u8] })), "Original source snapshot is missing.");
        Reject(check, "source fingerprint differs", () => fixture.Read(fixture.Request(fixture.Plan with { SourceFingerprint = new string('a', 64) })), "Source evidence differs from the verified plan.");
        Reject(check, "guide directory differs", () => fixture.Read(fixture.Request(fixture.Plan with { Guides = fixture.Plan.Guides with { Directory = Path.GetTempPath() } })), "Guide inputs must belong to this generation directory.");
        foreach (var (name, guides) in new (string, TimberGuideStaging)[]
        {
            ("missing drawings", fixture.Plan.Guides with { Drawings = [] }),
            ("missing labels", fixture.Plan.Guides with { Labels = [] }),
            ("duplicate drawings", fixture.Plan.Guides with { Drawings = [.. fixture.Plan.Guides.Drawings, fixture.Plan.Guides.Drawings[0]] }),
            ("blank text", fixture.Label(l => l with { Text = " " })),
            ("long text", fixture.Label(l => l with { Text = new string('x', 16385) })),
            ("zero height", fixture.Label(l => l with { TextHeightInches = 0 })),
            ("excessive height", fixture.Label(l => l with { TextHeightInches = 2.001 })),
            ("unmapped drawing", fixture.Label(l => l with { GuideFile = "absent.dxf" }))
        }) Reject(check, name, () => fixture.Read(fixture.Request(fixture.Plan with { Guides = guides })), GuideError);
        Reject(check, "missing panel label", () => fixture.Read(fixture.Request(fixture.Plan with { Guides = fixture.Label(l => l with { PanelId = "absent" }) })),
            "Every native part label must map to the exact source panel and face.");
        CheckFileHelpers(check, fixture.Directory);
        string extra = Path.Combine(fixture.Directory, "cutting", "unexpected.txt");
        File.WriteAllText(extra, "unplanned");
        Reject(check, "extra file", () => fixture.Read(request), "Native input file inventory differs from its verified plan.");
        File.Delete(extra);
        string cut = Path.Combine(fixture.Directory, "cutting", fixture.Plan.CutFiles[0].Name);
        var bytes = File.ReadAllBytes(cut); bytes[0] ^= 1; File.WriteAllBytes(cut, bytes);
        Reject(check, "same-length cutting tamper", () => fixture.Read(request), "Native input changed: " + fixture.Plan.CutFiles[0].Name);
    }

    private static void CheckFileHelpers(Action<string, bool> check, string directory)
    {
        string path = Path.Combine(directory, "one-byte"); File.WriteAllBytes(path, [42]);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        check("hash consumes a one-byte final block and terminates at EOF",
            NativeTimberKitFiles.Hash(path, timeout.Token) == Convert.ToHexStringLower(SHA256.HashData(new byte[] { 42 })));
        check("file evidence records relative slash-normalized name, bytes, digest and role",
            NativeTimberKitFiles.File(directory, path, "input") == new TimberNativeFile("one-byte", NativeTimberKitFiles.Hash(path), 1, "input"));
        string fresh = NativeTimberKitFiles.FreshDirectory(directory, "native");
        check("fresh native output directory starts empty", System.IO.Directory.Exists(fresh) && !System.IO.Directory.EnumerateFileSystemEntries(fresh).Any());
        bool refused = false;
        try { NativeTimberKitFiles.FreshDirectory(directory, "native"); } catch (IOException error) { refused = error.Message.Contains("recover the original generation", StringComparison.Ordinal); }
        check("existing output directory refuses retry", refused);
        refused = false;
        try { NativeTimberKitFiles.FreshDirectory(directory, "one-byte"); } catch (IOException error) { refused = error.Message.Contains("recover the original generation", StringComparison.Ordinal); }
        check("existing file refuses output-directory replacement", refused && File.ReadAllBytes(path).SequenceEqual(new byte[] { 42 }));
        string json = Path.Combine(directory, "written.json");
        string digest = NativeTimberKitFiles.WriteNew(json, new { Value = "persisted" });
        check("new JSON evidence returns digest of persisted bytes", digest == Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(json))) && File.ReadAllText(json).Contains("persisted", StringComparison.Ordinal));
        refused = false;
        try { NativeTimberKitFiles.WriteNew(json, new { Value = "replacement" }); } catch (IOException) { refused = true; }
        check("JSON evidence cannot overwrite existing bytes", refused && NativeTimberKitFiles.Hash(json) == digest);
    }

    private static void Reject(Action<string, bool> check, string name, Action action, string message)
    {
        bool rejected = false;
        try { action(); } catch (InvalidDataException error) { rejected = error.Message == message; }
        check("native inputs reject " + name + " with the original diagnostic", rejected);
    }

    private sealed class Fixture : IDisposable
    {
        internal string Directory { get; } = Path.Combine(Path.GetTempPath(), "revitthyme-native-inputs-" + Guid.NewGuid().ToString("N"));
        internal Guid RunId { get; } = Guid.NewGuid();
        internal TimberNativeKitPlan Plan { get; }
        private readonly string seal;
        internal Fixture()
        {
            using var json = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "flat-roof-control.json")));
            var root = json.RootElement;
            var source = root.GetProperty("snapshot").Deserialize<TimberSnapshot>(Protocol.Json) ?? throw new InvalidDataException("source fixture");
            var profile = root.GetProperty("profile").Deserialize<TimberFoldProfile>(Protocol.Json) ?? throw new InvalidDataException("profile fixture");
            var scope = root.GetProperty("scope").Deserialize<TimberScope>(Protocol.Json) ?? throw new InvalidDataException("scope fixture");
            var fitted = TimberFabrication.Fit(source, profile, scope);
            var stock = TimberStockLayout.Create(fitted.Draft.Panels, profile);
            System.IO.Directory.CreateDirectory(Directory);
            var cutting = TimberCutFiles.Stage(Path.Combine(Directory, "cutting"), fitted.Draft.Panels, stock, profile);
            var guides = TimberGuides.Stage(Path.Combine(Directory, "guides"), fitted, stock, profile);
            var context = new Snapshot(source.Target, "fixture", "fixture", "", false, false, "fixture", "fixture", 1, false, null);
            Plan = new(1, RunId, Guid.NewGuid(), context, source.DocumentFingerprint, profile, scope,
                [.. JsonSerializer.SerializeToUtf8Bytes(source, Protocol.Json)], fitted, stock, guides, cutting.Files);
            // This boundary authenticates bytes of the seal; the generation coordinator owns its contents.
            byte[] sealBytes = "{\"schema\":1,\"fixture\":true}"u8.ToArray();
            File.WriteAllBytes(Path.Combine(Directory, "generation-inputs.json"), sealBytes);
            seal = Convert.ToHexStringLower(SHA256.HashData(sealBytes));
        }
        internal TimberGuideStaging Label(Func<TimberGuideLabel, TimberGuideLabel> change) =>
            Plan.Guides with { Labels = Plan.Guides.Labels.SetItem(0, change(Plan.Guides.Labels[0])) };
        internal TimberNativeKitRequest Request(TimberNativeKitPlan plan) => new(RunId, Plan.SourceContext.Target, Directory,
            [.. JsonSerializer.SerializeToUtf8Bytes(plan, Protocol.Json)], seal);
        internal TimberNativeKitPlan Read(TimberNativeKitRequest request) => NativeTimberKitFiles.Read(request, default, _ => Directory);
        public void Dispose() => System.IO.Directory.Delete(Directory, true);
    }
}
