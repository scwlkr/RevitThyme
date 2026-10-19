using System.IO;
using Autodesk.Revit.DB;
using IxMilia.Dxf;
using IxMilia.Dxf.Entities;
using RevitThyme.Core;

namespace RevitThyme.Addin;

// Reference diagrams may intentionally overlap/project to lines; fabrication's
// closed-ring/multiplicity gate remains separate and stricter.
internal static class NativeTimberKitReference
{
    internal sealed record CheckResult(string File, int ExpectedSegments, int NativeSegments, bool CoveragePassed);
    internal static CheckResult Check(Document doc, ImportInstance import, string path, CancellationToken ct)
    {
        var drawing = DxfFile.Load(path);
        if (drawing.Header.DefaultDrawingUnits != DxfUnits.Inches) throw new InvalidDataException("Reference drawing must retain inch units.");
        var expected = new List<NativeTimberKitSegment>(); var actual = new List<NativeTimberKitSegment>(); long work = 0;
        foreach (var entity in drawing.Entities)
        {
            ct.ThrowIfCancellationRequested();
            if (entity is not DxfLwPolyline { IsClosed: true } polyline || entity.Layer != "REFERENCE")
                throw new InvalidDataException("Reference input has unsupported entities/layers.");
            var points = polyline.Vertices.Select(p => new TimberPoint2(p.X, p.Y)).ToArray();
            for (int i = 0; i < points.Length; i++) Add(expected, points[i], points[(i + 1) % points.Length]);
        }
        Walk(import.get_Geometry(new Options()));
        if (expected.Count == 0 || actual.Count == 0 || !NativeTimberKitCoverage.Covers(expected, actual, ct, Step) || !NativeTimberKitCoverage.Covers(actual, expected, ct, Step))
            throw new InvalidDataException("Native reference CAD lost or added diagram geometry: " + Path.GetFileName(path));
        return new(Path.GetFileName(path), expected.Count, actual.Count, true);

        void Walk(GeometryElement geometry)
        {
            foreach (var item in geometry)
            {
                ct.ThrowIfCancellationRequested();
                if (item is GeometryInstance instance) { using var nested = instance.GetInstanceGeometry(); Walk(nested); continue; }
                if ((doc.GetElement(item.GraphicsStyleId) as GraphicsStyle)?.GraphicsStyleCategory.Name != "REFERENCE")
                    throw new InvalidDataException("Native guide import contains non-reference geometry.");
                if (item is PolyLine polyline)
                {
                    var points = polyline.GetCoordinates();
                    for (int i = 1; i < points.Count; i++) Add(actual, Convert(points[i - 1]), Convert(points[i]));
                }
                else if (item is Line line) Add(actual, Convert(line.GetEndPoint(0)), Convert(line.GetEndPoint(1)));
                else throw new InvalidDataException("Native reference CAD contains unexpected geometry.");
            }
        }
        TimberPoint2 Convert(XYZ p)
        {
            if (!double.IsFinite(p.X) || !double.IsFinite(p.Y) || !double.IsFinite(p.Z) || Math.Abs(p.Z * 12) > .000005)
                throw new InvalidDataException("Native reference geometry is not finite/planar.");
            return new(p.X * 12, p.Y * 12);
        }
        void Add(List<NativeTimberKitSegment> target, TimberPoint2 a, TimberPoint2 b)
        {
            if (!double.IsFinite(a.X) || !double.IsFinite(a.Y) || !double.IsFinite(b.X) || !double.IsFinite(b.Y))
                throw new InvalidDataException("Nonfinite reference coordinates.");
            if (NativeTimberKitCoverage.Length(new(a, b)) > 1e-12) target.Add(new(a, b));
            if (target.Count > 1000000) throw new InvalidDataException("Reference diagram exceeds the bounded segment limit.");
        }
        void Step()
        {
            if (++work > 20000000) throw new InvalidDataException("Reference comparison exceeds its bounded work limit.");
            if ((work & 127) == 0) ct.ThrowIfCancellationRequested();
        }
    }
}
