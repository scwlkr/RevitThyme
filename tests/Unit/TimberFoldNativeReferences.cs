using System.Text;
using IxMilia.Dxf;
using IxMilia.Dxf.Entities;
using RevitThyme.Addin;
using RevitThyme.Core;

namespace RevitThyme.Unit;

internal static class TimberFoldNativeReferences
{
    public static void Run(Action<string, bool> check)
    {
        byte[] input = Encoding.ASCII.GetBytes("0\r\nSECTION\r\n2\r\nHEADER\r\n9\r\n$ACADVER\r\n1\r\nAC1027\r\n9\r\n$INSUNITS\r\n70\r\n1\r\n0\r\nENDSEC\r\n0\r\nSECTION\r\n2\r\nENTITIES\r\n0\r\nLWPOLYLINE\r\n100\r\nAcDbEntity\r\n8\r\nREFERENCE\r\n100\r\nAcDbPolyline\r\n90\r\n4\r\n70\r\n1\r\n10\r\n0\r\n20\r\n0\r\n10\r\n1\r\n20\r\n0\r\n10\r\n1\r\n20\r\n1\r\n10\r\n0\r\n20\r\n1\r\n0\r\nENDSEC\r\n0\r\nEOF\r\n");
        var original = input.ToArray();
        TimberGuideLabel[] labels = [new("ASSEMBLY-REFERENCE.dxf", "P1", "source", "face", new(2.5, -1.25), "P001", .125)];
        var native = NativeTimberKitReferenceTransform.Prepare(input, labels);
        using var stream = new MemoryStream(native.Dxf.ToArray()); var drawing = DxfFile.Load(stream);
        var ring = (DxfLwPolyline)drawing.Entities.Single();
        check("native reference planning uses measured multiplier48 with inch metadata and unchanged source bytes",
            native.CoordinateMultiplier == 48 && native.ViewScale == 48 && drawing.Header.DefaultDrawingUnits == DxfUnits.Inches &&
            ring.Layer == "REFERENCE" && ring.IsClosed && ring.Vertices.Max(v => v.X) == 48 && ring.Vertices.Max(v => v.Y) == 48 && input.SequenceEqual(original));
        check("native reference labels use scaled model positions and retain paper text size",
            native.Labels.Length == 1 && native.Labels[0].PositionFeet == new TimberPoint3(10, -5, 0) && native.Labels[0].Source == labels[0] &&
            native.Labels[0].Source.TextHeightInches == .125);
    }
}
