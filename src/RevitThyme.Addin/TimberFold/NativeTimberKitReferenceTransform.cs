using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Text;
using RevitThyme.Core;

namespace RevitThyme.Addin;

public sealed record NativeTimberReferenceLabel(TimberGuideLabel Source, TimberPoint3 PositionFeet);
public sealed record NativeTimberReferencePrepared(ImmutableArray<byte> Dxf, ImmutableArray<NativeTimberReferenceLabel> Labels,
    int CoordinateMultiplier, int ViewScale);

/// <summary>Owned native-reference input planning; physical guides and cutting inputs are unchanged.</summary>
public static class NativeTimberKitReferenceTransform
{
    public const int Multiplier = 48;
    public static NativeTimberReferencePrepared Prepare(ReadOnlySpan<byte> physicalDxf, IReadOnlyList<TimberGuideLabel> labels,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (physicalDxf.IsEmpty || physicalDxf.Length > 16777216) throw new InvalidDataException("Native reference input exceeds the bounded ASCII DXF size.");
        foreach (byte value in physicalDxf) if (value > 127) throw new InvalidDataException("Native reference planning requires the verified ASCII DXF dialect.");
        var lines = Encoding.ASCII.GetString(physicalDxf).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        if (lines[^1].Length == 0) lines = lines[..^1];
        if (lines.Length % 2 != 0) throw new InvalidDataException("Reference DXF group pairs are incomplete.");
        bool entities = false, foundEntities = false, inches = false;
        for (int i = 0; i < lines.Length; i += 2)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int code = int.Parse(lines[i], CultureInfo.InvariantCulture); string value = lines[i + 1];
            if (code == 9 && value == "$INSUNITS" && i + 3 < lines.Length)
                inches = int.Parse(lines[i + 2], CultureInfo.InvariantCulture) == 70 && int.Parse(lines[i + 3], CultureInfo.InvariantCulture) == 1;
            if (code == 2 && value == "ENTITIES") { entities = true; foundEntities = true; }
            if (code == 0 && value == "ENDSEC") entities = false;
            if (!entities) continue;
            if (code == 0 && value != "LWPOLYLINE" || code == 8 && value != "REFERENCE")
                throw new InvalidDataException("Only verified REFERENCE polylines belong in native review inputs.");
            if (code is not (10 or 20)) continue;
            double coordinate = double.Parse(value, CultureInfo.InvariantCulture) * Multiplier;
            if (!double.IsFinite(coordinate)) throw new InvalidDataException("Native reference coordinate is not finite.");
            lines[i + 1] = coordinate.ToString("R", CultureInfo.InvariantCulture);
        }
        if (!foundEntities || !inches) throw new InvalidDataException("Native reference inputs must retain explicit inch metadata and entities.");
        var nativeLabels = ImmutableArray.CreateBuilder<NativeTimberReferenceLabel>();
        foreach (var label in labels)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var position = new TimberPoint3(label.PhysicalPositionInches.X * Multiplier / 12, label.PhysicalPositionInches.Y * Multiplier / 12, 0);
            if (!double.IsFinite(position.X) || !double.IsFinite(position.Y)) throw new InvalidDataException("Native reference label is not finite.");
            nativeLabels.Add(new(label, position));
        }
        return new(Encoding.ASCII.GetBytes(string.Join("\r\n", lines) + "\r\n").ToImmutableArray(), nativeLabels.ToImmutable(), Multiplier, Multiplier);
    }
}
