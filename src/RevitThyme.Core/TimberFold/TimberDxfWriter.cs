using System.Globalization;
using System.Text;
using IxMilia.Dxf;
using IxMilia.Dxf.Entities;

namespace RevitThyme.Core;

// The supported interchange dialect is pinned to IxMilia.Dxf 0.8.4, AC1027.
// Geometry contains only closed, zero-width, planar CUT lightweight polylines.
internal static class TimberDxfWriter
{
    internal static byte[] Write(IEnumerable<IEnumerable<TimberPoint2>> contours)
    {
        var drawing = new DxfFile();
        drawing.Header.SetDefaults();
        drawing.Header.Version = DxfAcadVersion.R2013;
        drawing.Header.DefaultDrawingUnits = DxfUnits.Inches;
        drawing.Header.DrawingUnits = DxfDrawingUnits.English;
        drawing.Header.FingerprintGuid = Guid.Empty;
        drawing.Header.VersionGuid = Guid.Empty;
        drawing.Header.CreationDate = drawing.Header.CreationDateUniversal = new(2000, 1, 1);
        drawing.ViewPorts.Clear();
        foreach (var contour in contours)
            drawing.Entities.Add(new DxfLwPolyline(contour.Select(p => new DxfLwPolylineVertex { X = p.X, Y = p.Y }))
            { Layer = "CUT", IsClosed = true });
        using var stream = new MemoryStream();
        drawing.Save(stream, asText: true);
        // Save overwrites these four standard time variables. Normalize only their
        // HEADER values, with exact code/type checks; geometry and other metadata stay intact.
        var lines = Encoding.UTF8.GetString(stream.ToArray()).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        bool header = false;
        for (int i = 0; i + 3 < lines.Length; i += 2)
        {
            int code = int.Parse(lines[i], CultureInfo.InvariantCulture);
            if (code == 2 && lines[i + 1] == "HEADER") header = true;
            if (code == 0 && lines[i + 1] == "ENDSEC") header = false;
            if (!header || code != 9) continue;
            string? value = lines[i + 1] switch
            {
                "$TDUPDATE" or "$TDUUPDATE" => "2451544.5",
                "$TDINDWG" or "$TDUSRTIMER" => "0.0",
                _ => null
            };
            if (value is null) continue;
            if (int.Parse(lines[i + 2], CultureInfo.InvariantCulture) != 40)
                throw new InvalidDataException("Unexpected DXF standard time field type.");
            lines[i + 3] = value;
        }
        return Encoding.ASCII.GetBytes(string.Join("\r\n", lines));
    }
}
