using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitThyme.TimberFold.Fixtures;

// Independent native consumer: no Core, Clipper or DXF-writer dependency.
public static class NativeCutQualification
{
    private sealed record Point(double X, double Y, double Z = 0);
    private sealed record Segment(Point A, Point B);
    private const double Tolerance = .00001; // Physical inches.

    public static object Check(UIApplication uiapp, string runDirectory, string destination, CancellationToken ct)
    {
        var baseline = uiapp.ActiveUIDocument?.Document ?? throw new InvalidOperationException("Pinned source is required.");
        string? pinned = Environment.GetEnvironmentVariable("REVITTHYME_FOUNDATION_FIXTURE");
        if (pinned is null || Environment.GetEnvironmentVariable("REVITTHYME_TRUSTED_CODE") != "1" ||
            !string.Equals(Path.GetFullPath(baseline.PathName), Path.GetFullPath(pinned), StringComparison.OrdinalIgnoreCase) ||
            baseline.IsWorkshared || uiapp.Application.Documents.Size != 1)
            throw new InvalidOperationException("Use exactly one pinned disposable development document.");
        if (!Path.IsPathFullyQualified(runDirectory) || !Path.IsPathFullyQualified(destination) ||
            Directory.Exists(destination) || File.Exists(destination)) throw new InvalidDataException("Use an existing run and fresh absolute evidence directory.");
        using var fittedJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(runDirectory, "fitted.json")));
        using var stockJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(runDirectory, "layout.json")));
        using var receiptJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(runDirectory, "cutting", "cut-staging.json")));
        var panels = P(P(fittedJson.RootElement, "draft"), "panels").EnumerateArray().ToDictionary(e => Text(e, "id"));
        var placements = P(stockJson.RootElement, "placements").EnumerateArray().ToArray();
        var receipt = receiptJson.RootElement; var profile = P(receipt, "profile");
        var expected = new Dictionary<string, List<Segment>>(StringComparer.Ordinal);
        foreach (var placement in placements)
        {
            string name = FormattableString.Invariant($"CUT-{P(placement, "sheet").GetInt32():000}.dxf");
            if (!expected.TryGetValue(name, out var segments)) expected.Add(name, segments = []);
            foreach (var ring in P(panels[Text(placement, "panelId")], "ringsInches").EnumerateArray())
                AddRing(segments, ring.EnumerateArray().Select(p => Transform(new(Number(p, "x"), Number(p, "y")), placement)).ToArray());
        }
        var calibration = new List<Segment>(); expected.Add("CALIBRATION.dxf", calibration);
        double bar = Number(profile, "minimumBarInches"), width = Number(profile, "thicknessInches") + Number(profile, "jointClearanceInches");
        double arm = Math.Max(1, 2 * width);
        foreach (var placement in P(receipt, "calibrationPlacements").EnumerateArray())
        {
            Point[] ring = Text(placement, "panelId") switch
            {
                "calibration-square" => [new(0, 0), new(1, 0), new(1, 1), new(0, 1)],
                "calibration-bar" => [new(0, 0), new(1, 0), new(1, bar), new(0, bar)],
                "calibration-corner" => [new(0, 0), new(arm, 0), new(arm, width), new(width, width), new(width, arm), new(0, arm)],
                _ => throw new InvalidDataException("Unknown calibration coupon.")
            };
            AddRing(calibration, ring.Select(p => Transform(p, placement)).ToArray());
        }
        Directory.CreateDirectory(destination);
        var reports = new List<object>(); Document? temporary = null;
        bool modified = baseline.IsModified;
        var baselineIds = Inventory(baseline);
        try
        {
            string template = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Autodesk", "RVT 2027", "Templates", "Default_I_ENU.rte");
            temporary = uiapp.Application.NewProjectDocument(template);
            foreach (var file in expected.OrderBy(e => e.Key, StringComparer.Ordinal))
            {
                ct.ThrowIfCancellationRequested();
                using var transaction = new Transaction(temporary, "Temporary independent CAD readback");
                if (transaction.Start() != TransactionStatus.Started) throw new InvalidOperationException("Readback transaction did not start.");
                var type = new FilteredElementCollector(temporary).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                    .First(t => t.ViewFamily == ViewFamily.Drafting);
                var view = ViewDrafting.Create(temporary, type.Id); view.Scale = 1;
                using var options = new DWGImportOptions { Unit = ImportUnit.Default, Placement = ImportPlacement.Origin,
                    ThisViewOnly = true, OrientToView = false, AutoCorrectAlmostVHLines = false };
                string path = Path.Combine(runDirectory, "cutting", file.Key);
                if (!temporary.Import(path, options, view, out var id) || temporary.GetElement(id) is not ImportInstance imported)
                    throw new InvalidDataException("Native CAD import failed: " + file.Key);
                temporary.Regenerate();
                var actual = new List<Segment>(); var layers = new HashSet<string>(StringComparer.Ordinal);
                Read(temporary, imported.get_Geometry(new Options()), actual, layers, ct);
                if (actual.Count == 0 || layers.Count != 1 || !layers.Contains("CUT"))
                    throw new InvalidDataException("Native CAD geometry is empty or contains non-CUT layers.");
                foreach (var segment in actual.Concat(file.Value))
                    if (Math.Abs(segment.A.Z) > Tolerance / 2 || Math.Abs(segment.B.Z) > Tolerance / 2)
                        throw new InvalidDataException("Native CAD geometry is not planar at Z=0.");
                if (!Covered(actual, file.Value, ct) || !Covered(file.Value, actual, ct))
                    throw new InvalidDataException("Native CAD contour/scale differs by more than 0.00001 physical inch: " + file.Key);
                double expectedLength = file.Value.Sum(Length), actualLength = actual.Sum(Length);
                if (Math.Abs(expectedLength - actualLength) > Tolerance * (actual.Count + file.Value.Count))
                    throw new InvalidDataException("Native CAD duplicates or loses cutting segments: " + file.Key);
                reports.Add(new { file = file.Key, passed = true, expectedSegments = file.Value.Count, nativeSegments = actual.Count,
                    expectedLengthInches = expectedLength, nativeLengthInches = actualLength, layers = layers.ToArray(),
                    contourToleranceInches = Tolerance, importUnits = "Default: consume saved CAD units" });
                if (transaction.RollBack() != TransactionStatus.RolledBack) throw new InvalidOperationException("Temporary import did not roll back.");
            }
            temporary.Close(false); temporary = null;
            var afterIds = Inventory(baseline);
            var preservation = new { beforeModified = modified, afterModified = baseline.IsModified,
                activeSameReference = uiapp.ActiveUIDocument?.Document == baseline,
                activeSameNativeDocument = baseline.Equals(uiapp.ActiveUIDocument?.Document),
                activePath = uiapp.ActiveUIDocument?.Document.PathName, baselinePath = baseline.PathName,
                documentCount = uiapp.Application.Documents.Size, addedIds = afterIds.Except(baselineIds).ToArray(),
                removedIds = baselineIds.Except(afterIds).ToArray() };
            Write(Path.Combine(destination, "native-source-preservation.json"), preservation);
            if (baseline.IsModified != modified || !baseline.Equals(uiapp.ActiveUIDocument?.Document) || uiapp.Application.Documents.Size != 1 ||
                !baselineIds.SequenceEqual(afterIds))
                throw new InvalidDataException("Pinned source state changed during independent readback: " + JsonSerializer.Serialize(preservation));
            var result = new { passed = true, runDirectory, files = reports, sourcePreserved = true, physicalChecksOutstanding = true };
            Write(Path.Combine(destination, "native-cut-readback.json"), result); return result;
        }
        catch (Exception error)
        { Write(Path.Combine(destination, "native-cut-readback-failed.json"), new { passed = false, error = error.ToString(), files = reports }); throw; }
        finally { if (temporary is { IsValidObject: true }) temporary.Close(false); }
    }

    private static void Read(Document doc, GeometryElement geometry, List<Segment> segments, HashSet<string> layers, CancellationToken ct)
    {
        foreach (var item in geometry)
        {
            ct.ThrowIfCancellationRequested();
            if (item is GeometryInstance instance) { Read(doc, instance.GetInstanceGeometry(), segments, layers, ct); continue; }
            if (item is not PolyLine && item is not Line) throw new InvalidDataException("Native CAD contains geometry other than straight cuts: " + item.GetType().Name);
            var style = doc.GetElement(item.GraphicsStyleId) as GraphicsStyle;
            layers.Add(style?.GraphicsStyleCategory.Name ?? "unresolved");
            if (item is PolyLine polyline)
            {
                var points = polyline.GetCoordinates();
                for (int i = 1; i < points.Count; i++) segments.Add(new(Convert(points[i - 1]), Convert(points[i])));
            }
            else if (item is Line line) segments.Add(new(Convert(line.GetEndPoint(0)), Convert(line.GetEndPoint(1))));
        }
        static Point Convert(XYZ value) => new(value.X * 12, value.Y * 12, value.Z * 12);
    }

    // Half-tolerance budgets for perpendicular, projection-gap and Z errors
    // conservatively bound total 3D deviation below the Euclidean gate.
    private static bool Covered(List<Segment> source, List<Segment> target, CancellationToken ct)
    {
        foreach (var segment in source)
        {
            ct.ThrowIfCancellationRequested();
            double dx = segment.B.X - segment.A.X, dy = segment.B.Y - segment.A.Y, length = Length(segment);
            if (length <= 1e-12) return false;
            var intervals = new List<(double Low, double High)>();
            foreach (var other in target)
            {
                double a = Project(other.A), b = Project(other.B);
                if (Distance(other.A, a) > Tolerance / 2 || Distance(other.B, b) > Tolerance / 2) continue;
                intervals.Add((Math.Max(0, Math.Min(a, b)), Math.Min(length, Math.Max(a, b))));
            }
            double covered = 0;
            foreach (var interval in intervals.OrderBy(i => i.Low))
            {
                if (interval.High < 0 || interval.Low > length) continue;
                if (interval.Low > covered + Tolerance / 2) return false;
                covered = Math.Max(covered, interval.High);
            }
            if (covered + Tolerance / 2 < length) return false;
            double Project(Point p) => ((p.X - segment.A.X) * dx + (p.Y - segment.A.Y) * dy) / length;
            double Distance(Point p, double projection) => Math.Sqrt(Math.Pow(p.X - segment.A.X - dx * projection / length, 2) +
                Math.Pow(p.Y - segment.A.Y - dy * projection / length, 2));
        }
        return true;
    }
    private static double Length(Segment value) => Math.Sqrt(Math.Pow(value.B.X - value.A.X, 2) + Math.Pow(value.B.Y - value.A.Y, 2));
    private static long[] Inventory(Document doc) => new FilteredElementCollector(doc).WhereElementIsNotElementType().ToElementIds()
        .Concat(new FilteredElementCollector(doc).WhereElementIsElementType().ToElementIds()).Select(id => id.Value).Order().ToArray();
    private static Point Transform(Point p, JsonElement placement)
    {
        double x = Number(placement, "xInches"), y = Number(placement, "yInches");
        var min = P(placement, "localMinimum"); var max = P(placement, "localMaximum");
        return P(placement, "quarterTurn").GetBoolean() ? new(x + Number(max, "y") - p.Y, y + p.X - Number(min, "x")) :
            new(x + p.X - Number(min, "x"), y + p.Y - Number(min, "y"));
    }
    private static void AddRing(List<Segment> segments, Point[] points)
    { for (int i = 0; i < points.Length; i++) segments.Add(new(points[i], points[(i + 1) % points.Length])); }
    private static JsonElement P(JsonElement value, string name) => value.EnumerateObject().Single(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)).Value;
    private static double Number(JsonElement value, string name) => P(value, name).GetDouble();
    private static string Text(JsonElement value, string name) => P(value, name).GetString() ?? throw new InvalidDataException("Missing " + name);
    private static void Write(string path, object value)
    { using var stream = new FileStream(path, FileMode.CreateNew); JsonSerializer.Serialize(stream, value, new JsonSerializerOptions { WriteIndented = true }); }
}
