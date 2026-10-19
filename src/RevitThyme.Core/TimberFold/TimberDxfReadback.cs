using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace RevitThyme.Core;

// Independent ASCII DXF reader: never calls the writer library. The metadata
// fingerprint pins a reviewed, geometry-free AC1027 dialect, in addition to the
// explicit geometric checks below. Unknown extensions fail closed.
internal static class TimberDxfReadback
{
    // Reviewed defaults: inch/English WCS, zero insertions/elevations, no custom
    // UCS, empty model/paper blocks, standard tables and two plain dictionaries.
    // Only checked extents and nongeometric hex handles vary with the inventory.
    private const string MetadataHash = "a7d3d206843c8c471cd556221a998e5625883b04168b9502a113ff43dc5e2a07";
    private sealed record Pair(int Code, string Value);

    internal static void Check(string path, IReadOnlyList<IReadOnlyList<TimberPoint2>> expected, CancellationToken ct, bool calibration)
    {
        var info = new FileInfo(path);
        if (info.Length > 64 * 1024 * 1024) throw new InvalidDataException("DXF readback exceeds 64 MiB.");
        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Any(b => b > 127 || b == 0)) throw new InvalidDataException("Expected ASCII DXF.");
        var lines = Encoding.ASCII.GetString(bytes).Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n').Split('\n');
        if (lines.Length % 2 != 0) throw new InvalidDataException("Incomplete DXF code pair.");
        var pairs = new List<Pair>();
        for (int i = 0; i < lines.Length; i += 2)
        {
            if ((i & 4095) == 0) ct.ThrowIfCancellationRequested();
            if (!int.TryParse(lines[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out int code))
                throw new InvalidDataException("Invalid DXF group code.");
            pairs.Add(new(code, lines[i + 1].Trim()));
        }
        var sections = new Dictionary<string, List<Pair>>(StringComparer.Ordinal);
        int position = 0;
        while (position < pairs.Count - 1)
        {
            Require(pairs[position++] == new Pair(0, "SECTION"), "Expected DXF SECTION.");
            var name = pairs[position++];
            Require(name.Code == 2 && !sections.ContainsKey(name.Value), "Invalid/duplicate DXF section.");
            var section = new List<Pair>();
            while (position < pairs.Count && pairs[position] != new Pair(0, "ENDSEC")) section.Add(pairs[position++]);
            Require(position++ < pairs.Count, "Unterminated DXF section.");
            sections.Add(name.Value, section);
        }
        Require(position == pairs.Count - 1 && pairs[position] == new Pair(0, "EOF"), "DXF must end exactly at EOF.");
        Require(sections.Keys.SequenceEqual(new[] { "HEADER", "CLASSES", "TABLES", "BLOCKS", "ENTITIES", "OBJECTS" }), "Unexpected DXF sections.");
        var header = ReadHeader(sections["HEADER"]);
        Header("$ACADVER", new Pair(1, "AC1027")); Header("$INSUNITS", new Pair(70, "1")); Header("$MEASUREMENT", new Pair(70, "0"));
        Header("$INSBASE", new(10, "0.0"), new(20, "0.0"), new(30, "0.0"));
        Header("$UCSORG", new(10, "0.0"), new(20, "0.0"), new(30, "0.0"));
        Header("$UCSXDIR", new(10, "1.0"), new(20, "0.0"), new(30, "0.0"));
        Header("$UCSYDIR", new(10, "0.0"), new(20, "1.0"), new(30, "0.0"));
        Header("$ELEVATION", new Pair(40, "0.0")); Header("$PELEVATION", new Pair(40, "0.0")); Header("$THICKNESS", new Pair(40, "0.0"));
        var entities = Records(sections["ENTITIES"]);
        Require(entities.Count == expected.Count, "Cut contour count differs from the complete part inventory.");
        for (int i = 0; i < entities.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var record = entities[i];
            Require(record[0] == new Pair(0, "LWPOLYLINE"), "Only CUT LWPOLYLINE entities are permitted.");
            Require(record.All(p => p.Code is 0 or 5 or 100 or 8 or 370 or 430 or 440 or 284 or 90 or 70 or 10 or 20 or 91 or 38 or 39 or 40 or 41 or 42 or 43 or 210 or 220 or 230), "Unexpected entity code/transform.");
            foreach (int code in new[] { 5, 8, 370, 430, 440, 284, 90, 70 })
                Require(record.Count(p => p.Code == code) == 1, "Missing/duplicate required polyline field.");
            Require(ulong.TryParse(record.Single(p => p.Code == 5).Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _), "Invalid entity handle.");
            foreach (int code in new[] { 38, 39, 43, 210, 220, 230 })
                Require(record.Count(p => p.Code == code) <= 1, "Duplicate polyline transform field.");
            Require(record.Single(p => p.Code == 8).Value == "CUT" && record.Single(p => p.Code == 70).Value == "1", "Contours must be closed on CUT.");
            Require(record.Where(p => p.Code is 38 or 39 or 40 or 41 or 42 or 43 or 210 or 220).All(p => Number(p) == 0) &&
                record.Where(p => p.Code == 230).All(p => Number(p) == 1), "Contours must have zero elevation, width, bulge and identity extrusion.");
            Require(record.Where(p => p.Code is 370 or 440 or 284 or 91).All(p => Number(p) == 0) &&
                record.Where(p => p.Code == 430).All(p => p.Value == "") &&
                record.Where(p => p.Code == 100).Select(p => p.Value).SequenceEqual(new[] { "AcDbEntity", "AcDbPolyline" }), "Unexpected polyline display metadata.");
            var coordinates = record.Where(p => p.Code is 10 or 20).ToArray();
            Require(Number(record.Single(p => p.Code == 90)) == expected[i].Count && coordinates.Length == expected[i].Count * 2 && expected[i].Count >= 3,
                "Polyline vertex count differs from the canonical ring.");
            Require(record.Count(p => p.Code == 91) == expected[i].Count &&
                new[] { 40, 41, 42 }.All(code => record.Count(p => p.Code == code) <= expected[i].Count), "Unexpected per-vertex metadata.");
            for (int j = 0; j < expected[i].Count; j++)
                Require(coordinates[j * 2].Code == 10 && coordinates[j * 2 + 1].Code == 20 &&
                    PointNear(Number(coordinates[j * 2]), Number(coordinates[j * 2 + 1]), expected[i][j]),
                    "Saved contour differs from its canonical physical-inch transform.");
            if (calibration && i == 0)
            {
                var x = coordinates.Where(p => p.Code == 10).Select(Number).ToArray();
                var y = coordinates.Where(p => p.Code == 20).Select(Number).ToArray();
                Require(x.Length == 4 && Near(x.Max() - x.Min(), 1) && Near(y.Max() - y.Min(), 1),
                    "Saved one-inch calibration dimensions exceed 1e-5 inch tolerance.");
                for (int j = 0; j < 4; j++)
                {
                    double dx = x[(j + 1) % 4] - x[j], dy = y[(j + 1) % 4] - y[j];
                    Require(Near(Math.Sqrt(dx * dx + dy * dy), 1), "Saved one-inch calibration edge exceeds 1e-5 inch tolerance.");
                }
            }
        }
        var points = expected.SelectMany(p => p).ToArray();
        Extent("$EXTMIN", points.Min(p => p.X), points.Min(p => p.Y));
        Extent("$EXTMAX", points.Max(p => p.X), points.Max(p => p.Y));
        // Blocks may contain only empty model/paper space containers: no nested
        // geometry or INSERT transforms can hide outside the checked ENTITIES.
        Require(Records(sections["BLOCKS"]).Select(r => r[0].Value).SequenceEqual(new[] { "BLOCK", "ENDBLK", "BLOCK", "ENDBLK" }), "Blocks must be empty space containers.");
        Require(Records(sections["OBJECTS"]).All(r => r[0].Value == "DICTIONARY") && sections["CLASSES"].Count == 0, "Unexpected object/class payload.");
        var metadata = new StringBuilder();
        foreach (var section in sections.Where(s => s.Key != "ENTITIES"))
        {
            metadata.Append(section.Key).Append('\n');
            string variable = "";
            foreach (var pair in section.Value)
            {
                if (pair.Code == 9) variable = pair.Value;
                string value = pair.Value;
                if (pair.Code is 5 or 330 or 350)
                { Require(ulong.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _), "Invalid metadata handle."); value = "HANDLE"; }
                if (section.Key == "HEADER" && (variable is "$EXTMIN" or "$EXTMAX") && pair.Code != 9) value = "EXTENT";
                metadata.Append(pair.Code.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value).Append('\n');
            }
        }
        string hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(metadata.ToString())));
        Require(hash == MetadataHash, "Unsupported DXF metadata dialect: " + hash);

        void Header(string name, params Pair[] values) => Require(header.TryGetValue(name, out var found) && found.SequenceEqual(values), "Unexpected DXF header " + name);
        void Extent(string name, double x, double y)
        {
            Require(header.TryGetValue(name, out var found) && found.Count == 3 && found[0].Code == 10 && found[1].Code == 20 && found[2].Code == 30 &&
                Near(Number(found[0]), x) && Near(Number(found[1]), y) && Number(found[2]) == 0, "Invalid drawing extent.");
        }
    }

    private static Dictionary<string, List<Pair>> ReadHeader(List<Pair> pairs)
    {
        var result = new Dictionary<string, List<Pair>>(StringComparer.Ordinal);
        List<Pair>? current = null;
        foreach (var pair in pairs)
            if (pair.Code == 9) { current = []; Require(result.TryAdd(pair.Value, current), "Duplicate header variable."); }
            else { Require(current is not null, "Header value has no variable."); current!.Add(pair); }
        return result;
    }
    private static List<List<Pair>> Records(List<Pair> pairs)
    {
        var records = new List<List<Pair>>();
        foreach (var pair in pairs)
        {
            if (pair.Code == 0) records.Add([]);
            Require(records.Count > 0, "Invalid DXF record."); records[^1].Add(pair);
        }
        return records;
    }
    private static double Number(Pair pair)
    {
        Require(double.TryParse(pair.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && double.IsFinite(value), "Nonfinite/invalid DXF number.");
        return value;
    }
    private static bool Near(double a, double b) => Math.Abs(a - b) <= 0.00001;
    private static bool PointNear(double x, double y, TimberPoint2 expected)
    {
        double dx = x - expected.X, dy = y - expected.Y;
        // Corresponding vertices with this Euclidean bound also bound every
        // corresponding straight segment, hence the complete polygon contour.
        return dx * dx + dy * dy <= 0.00001 * 0.00001;
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
}
