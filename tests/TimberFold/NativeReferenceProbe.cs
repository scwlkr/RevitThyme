using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitThyme.TimberFold.Fixtures;

// Test-only measurement of the INSTALLED native reference consumer, never a replacement assembly.
public static class NativeReferenceProbe
{
    private sealed record Point(double X, double Y, double Z = 0);
    private sealed record Segment(Point A, Point B, int Entity);
    private sealed record Pair(int Code, string Value);

    public static object Check(UIApplication uiapp, string sourceDxf, string destination, int entityIndex, double coordinateMultiplier, CancellationToken ct)
    {
        var source = uiapp.ActiveUIDocument?.Document ?? throw new InvalidOperationException("Pinned source required.");
        var pinned = Environment.GetEnvironmentVariable("REVITTHYME_FOUNDATION_FIXTURE");
        if (Environment.GetEnvironmentVariable("REVITTHYME_TRUSTED_CODE") != "1" || pinned is null || source.IsWorkshared ||
            uiapp.Application.Documents.Size != 1 || !string.Equals(Path.GetFullPath(pinned), Path.GetFullPath(source.PathName), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Use one exact pinned disposable development source.");
        if (!Path.IsPathFullyQualified(destination) || Directory.Exists(destination) || File.Exists(destination) ||
            !Path.IsPathFullyQualified(sourceDxf) || !sourceDxf.EndsWith(".dxf", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Use a known DXF and a fresh absolute evidence directory.");
        string runRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RevitThyme", "TimberFold", "runs") + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(sourceDxf).StartsWith(runRoot, StringComparison.OrdinalIgnoreCase) || new FileInfo(sourceDxf).Length > 16777216)
            throw new InvalidDataException("Reference input must be bounded and belong to a retained host run.");
        var ids = Ids(source); bool modified = source.IsModified; long view = source.ActiveView.Id.Value; string hash = Hash(source.PathName);
        var pairs = Read(sourceDxf); var records = Entities(pairs); int start = records[0].Start, end = records[^1].End;
        if (entityIndex < -2 || entityIndex >= records.Count) throw new ArgumentOutOfRangeException(nameof(entityIndex));
        var selected = entityIndex < 0 ? pairs : pairs.Take(start).Concat(pairs.Skip(records[entityIndex].Start).Take(records[entityIndex].End - records[entityIndex].Start)).Concat(pairs.Skip(end)).ToArray();
        if (entityIndex == -2)
        {
            var original = records[Math.Min(11, records.Count - 1)];
            var header = pairs.Skip(original.Start).Take(original.End - original.Start).Where(p => p.Code is not (10 or 20 or 40 or 41 or 42 or 91))
                .Select(p => p.Code == 90 ? new Pair(90, "4") : p);
            Pair[] vertices = [new(10, "0"), new(20, "0"), new(10, "0.00000001"), new(20, "0"), new(10, "1"), new(20, "1"), new(10, "0"), new(20, "1")];
            var corner = header.Concat(vertices);
            selected = pairs.Take(start).Concat(corner).Concat(pairs.Skip(end)).ToArray();
        }
        if (!double.IsFinite(coordinateMultiplier) || coordinateMultiplier < 1 || coordinateMultiplier > 1000) throw new ArgumentOutOfRangeException(nameof(coordinateMultiplier));
        if (coordinateMultiplier != 1)
        {
            var entities = Entities(selected); int first = entities[0].Start, last = entities[^1].End;
            selected = selected.Select((p, i) => i >= first && i < last && p.Code is 10 or 20
                ? new Pair(p.Code, (double.Parse(p.Value, CultureInfo.InvariantCulture) * coordinateMultiplier).ToString("R", CultureInfo.InvariantCulture)) : p).ToArray();
        }
        var expected = Segments(selected); var actual = new List<Segment>(); var layers = new HashSet<string>(StringComparer.Ordinal);
        Directory.CreateDirectory(destination);
        string input = Path.Combine(destination, "REFERENCE.dxf");
        if (entityIndex == -1 && coordinateMultiplier == 1) File.Copy(sourceDxf, input, false);
        else File.WriteAllText(input, string.Join("\r\n", selected.SelectMany(p => new[] { p.Code.ToString(CultureInfo.InvariantCulture), p.Value })) + "\r\n");
        Document? temporary = null; bool closed = false, productPassed = false; string? productError = null, probeError = null;
        try
        {
            ct.ThrowIfCancellationRequested();
            string template = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Autodesk", "RVT 2027", "Templates", "Default_I_ENU.rte");
            temporary = uiapp.Application.NewProjectDocument(template);
            using (var transaction = new Transaction(temporary, "Temporary reference consumer measurement"))
            {
                if (transaction.Start() != TransactionStatus.Started) throw new InvalidOperationException("Probe transaction did not start.");
                var type = new FilteredElementCollector(temporary).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>().First(t => t.ViewFamily == ViewFamily.Drafting);
                var drafting = ViewDrafting.Create(temporary, type.Id); drafting.Scale = 1;
                using var options = new DWGImportOptions { Unit = ImportUnit.Default, Placement = ImportPlacement.Origin,
                    ThisViewOnly = true, OrientToView = false, AutoCorrectAlmostVHLines = false };
                if (!temporary.Import(input, options, drafting, out var id) || temporary.GetElement(id) is not ImportInstance import)
                    throw new InvalidDataException("Native reference import failed.");
                temporary.Regenerate(); Walk(temporary, import.get_Geometry(new Options()));
                var installed = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "RevitThyme.Addin");
                var method = installed.GetType("RevitThyme.Addin.NativeTimberKitReference", true)!.GetMethod("Check", BindingFlags.NonPublic | BindingFlags.Static)!;
                try
                {
                    var result = method.Invoke(null, [temporary, import, input, ct])!;
                    productPassed = (bool)result.GetType().GetProperty("CoveragePassed")!.GetValue(result)!;
                }
                catch (TargetInvocationException error) { productError = (error.InnerException ?? error).ToString(); }
                if (transaction.RollBack() != TransactionStatus.RolledBack) throw new InvalidOperationException("Probe rollback failed.");
            }
        }
        catch (Exception error) { probeError = error.ToString(); }
        finally
        {
            if (temporary is { IsValidObject: true })
                try { closed = temporary.Close(false); } catch (Exception error) { probeError = error.ToString(); }
        }
        bool sourcePreserved = source.Equals(uiapp.ActiveUIDocument?.Document) && modified == source.IsModified &&
            view == source.ActiveView.Id.Value && hash == Hash(source.PathName) && ids.SequenceEqual(Ids(source)) && uiapp.Application.Documents.Size == 1;
        var forwardFailure = FirstFailure(expected, actual, ct); var reverseFailure = FirstFailure(actual, expected, ct);
        var report = new { sourceDxf, sourceSha256 = Hash(sourceDxf), entityIndex, coordinateMultiplier, totalOriginalEntities = records.Count,
            input, inputSha256 = Hash(input), expectedSegments = expected, nativeSegments = actual, nativeLayers = layers.Order().ToArray(),
            productPassed, productError, probeError, closed, sourcePreserved, forwardFailure, reverseFailure,
            coverageMeasurement = "Finite-segment endpoint capsules plus parallel intervals, matching installed shared coverage; productPassed invokes installed checker.",
            sourceModifiedBefore = modified, sourceModifiedAfter = source.IsModified, sourceFileSha256 = hash };
        string reportPath = Path.Combine(destination, "native-reference-probe.json");
        using (var stream = new FileStream(reportPath, FileMode.CreateNew))
        { JsonSerializer.Serialize(stream, report, new JsonSerializerOptions { WriteIndented = true }); stream.Flush(true); }
        return new { probeSucceeded = probeError is null && closed && sourcePreserved, productPassed, productError,
            reportPath, sha256 = Hash(reportPath), expectedSegments = expected.Count, nativeSegments = actual.Count, closed, sourcePreserved };

        void Walk(Document doc, GeometryElement geometry)
        {
            foreach (var item in geometry)
            {
                ct.ThrowIfCancellationRequested();
                if (item is GeometryInstance instance) { using var nested = instance.GetInstanceGeometry(); Walk(doc, nested); continue; }
                layers.Add((doc.GetElement(item.GraphicsStyleId) as GraphicsStyle)?.GraphicsStyleCategory.Name ?? "unresolved");
                if (item is PolyLine polyline)
                {
                    var points = polyline.GetCoordinates();
                    for (int i = 1; i < points.Count; i++) actual.Add(new(Convert(points[i - 1]), Convert(points[i]), -1));
                }
                else if (item is Line line) actual.Add(new(Convert(line.GetEndPoint(0)), Convert(line.GetEndPoint(1)), -1));
                else throw new InvalidDataException("Unexpected native reference geometry: " + item.GetType().Name);
                if (actual.Count > 100000) throw new InvalidDataException("Probe native segment limit exceeded.");
            }
        }
        static Point Convert(XYZ p) => new(p.X * 12, p.Y * 12, p.Z * 12);
    }

    private static object? FirstFailure(List<Segment> source, List<Segment> target, CancellationToken ct)
    {
        foreach (var s in source)
        {
            ct.ThrowIfCancellationRequested(); double length = Length(s);
            if (length <= 1e-12) continue;
            var intervals = new List<(double Low, double High)>(); var near = new List<object>(); bool finiteCoverage = false;
            foreach (var t in target)
            {
                ct.ThrowIfCancellationRequested();
                double a = Project(t.A), b = Project(t.B), da = Distance(t.A, a), db = Distance(t.B, b);
                double finiteA = FiniteDistance(s.A, t), finiteB = FiniteDistance(s.B, t);
                if (finiteA <= .000005 && finiteB <= .000005) { finiteCoverage = true; break; }
                if (Math.Min(da, db) <= .00001 && Math.Min(a, b) <= length + .00001 && Math.Max(a, b) >= -.00001)
                    near.Add(new { segment = t, a, b, da, db, finiteA, finiteB });
                if (da <= .000005 && db <= .000005)
                { double low = Math.Max(0, Math.Min(a, b)), high = Math.Min(length, Math.Max(a, b)); if (high > low) intervals.Add((low, high)); }
            }
            if (finiteCoverage) continue;
            double covered = 0; bool passed = intervals.Count > 0;
            foreach (var interval in intervals.OrderBy(i => i.Low))
            { if (interval.Low > covered + .000005) { passed = false; break; } covered = Math.Max(covered, interval.High); }
            passed &= covered + .000005 >= length;
            if (!passed) return new { segment = s, length, covered, intervals = intervals.Select(i => new { i.Low, i.High }).ToArray(), nearby = near.Take(20).ToArray() };
            double Project(Point p) => ((p.X - s.A.X) * (s.B.X - s.A.X) + (p.Y - s.A.Y) * (s.B.Y - s.A.Y)) / length;
            double Distance(Point p, double d) => Math.Sqrt(Math.Pow(p.X - s.A.X - (s.B.X - s.A.X) * d / length, 2) + Math.Pow(p.Y - s.A.Y - (s.B.Y - s.A.Y) * d / length, 2));
        }
        return null;
    }
    private static Pair[] Read(string path)
    {
        var lines = File.ReadAllLines(path); if (lines.Length % 2 != 0) throw new InvalidDataException("DXF pairs are incomplete.");
        return Enumerable.Range(0, lines.Length / 2).Select(i => new Pair(int.Parse(lines[2 * i], CultureInfo.InvariantCulture), lines[2 * i + 1])).ToArray();
    }
    private static List<(int Start, int End)> Entities(Pair[] pairs)
    {
        int section = Array.FindIndex(pairs, p => p.Code == 2 && p.Value == "ENTITIES");
        if (section < 0) throw new InvalidDataException("DXF has no ENTITIES section.");
        var result = new List<(int Start, int End)>(); int i = section + 1;
        while (pairs[i].Value != "ENDSEC")
        {
            int start = i++;
            while (pairs[i].Code != 0) i++;
            result.Add((start, i));
        }
        if (result.Count == 0) throw new InvalidDataException("Reference DXF has no entities."); return result;
    }
    private static List<Segment> Segments(Pair[] pairs)
    {
        var segments = new List<Segment>(); int entity = 0;
        foreach (var record in Entities(pairs))
        {
            if (pairs[record.Start].Value != "LWPOLYLINE") throw new InvalidDataException("Probe input requires LWPOLYLINE reference entities.");
            var points = new List<Point>(); double? x = null;
            foreach (var pair in pairs.Skip(record.Start).Take(record.End - record.Start))
            {
                if (pair.Code == 10) x = double.Parse(pair.Value, CultureInfo.InvariantCulture);
                if (pair.Code == 20 && x.HasValue) { points.Add(new(x.Value, double.Parse(pair.Value, CultureInfo.InvariantCulture))); x = null; }
            }
            for (int i = 0; i < points.Count; i++) segments.Add(new(points[i], points[(i + 1) % points.Count], entity));
            entity++;
        }
        return segments;
    }
    private static double Length(Segment s) => Math.Sqrt(Math.Pow(s.B.X - s.A.X, 2) + Math.Pow(s.B.Y - s.A.Y, 2));
    private static double FiniteDistance(Point p, Segment s)
    {
        double x = s.B.X - s.A.X, y = s.B.Y - s.A.Y, square = x * x + y * y;
        if (square <= 0) return double.PositiveInfinity;
        double t = Math.Clamp(((p.X - s.A.X) * x + (p.Y - s.A.Y) * y) / square, 0, 1);
        return Math.Sqrt(Math.Pow(p.X - s.A.X - t * x, 2) + Math.Pow(p.Y - s.A.Y - t * y, 2));
    }
    private static long[] Ids(Document doc) => new FilteredElementCollector(doc).WhereElementIsNotElementType().ToElementIds()
        .Concat(new FilteredElementCollector(doc).WhereElementIsElementType().ToElementIds()).Select(id => id.Value).Order().ToArray();
    private static string Hash(string path)
    { using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); return Convert.ToHexStringLower(SHA256.HashData(stream)); }
}
