using System.IO;
using RevitThyme.Core;

namespace RevitThyme.Addin;

/// <summary>Independent native-consumer comparison of one canonical and one native closed ring.</summary>
public static class NativeTimberKitContourCheck
{
    private const double Tolerance = .00001;
    public static bool Check(IReadOnlyList<TimberPoint2> canonical, IReadOnlyList<TimberPoint2> native, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (canonical.Count < 3 || native.Count < 3 || canonical.Concat(native).Any(p => !double.IsFinite(p.X) || !double.IsFinite(p.Y))) return false;
        if (Math.Abs(canonical.Min(p => p.X) - native.Min(p => p.X)) > Tolerance ||
            Math.Abs(canonical.Max(p => p.X) - native.Max(p => p.X)) > Tolerance ||
            Math.Abs(canonical.Min(p => p.Y) - native.Min(p => p.Y)) > Tolerance ||
            Math.Abs(canonical.Max(p => p.Y) - native.Max(p => p.Y)) > Tolerance) return false;
        long work = 0;
        if (!NativeTimberKitTopology.IsSimple(canonical, cancellationToken, Budget) || !NativeTimberKitTopology.IsSimple(native, cancellationToken, Budget)) return false;
        var a = Segments(canonical); var b = Segments(native);
        return NativeTimberKitCoverage.Covers(a, b, cancellationToken, Budget) && NativeTimberKitCoverage.Covers(b, a, cancellationToken, Budget) &&
            Math.Abs(a.Sum(NativeTimberKitCoverage.Length) - b.Sum(NativeTimberKitCoverage.Length)) <= Tolerance * (a.Length + b.Length);
        void Budget()
        {
            if (++work > 20000000) throw new InvalidDataException("Native contour comparison exceeds its bounded work limit.");
            if ((work & 127) == 0) cancellationToken.ThrowIfCancellationRequested();
        }
    }
    private static NativeTimberKitSegment[] Segments(IReadOnlyList<TimberPoint2> ring) => ring.Select((p, i) => new NativeTimberKitSegment(p, ring[(i + 1) % ring.Count])).ToArray();
}
