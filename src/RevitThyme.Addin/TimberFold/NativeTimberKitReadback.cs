using System.IO;
using Autodesk.Revit.DB;
using RevitThyme.Core;

namespace RevitThyme.Addin;

internal static class NativeTimberKitReadback
{
    internal sealed record RingMatch(string PanelId, int ContourIndex, int NativeVertices, double CanonicalPerimeterInches, double NativePerimeterInches);
    internal sealed record FileCheck(string File, string[] Layers, RingMatch[] Rings, double ToleranceInches);
    private sealed record Segment(TimberPoint2 A, TimberPoint2 B);
    private const double Tolerance = .00001;

    internal static FileCheck Check(Document doc, ImportInstance import, string file,
        NativeTimberKitContours.Ring[] expected, string layer, CancellationToken ct)
    {
        var rings = new List<TimberPoint2[]>(); var layers = new HashSet<string>(StringComparer.Ordinal);
        long work = 0; int pointCount = 0;
        Walk(import.get_Geometry(new Options()));
        if (rings.Count != expected.Length || layers.Count != 1 || !layers.Contains(layer))
            throw new InvalidDataException("Native CAD lost/added closed contours or changed cutting layers: " + file);
        var available = Enumerable.Range(0, rings.Count).ToHashSet(); var matches = new List<RingMatch>();
        foreach (var original in expected)
        {
            ct.ThrowIfCancellationRequested();
            var candidates = available.Where(i => Equivalent(original.Points, rings[i])).ToArray();
            if (candidates.Length != 1) throw new InvalidDataException($"Native CAD contour {original.PanelId}/{original.Index} has missing or ambiguous coverage: {file}");
            int index = candidates[0]; available.Remove(index);
            matches.Add(new(original.PanelId, original.Index, rings[index].Length, Perimeter(original.Points), Perimeter(rings[index])));
        }
        if (available.Count != 0) throw new InvalidDataException("Native CAD contains unmapped cutting contours.");
        return new(file, layers.Order(StringComparer.Ordinal).ToArray(), matches.ToArray(), Tolerance);

        void Walk(GeometryElement geometry)
        {
            foreach (var item in geometry)
            {
                ct.ThrowIfCancellationRequested();
                if (item is GeometryInstance instance) { using var nested = instance.GetInstanceGeometry(); Walk(nested); continue; }
                layers.Add((doc.GetElement(item.GraphicsStyleId) as GraphicsStyle)?.GraphicsStyleCategory.Name ?? "unresolved");
                if (item is not PolyLine polyline) throw new InvalidDataException("Native cutting CAD must contain closed polylines only.");
                var coordinates = polyline.GetCoordinates(); pointCount += coordinates.Count;
                if (pointCount > 1000000 || coordinates.Count < 4 || coordinates[0].DistanceTo(coordinates[^1]) * 12 > 1e-10)
                    throw new InvalidDataException("Native CAD polyline is open/empty or exceeds the bounded point limit.");
                if (coordinates.Any(p => !double.IsFinite(p.X) || !double.IsFinite(p.Y) || !double.IsFinite(p.Z) || Math.Abs(p.Z * 12) > Tolerance / 2))
                    throw new InvalidDataException("Native CAD has nonfinite/nonplanar cutting coordinates.");
                var ring = coordinates.Take(coordinates.Count - 1).Select(p => new TimberPoint2(p.X * 12, p.Y * 12)).ToArray();
                var segments = Segments(ring);
                if (segments.Any(s => Length(s) <= 1e-12)) throw new InvalidDataException("Native CAD contains a zero-length cut.");
                for (int i = 0; i < segments.Length; i++)
                for (int j = i + 1; j < segments.Length; j++)
                {
                    Budget();
                    var a = segments[i]; var b = segments[j]; double length = Length(a);
                    double x = Project(b.A, a, length), y = Project(b.B, a, length);
                    if (Distance(b.A, a, x, length) < 1e-10 && Distance(b.B, a, y, length) < 1e-10 &&
                        Math.Min(length, Math.Max(x, y)) - Math.Max(0, Math.Min(x, y)) > 1e-10)
                        throw new InvalidDataException("Native CAD repeats/backtracks a cutting segment.");
                }
                rings.Add(ring);
            }
        }
        bool Equivalent(TimberPoint2[] canonical, TimberPoint2[] actual)
        {
            Budget();
            return NativeTimberKitContourCheck.Check(canonical, actual, ct);
        }
        void Budget()
        {
            if (++work > 20000000) throw new InvalidDataException("Native contour comparison exceeds its bounded work limit.");
            if ((work & 127) == 0) ct.ThrowIfCancellationRequested();
        }
    }
    private static Segment[] Segments(TimberPoint2[] ring) => ring.Select((p, i) => new Segment(p, ring[(i + 1) % ring.Length])).ToArray();
    private static double Perimeter(TimberPoint2[] ring) => Segments(ring).Sum(Length);
    private static double Length(Segment s) => Math.Sqrt(Math.Pow(s.B.X - s.A.X, 2) + Math.Pow(s.B.Y - s.A.Y, 2));
    private static double Project(TimberPoint2 p, Segment s, double length) => ((p.X - s.A.X) * (s.B.X - s.A.X) + (p.Y - s.A.Y) * (s.B.Y - s.A.Y)) / length;
    private static double Distance(TimberPoint2 p, Segment s, double d, double length) => Math.Sqrt(
        Math.Pow(p.X - s.A.X - (s.B.X - s.A.X) * d / length, 2) + Math.Pow(p.Y - s.A.Y - (s.B.Y - s.A.Y) * d / length, 2));
}
