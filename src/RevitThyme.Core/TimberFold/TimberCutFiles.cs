using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;

namespace RevitThyme.Core;

public sealed record TimberCutFile(string Name, string Sha256, long Bytes);
public sealed record TimberCutPart(string PanelId, string SourceId, string FaceId, string File,
    int FirstContour, int ContourCount, TimberStockPlacement Placement);
public sealed record TimberCutStaging(string Directory, ImmutableArray<TimberCutFile> Files,
    ImmutableArray<TimberCutPart> Parts, ImmutableArray<TimberDiagnostic> Diagnostics)
{
    public bool Staged => Files.Length > 0 && Diagnostics.All(d => d.Severity != TimberSeverity.Error);
    public bool ManufacturingReady => false;
    public bool NativeImportOutstanding => true;
    public bool PhysicalChecksOutstanding => true;
}

public static class TimberCutFiles
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private sealed record Receipt(int Schema, string State, bool ManufacturingReady, bool NativeImportOutstanding,
        bool PhysicalChecksOutstanding, string Units, string Kerf, TimberFoldProfile Profile,
        ImmutableArray<TimberCutFile> Files, ImmutableArray<TimberCutPart> Parts, string[] CalibrationContours,
        ImmutableArray<TimberStockPlacement> CalibrationPlacements);

    public static TimberCutStaging Stage(string freshAbsoluteDirectory, IReadOnlyList<TimberPanel> panels,
        TimberStockPlan stock, TimberFoldProfile profile, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            string directory = Absolute(freshAbsoluteDirectory);
            var inventory = TimberCutInventory.Create(panels, stock, profile, cancellationToken);
            if (Directory.Exists(directory) || File.Exists(directory)) throw new IOException("Staging requires a fresh directory; existing outputs were preserved.");
            string parent = Path.GetDirectoryName(directory) ?? throw new InvalidDataException("Staging needs a parent directory.");
            if (!Directory.Exists(parent)) throw new DirectoryNotFoundException("The staging parent must already exist.");
            // Rename an owned sibling atomically: a competing existing destination
            // cannot be adopted or overwritten between the existence check and writes.
            string temporary = Path.Combine(parent, ".timberfold-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporary);
            try { Directory.Move(temporary, directory); }
            finally { if (Directory.Exists(temporary)) Directory.Delete(temporary); }
            foreach (var drawing in inventory.Drawings)
            {
                cancellationToken.ThrowIfCancellationRequested();
                WriteNew(Path.Combine(directory, drawing.Name), TimberDxfWriter.Write(drawing.Contours.Select(r => r.AsEnumerable())));
            }
            var files = Readback(directory, panels, inventory, cancellationToken);
            WriteNew(Path.Combine(directory, "cut-staging.json"), ReceiptBytes(profile, files, inventory));
            return new(directory, files.Add(Hash(Path.Combine(directory, "cut-staging.json"))), inventory.Parts, []);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or TimberCutFailure)
        { return Failed(freshAbsoluteDirectory, ex); }
    }

    public static TimberCutStaging Verify(string directory, IReadOnlyList<TimberPanel> panels,
        TimberStockPlan stock, TimberFoldProfile profile, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            directory = Absolute(directory);
            var inventory = TimberCutInventory.Create(panels, stock, profile, cancellationToken);
            var files = Readback(directory, panels, inventory, cancellationToken);
            string receipt = Path.Combine(directory, "cut-staging.json");
            if (!File.ReadAllBytes(receipt).SequenceEqual(ReceiptBytes(profile, files, inventory)))
                throw new InvalidDataException("Staging receipt differs from saved file hashes, profile or source/part mapping.");
            if (!Directory.EnumerateFileSystemEntries(directory).Select(Path.GetFileName).Order(StringComparer.Ordinal)
                .SequenceEqual(files.Select(f => f.Name).Append("cut-staging.json").Order(StringComparer.Ordinal)))
                throw new InvalidDataException("Unexpected or missing staging content.");
            return new(directory, files.Add(Hash(receipt)), inventory.Parts, []);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or TimberCutFailure)
        { return Failed(directory, ex); }
    }

    private static ImmutableArray<TimberCutFile> Readback(string directory, IReadOnlyList<TimberPanel> panels,
        TimberCutInventory inventory, CancellationToken ct)
    {
        var files = ImmutableArray.CreateBuilder<TimberCutFile>();
        foreach (var drawing in inventory.Drawings)
        {
            ct.ThrowIfCancellationRequested();
            var contours = new List<IReadOnlyList<TimberPoint2>>();
            if (drawing.Name == "CALIBRATION.dxf")
            {
                foreach (var coupon in inventory.Calibration.Coupons)
                    AddContours(coupon, inventory.Calibration.Layout.Placements.Single(p => p.PanelId == coupon.Id));
            }
            else foreach (var part in inventory.Parts.Where(p => p.File == drawing.Name))
            {
                AddContours(panels.Single(p => p.Id == part.PanelId), part.Placement);
            }
            string path = Path.Combine(directory, drawing.Name);
            TimberDxfReadback.Check(path, contours, ct, drawing.Name == "CALIBRATION.dxf");
            files.Add(Hash(path));

            void AddContours(TimberPanel panel, TimberStockPlacement placement)
            {
                // Independent canonical transform; never consumes writer rings/Project.
                foreach (var ring in panel.RingsInches)
                    contours.Add(ring.Select(p => placement.QuarterTurn
                        ? new TimberPoint2(placement.XInches + placement.LocalMaximum.Y - p.Y, placement.YInches + p.X - placement.LocalMinimum.X)
                        : new TimberPoint2(placement.XInches + p.X - placement.LocalMinimum.X, placement.YInches + p.Y - placement.LocalMinimum.Y)).ToArray());
            }
        }
        return files.ToImmutable();
    }

    private static byte[] ReceiptBytes(TimberFoldProfile profile, ImmutableArray<TimberCutFile> files, TimberCutInventory inventory) =>
        JsonSerializer.SerializeToUtf8Bytes(new Receipt(1, "Staged", false, true, true, "inches", "Nominal contours; compensation belongs to laser software.",
            profile, files, inventory.Parts, ["1 x 1 inch square", "1 inch by minimum-bar coupon", "Corner width = thickness + joint clearance; arms = max(1 inch, 2 x width)"],
            inventory.Calibration.Coupons.Select(c => inventory.Calibration.Layout.Placements.Single(p => p.PanelId == c.Id)).ToImmutableArray()), Json);
    private static TimberCutFile Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return new(Path.GetFileName(path), Convert.ToHexStringLower(SHA256.HashData(stream)), stream.Length);
    }
    private static void WriteNew(string path, byte[] bytes)
    { using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None); stream.Write(bytes); }
    private static string Absolute(string directory)
    {
        if (!Path.IsPathFullyQualified(directory)) throw new InvalidDataException("Staging needs an absolute path.");
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
    }
    private static TimberCutStaging Failed(string directory, Exception error) =>
        new(directory, [], [], [new(error is TimberCutFailure failure ? failure.Code : "cut_files", TimberSeverity.Error, error.Message)]);
}
