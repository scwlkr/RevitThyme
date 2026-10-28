using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using RevitThyme.Addin;
using RevitThyme.Core;

namespace RevitThyme.Unit;

internal static partial class NativeTimberKitInputs
{
    private const string SourceError = "Source evidence differs from the verified plan.";
    private const string CuttingError = "Canonical cutting files failed independent verification.";
    private const string PanelError = "Every native part label must map to the exact source panel and face.";

    private static void CheckBoundaries(Action<string, bool> check, Fixture fixture)
    {
        var plan = fixture.Plan; var request = fixture.Request(plan);
        byte[] bounded = new byte[134217728]; Array.Fill(bounded, (byte)' '); "null"u8.CopyTo(bounded);
        Reject(check, "exact 128 MiB plan reaches JSON validation", () => fixture.Read(request with { PlanJson = ImmutableCollectionsMarshal.AsImmutableArray(bounded) }), "Native kit plan is missing.");
        byte[] excessive = new byte[134217729];
        Reject(check, "over 128 MiB plan", () => fixture.Read(request with { PlanJson = ImmutableCollectionsMarshal.AsImmutableArray(excessive) }), RequestError);
        var panel = plan.Fitted.Draft.Panels[0] with { RingsInches = [], Triangles = [] };
        var boundaryPlan = plan with { SourceFingerprint = new string('a', 64), Fitted = plan.Fitted with { Draft = plan.Fitted.Draft with { Panels = [.. Enumerable.Repeat(panel, 10000)] } } };
        Reject(check, "exact 10000 panels reaches source validation", () => fixture.Read(fixture.Request(boundaryPlan)), SourceError);
        Reject(check, "over 10000 panels", () => fixture.Read(fixture.Request(boundaryPlan with { Fitted = boundaryPlan.Fitted with { Draft = boundaryPlan.Fitted.Draft with { Panels = boundaryPlan.Fitted.Draft.Panels.Add(panel) } } })), PlanError);
        var source = JsonSerializer.Deserialize<TimberSnapshot>(plan.SourceSnapshotJson.AsSpan(), Protocol.Json) ?? throw new InvalidDataException("source");
        Reject(check, "independent source target", () => fixture.Read(fixture.Request(plan with { SourceSnapshotJson = [.. JsonSerializer.SerializeToUtf8Bytes(source with { Target = source.Target with { Revision = source.Target.Revision + 1 } }, Protocol.Json)] })), SourceError);
        Reject(check, "independent source diagnostics", () => fixture.Read(fixture.Request(plan with { Scope = plan.Scope with { PhaseId = "absent-phase" } })), SourceError);
        Reject(check, "missing seal digest", () => fixture.Read(request with { InputSealSha256 = null }), "Verified generation input seal changed before native execution or assimilation.");
        check("label text and height accept their exact upper bounds", fixture.Read(fixture.Request(plan with { Guides = fixture.Label(l => l with { Text = new string('x', 16384), TextHeightInches = 2 }) })).RunId == fixture.RunId);
        Reject(check, "drawing absent from verified files", () => fixture.Read(fixture.Request(plan with { Guides = plan.Guides with { Drawings = plan.Guides.Drawings.Add(plan.Guides.Drawings[0] with { File = "absent.dxf" }) } })), GuideError);
        foreach (var (name, guides) in new (string, TimberGuideStaging)[]
        {
            ("source mapping", fixture.Label(l => l with { SourceId = "absent" })),
            ("face mapping", fixture.Label(l => l with { FaceId = "absent" })),
            ("missing mapped label without orphan", plan.Guides with { Labels = [.. plan.Guides.Labels.Select(l => l.PanelId == panel.Id ? l with { PanelId = null } : l)] }),
            ("extra orphan without missing label", plan.Guides with { Labels = plan.Guides.Labels.Add(plan.Guides.Labels[0] with { PanelId = "orphan" }) })
        }) Reject(check, name, () => fixture.Read(fixture.Request(plan with { Guides = guides })), PanelError);
        GuideBounds(check, fixture);
        CuttingBounds(check, fixture);
        ReparseBoundaries(check, fixture);
        string durable = Path.Combine(fixture.Directory, "durable.json");
        ObservedFile? observed = null;
        NativeTimberKitFiles.WriteNew(durable, new { Value = "durable" }, path => observed = new ObservedFile(path));
        check("new evidence requests an OS disk flush before returning", observed is { FlushedToDisk: true });
    }

    private static void GuideBounds(Action<string, bool> check, Fixture fixture)
    {
        var guides = fixture.Plan.Guides;
        var files = guides.Files.ToBuilder(); var drawings = guides.Drawings.ToBuilder(); var extras = new List<string>();
        try
        {
            while (drawings.Count < 1000)
            {
                string name = "boundary-" + drawings.Count + ".dxf";
                string path = Path.Combine(guides.Directory, name); File.WriteAllBytes(path, [42]); extras.Add(path);
                files.Add(new(name, Convert.ToHexStringLower(SHA256.HashData(new byte[] { 42 })), 1));
                drawings.Add(guides.Drawings[0] with { File = name });
            }
            var labels = guides.Labels.AddRange(Enumerable.Repeat(guides.Labels[0], 100000 - guides.Labels.Length));
            var boundary = guides with { Files = files.ToImmutable(), Drawings = drawings.ToImmutable(), Labels = labels };
            check("exact 1000 drawings and 100000 labels pass the native inventory boundary", fixture.Read(fixture.Request(fixture.Plan with { Guides = boundary })).Guides.Labels.Length == 100000);
            Reject(check, "over 1000 drawings", () => fixture.Read(fixture.Request(fixture.Plan with { Guides = boundary with { Drawings = boundary.Drawings.Add(guides.Drawings[0] with { File = "beyond.dxf" }) } })), GuideError);
            Reject(check, "over 100000 labels", () => fixture.Read(fixture.Request(fixture.Plan with { Guides = boundary with { Labels = boundary.Labels.Add(labels[0]) } })), GuideError);
        }
        finally { foreach (string path in extras) File.Delete(path); }
        string oldName = guides.Drawings[0].File, newName = Path.ChangeExtension(oldName, ".DXF");
        string oldPath = Path.Combine(guides.Directory, oldName), newPath = Path.Combine(guides.Directory, newName);
        File.Move(oldPath, newPath);
        try
        {
            var uppercase = guides with
            {
                Files = [.. guides.Files.Select(f => f.Name == oldName ? f with { Name = newName } : f)],
                Drawings = [.. guides.Drawings.Select(d => d.File == oldName ? d with { File = newName } : d)],
                Labels = [.. guides.Labels.Select(l => l.GuideFile == oldName ? l with { GuideFile = newName } : l)]
            };
            Reject(check, "uppercase DXF suffix", () => fixture.Read(fixture.Request(fixture.Plan with { Guides = uppercase })), GuideError);
        }
        finally { File.Move(newPath, oldPath); }
    }

    private static void CuttingBounds(Action<string, bool> check, Fixture fixture)
    {
        var plan = fixture.Plan; var first = plan.CutFiles[0];
        Reject(check, "reordered canonical cut inventory", () => fixture.Read(fixture.Request(plan with { CutFiles = [.. plan.CutFiles.Reverse()] })), CuttingError);
        Reject(check, "duplicate declared file", () => fixture.Read(fixture.Request(plan with { CutFiles = plan.CutFiles.Add(first) })), "Native input file inventory differs from its verified plan.");
        foreach (var (name, file, error) in new (string, TimberCutFile, string)[]
        {
            ("embedded path", first with { Name = "sub/" + first.Name }, "Native input file inventory differs from its verified plan."),
            ("invalid filename", first with { Name = "invalid?" }, "Native input file inventory differs from its verified plan."),
            ("zero declared bytes", first with { Bytes = 0 }, "Invalid native input file metadata."),
            ("excessive declared bytes", first with { Bytes = 134217729 }, "Invalid native input file metadata."),
            ("exact byte limit reaches length validation", first with { Bytes = 134217728 }, "Native input changed: " + first.Name),
            ("length drift", first with { Bytes = first.Bytes + 1 }, "Native input changed: " + first.Name)
        }) Reject(check, name, () => fixture.Read(fixture.Request(plan with { CutFiles = plan.CutFiles.SetItem(0, file) })), error);
        string path = Path.Combine(fixture.Directory, "cutting", first.Name); byte[] saved = File.ReadAllBytes(path);
        try
        {
            File.WriteAllBytes(path, [42]);
            var changed = first with { Bytes = 1, Sha256 = NativeTimberKitFiles.Hash(path) };
            Reject(check, "resealed one-byte noncanonical cutting file", () => fixture.Read(fixture.Request(plan with { CutFiles = plan.CutFiles.SetItem(0, changed) })), CuttingError);
        }
        finally { File.WriteAllBytes(path, saved); }
    }

    private static void ReparseBoundaries(Action<string, bool> check, Fixture fixture)
    {
        string target = Path.Combine(fixture.Directory, "junction-target"); Directory.CreateDirectory(target);
        string alias = Path.Combine(fixture.Directory, "alias");
        Junction(alias, target);
        try { Reject(check, "run directory junction", () => NativeTimberKitFiles.RequirePlain(alias), "Native run paths cannot use reparse points."); }
        finally { Directory.Delete(alias); }
        string seal = Path.Combine(fixture.Directory, "generation-inputs.json"); byte[] sealBytes = File.ReadAllBytes(seal);
        File.Delete(seal); Junction(seal, target);
        try { Reject(check, "input seal junction", () => fixture.Read(fixture.Request(fixture.Plan)), "Verified generation input seal changed before native execution or assimilation."); }
        finally { Directory.Delete(seal); File.WriteAllBytes(seal, sealBytes); }
        string cut = Path.Combine(fixture.Directory, "cutting", fixture.Plan.CutFiles[0].Name); byte[] cutBytes = File.ReadAllBytes(cut);
        File.Delete(cut); Junction(cut, target);
        try { Reject(check, "cut input junction", () => fixture.Read(fixture.Request(fixture.Plan)), "Native input changed: " + fixture.Plan.CutFiles[0].Name); }
        finally { Directory.Delete(cut); File.WriteAllBytes(cut, cutBytes); }
    }

    private static void Junction(string path, string target)
    {
        var start = new System.Diagnostics.ProcessStartInfo("cmd.exe")
        {
            Arguments = $"/c mklink /J \"{path}\" \"{target}\"", UseShellExecute = false,
            CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        using var process = System.Diagnostics.Process.Start(start) ?? throw new IOException("junction process missing");
        string output = process.StandardOutput.ReadToEnd(); string error = process.StandardError.ReadToEnd(); process.WaitForExit();
        if (process.ExitCode != 0 || (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0)
            throw new IOException("Fixture junction creation failed: " + output + error);
    }

    private sealed class ObservedFile(string path) : FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read)
    {
        internal bool FlushedToDisk { get; private set; }
        public override void Flush(bool flushToDisk) { FlushedToDisk = flushToDisk; base.Flush(flushToDisk); }
    }
}
