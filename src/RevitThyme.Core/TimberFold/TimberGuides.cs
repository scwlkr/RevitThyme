using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;

namespace RevitThyme.Core;

public sealed record TimberGuideLabel(string GuideFile, string? PanelId, string? SourceId, string? FaceId,
    TimberPoint2 PhysicalPositionInches, string Text, double TextHeightInches = 0.125);
public sealed record TimberGuideDrawing(string File, string Role, string Title, TimberPoint2 MinimumInches,
    TimberPoint2 MaximumInches, ImmutableArray<string> PanelIds);
public sealed record TimberGuideStaging(string Directory, ImmutableArray<TimberCutFile> Files,
    ImmutableArray<TimberGuideLabel> Labels, ImmutableArray<TimberGuideDrawing> Drawings,
    ImmutableArray<TimberDiagnostic> Diagnostics)
{
    public bool Staged => Files.Length > 0 && Diagnostics.All(d => d.Severity != TimberSeverity.Error);
    public bool ManufacturingReady => false;
    public bool PhysicalChecksOutstanding => true;
}

public static class TimberGuides
{
    private sealed record Receipt(int Schema, string State, bool ManufacturingReady, bool PhysicalChecksOutstanding,
        string Units, string Kerf, TimberFoldProfile Profile, ImmutableArray<TimberCutFile> Files,
        ImmutableArray<TimberGuideDrawing> Drawings, ImmutableArray<TimberGuideLabel> Labels,
        ImmutableArray<TimberFacePartMap> FaceMap, ImmutableArray<TimberOpeningPartMap> OpeningMap,
        ImmutableArray<TimberJointRecord> Joints, ImmutableArray<TimberRoofExposureRecord> RoofExposure,
        ImmutableArray<TimberWallEnvelopeRecord> WallEnvelope);

    public static TimberGuideStaging Stage(string freshAbsoluteDirectory, TimberFittedDraft fitted,
        TimberStockPlan stock, TimberFoldProfile profile, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Path.IsPathFullyQualified(freshAbsoluteDirectory)) throw new InvalidDataException("Guide staging needs an absolute path.");
            string directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(freshAbsoluteDirectory));
            var inventory = TimberGuideInventory.Create(fitted, stock, profile, cancellationToken);
            if (Directory.Exists(directory) || File.Exists(directory)) throw new IOException("Guide staging requires a fresh directory; previous output was preserved.");
            string parent = Path.GetDirectoryName(directory) ?? throw new InvalidDataException("Guide staging needs a parent.");
            if (!Directory.Exists(parent)) throw new DirectoryNotFoundException("The guide staging parent must already exist.");
            string temporary = Path.Combine(parent, ".timberfold-guides-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporary);
            try { Directory.Move(temporary, directory); }
            finally { if (Directory.Exists(temporary)) Directory.Delete(temporary); }
            var files = ImmutableArray.CreateBuilder<TimberCutFile>();
            foreach (var vector in inventory.Vectors)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Save(vector.Drawing.File, TimberDxfWriter.Write(vector.Contours.Select(r => r.AsEnumerable()), "REFERENCE"));
            }
            Save("GUIDE.svg", TimberGuideSvg.Write(inventory, profile, cancellationToken));
            var drawings = inventory.Vectors.Select(v => v.Drawing).ToImmutableArray();
            var labels = inventory.Vectors.SelectMany(v => v.Labels).ToImmutableArray();
            var receipt = new Receipt(2, "ReferenceStaged", false, true, "inches",
                "Nominal contours; compensation belongs to laser software.", profile, files.ToImmutable(), drawings, labels,
                fitted.Draft.FaceMap.OrderBy(m => m.SourceId, StringComparer.Ordinal).ThenBy(m => m.FaceId, StringComparer.Ordinal)
                    .Select(m => m with { PanelIds = m.PanelIds.Order(StringComparer.Ordinal).ToImmutableArray() }).ToImmutableArray(),
                fitted.Draft.OpeningMap.OrderBy(m => m.SourceId, StringComparer.Ordinal).ThenBy(m => m.OpeningId, StringComparer.Ordinal)
                    .Select(m => m with { PanelIds = m.PanelIds.Order(StringComparer.Ordinal).ToImmutableArray() }).ToImmutableArray(),
                fitted.Joints.OrderBy(j => j.OwnerPanelId, StringComparer.Ordinal).ThenBy(j => j.ReceiverPanelId, StringComparer.Ordinal).ToImmutableArray(),
                fitted.RoofExposure.OrderBy(e => e.PanelId, StringComparer.Ordinal).ToImmutableArray(),
                fitted.WallEnvelope.OrderBy(e => e.PanelId, StringComparer.Ordinal).ToImmutableArray());
            Save("guide-staging.json", JsonSerializer.SerializeToUtf8Bytes(receipt, new JsonSerializerOptions { WriteIndented = true }));
            return new(directory, files.ToImmutable(), labels, drawings, []);

            void Save(string name, byte[] bytes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string path = Path.Combine(directory, name);
                using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) stream.Write(bytes);
                using var saved = File.OpenRead(path);
                files.Add(new(name, Convert.ToHexStringLower(SHA256.HashData(saved)), saved.Length));
            }
        }
        catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException or UnauthorizedAccessException)
        { return new(freshAbsoluteDirectory, [], [], [], [new("guide_files", TimberSeverity.Error, error.Message)]); }
    }
}
