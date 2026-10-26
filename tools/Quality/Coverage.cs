using System.Xml;
using System.Xml.Linq;

namespace RevitThyme.Quality;

public sealed record UnitCoverage(int CoveredLines, int ExecutableLines)
{
    public double Fraction => ExecutableLines == 0 ? 0 : CoveredLines / (double)ExecutableLines;
}

public sealed class Coverage
{
    private readonly Dictionary<string, Dictionary<int, bool>> files = new(StringComparer.OrdinalIgnoreCase);

    public static Coverage Load(string root, params string[] reports)
    {
        if (reports.Length == 0) throw new ArgumentException("Measured Cobertura reports are required.");
        var coverage = new Coverage();
        foreach (string path in reports)
        {
            using var reader = XmlReader.Create(path, new XmlReaderSettings
            { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 128 * 1024 * 1024 });
            var document = XDocument.Load(reader);
            if (document.Root?.Name != "coverage") throw new InvalidDataException("Expected Cobertura coverage XML.");
            foreach (var item in document.Descendants("class")) coverage.AddClass(root, item);
        }
        if (!coverage.files.Values.Any(lines => lines.Count > 0)) throw new InvalidDataException("Coverage reports contain no source lines.");
        return coverage;
    }

    private void AddClass(string root, XElement item)
    {
        string file = item.Attribute("filename")?.Value ?? throw new InvalidDataException("Coverage class has no filename.");
        string absolute = Path.GetFullPath(Path.Combine(root, file));
        if (!files.TryGetValue(absolute, out var hits)) files.Add(absolute, hits = []);
        foreach (var row in item.Elements("lines").Elements("line"))
        {
            int line = Count(row, "number", 1), count = Count(row, "hits", 0);
            hits[line] = hits.GetValueOrDefault(line) || count > 0;
        }
    }

    private static int Count(XElement row, string attribute, int minimum)
    {
        if (!int.TryParse(row.Attribute(attribute)?.Value, out int value) || value < minimum)
            throw new InvalidDataException("Invalid Cobertura line/hit count.");
        return value;
    }

    public UnitCoverage For(string root, CodeUnit unit)
    {
        if (!files.TryGetValue(Path.GetFullPath(Path.Combine(root, unit.Path)), out var hits)) return new(0, 0);
        var lines = hits.Where(pair => pair.Key >= unit.StartLine && pair.Key <= unit.EndLine).ToArray();
        return new(lines.Count(pair => pair.Value), lines.Length);
    }

    public bool? Hit(string root, string file, int line)
    {
        if (!files.TryGetValue(Path.GetFullPath(Path.Combine(root, file)), out var hits) || !hits.TryGetValue(line, out bool covered)) return null;
        return covered;
    }
}
