using System.Text.Json;
using System.Numerics;
using System.Security.Cryptography;
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
        => CheckFiles(uiapp, runDirectory, destination, false, ct);

    public static object CheckDwg(UIApplication uiapp, string runDirectory, string freshDestination, CancellationToken ct)
        => CheckFiles(uiapp, runDirectory, freshDestination, true, ct);

    private static object CheckFiles(UIApplication uiapp, string runDirectory, string destination, bool dwg, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
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
        if (panels.Count == 0 || placements.Length != panels.Count || placements.Select(p => Text(p, "panelId")).Distinct(StringComparer.Ordinal).Count() != panels.Count ||
            placements.Any(p => !panels.ContainsKey(Text(p, "panelId")))) throw new InvalidDataException("Independent expectations require every fitted part placed exactly once.");
        var receipt = receiptJson.RootElement; var profile = P(receipt, "profile");
        var expected = new Dictionary<string, List<Point[]>>(StringComparer.Ordinal);
        foreach (var placement in placements)
        {
            string name = FormattableString.Invariant($"CUT-{P(placement, "sheet").GetInt32():000}.") + (dwg ? "dwg" : "dxf");
            if (!expected.TryGetValue(name, out var rings)) expected.Add(name, rings = []);
            foreach (var ring in P(panels[Text(placement, "panelId")], "ringsInches").EnumerateArray())
                rings.Add(ring.EnumerateArray().Select(p => Transform(new(Number(p, "x"), Number(p, "y")), placement)).ToArray());
        }
        var calibration = new List<Point[]>(); expected.Add(dwg ? "CALIBRATION.dwg" : "CALIBRATION.dxf", calibration);
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
            calibration.Add(ring.Select(p => Transform(p, placement)).ToArray());
        }
        if (dwg && !Directory.GetFileSystemEntries(Path.Combine(runDirectory, "native-cuts")).Select(Path.GetFileName).Order(StringComparer.Ordinal)
            .SequenceEqual(expected.Keys.Order(StringComparer.Ordinal))) throw new InvalidDataException("Native DWG inventory differs from complete stock/calibration expectations.");
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
                string path = Path.Combine(runDirectory, dwg ? "native-cuts" : "cutting", file.Key);
                string inputHash = FileHash(path); long inputBytes = new FileInfo(path).Length;
                if (!temporary.Import(path, options, view, out var id) || temporary.GetElement(id) is not ImportInstance imported)
                    throw new InvalidDataException("Native CAD import failed: " + file.Key);
                temporary.Regenerate();
                var actual = new List<Segment>(); var layers = new HashSet<string>(StringComparer.Ordinal); var closed = new List<Point[]>();
                Read(temporary, imported.get_Geometry(new Options()), actual, layers, closed, dwg, ct);
                var canonical = new List<Segment>(); foreach (var ring in file.Value) AddRing(canonical, ring);
                if (actual.Count == 0 || layers.Count != 1 || !layers.Contains("CUT"))
                    throw new InvalidDataException("Native CAD geometry is empty or contains non-CUT layers.");
                foreach (var segment in actual.Concat(canonical))
                    if (Math.Abs(segment.A.Z) > Tolerance / 2 || Math.Abs(segment.B.Z) > Tolerance / 2)
                        throw new InvalidDataException("Native CAD geometry is not planar at Z=0.");
                RequireUnique(actual, ct);
                if (!Covered(actual, canonical, ct) || !Covered(canonical, actual, ct))
                    throw new InvalidDataException("Native CAD contour/scale differs by more than 0.00001 physical inch: " + file.Key);
                double expectedLength = canonical.Sum(Length), actualLength = actual.Sum(Length);
                if (Math.Abs(expectedLength - actualLength) > Tolerance * (actual.Count + canonical.Count))
                    throw new InvalidDataException("Native CAD duplicates or loses cutting segments: " + file.Key);
                if (dwg) RequireContours(file.Value, closed, file.Key == "CALIBRATION.dwg", ct);
                if (FileHash(path) != inputHash || new FileInfo(path).Length != inputBytes)
                    throw new InvalidDataException("Consumed CAD file changed during independent readback: " + file.Key);
                reports.Add(new { file = file.Key, sha256 = inputHash, bytes = inputBytes, passed = true, expectedSegments = canonical.Count, nativeSegments = actual.Count,
                    expectedClosedContours = file.Value.Count, nativeClosedContours = closed.Count,
                    expectedLengthInches = expectedLength, nativeLengthInches = actualLength, layers = layers.ToArray(),
                    contourToleranceInches = Tolerance, importUnits = "Default: consume saved CAD units" });
                if (transaction.RollBack() != TransactionStatus.RolledBack) throw new InvalidOperationException("Temporary import did not roll back.");
            }
            if (!temporary.Close(false)) throw new InvalidOperationException("Independent CAD readback document did not close unsaved.");
            temporary = null;
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
        finally { if (temporary is { IsValidObject: true } && !temporary.Close(false)) throw new InvalidOperationException("Independent CAD cleanup did not close its temporary document."); }
    }

    private static string FileHash(string path)
    { using var stream = File.OpenRead(path); return Convert.ToHexStringLower(SHA256.HashData(stream)); }

    private static void Read(Document doc, GeometryElement geometry, List<Segment> segments, HashSet<string> layers,
        List<Point[]> closed, bool requireClosed, CancellationToken ct)
    {
        foreach (var item in geometry)
        {
            ct.ThrowIfCancellationRequested();
            if (item is GeometryInstance instance) { using var nested = instance.GetInstanceGeometry(); Read(doc, nested, segments, layers, closed, requireClosed, ct); continue; }
            if (item is not PolyLine && item is not Line) throw new InvalidDataException("Native CAD contains geometry other than straight cuts: " + item.GetType().Name);
            var style = doc.GetElement(item.GraphicsStyleId) as GraphicsStyle;
            layers.Add(style?.GraphicsStyleCategory.Name ?? "unresolved");
            if (item is PolyLine polyline)
            {
                var points = polyline.GetCoordinates();
                bool isClosed = points.Count >= 4 && points[0].DistanceTo(points[^1]) * 12 <= 1e-10;
                if (requireClosed && !isClosed) throw new InvalidDataException("Native DWG contains an open cutting polyline.");
                if (isClosed) closed.Add(points.Take(points.Count - 1).Select(Convert).ToArray());
                for (int i = 1; i < points.Count; i++) segments.Add(new(Convert(points[i - 1]), Convert(points[i])));
            }
            else if (requireClosed) throw new InvalidDataException("Native DWG cutting contours must be explicit closed polylines.");
            else if (item is Line line) segments.Add(new(Convert(line.GetEndPoint(0)), Convert(line.GetEndPoint(1))));
            if (segments.Count > 1000000) throw new InvalidDataException("Independent CAD readback exceeds its segment limit.");
        }
        static Point Convert(XYZ value) => double.IsFinite(value.X) && double.IsFinite(value.Y) && double.IsFinite(value.Z)
            ? new(value.X * 12, value.Y * 12, value.Z * 12) : throw new InvalidDataException("Nonfinite native CAD coordinate.");
    }

    // Half-tolerance budgets for perpendicular, projection-gap and Z errors
    // conservatively bound total 3D deviation below the Euclidean gate.
    private static bool Covered(List<Segment> source, List<Segment> target, CancellationToken ct)
    {
        long work = 0;
        foreach (var segment in source)
        {
            ct.ThrowIfCancellationRequested();
            double dx = segment.B.X - segment.A.X, dy = segment.B.Y - segment.A.Y, length = Length(segment);
            if (length <= 1e-12) return false;
            var intervals = new List<(double Low, double High)>(); bool capsuleCovered = false;
            foreach (var other in target)
            {
                if (++work > 20000000) throw new InvalidDataException("Independent CAD coverage exceeds its work limit.");
                if ((work & 127) == 0) ct.ThrowIfCancellationRequested();
                // A finite segment plus a radius disk is convex. If both source
                // endpoints lie in this SAME capsule, its entire edge lies there.
                // This proves collapsed-corner coverage, rather than skipping tiny edges.
                if (WithinCapsule(segment.A, other) && WithinCapsule(segment.B, other))
                { capsuleCovered = true; break; }
                double a = Project(other.A), b = Project(other.B);
                if (Distance(other.A, a) > Tolerance / 2 || Distance(other.B, b) > Tolerance / 2) continue;
                double low = Math.Max(0, Math.Min(a, b)), high = Math.Min(length, Math.Max(a, b));
                if (high > low) intervals.Add((low, high));
            }
            if (capsuleCovered) continue;
            if (intervals.Count == 0) return false;
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
    private static bool WithinCapsule(Point point, Segment segment)
    {
        double x = segment.B.X - segment.A.X, y = segment.B.Y - segment.A.Y, squaredLength = x * x + y * y;
        if (squaredLength <= 1e-24) return false;
        double fraction = Math.Clamp(((point.X - segment.A.X) * x + (point.Y - segment.A.Y) * y) / squaredLength, 0, 1);
        double dx = point.X - (segment.A.X + fraction * x), dy = point.Y - (segment.A.Y + fraction * y);
        return dx * dx + dy * dy <= Tolerance * Tolerance / 4;
    }
    private static void RequireContours(List<Point[]> expected, List<Point[]> actual, bool calibration, CancellationToken ct)
    {
        if (expected.Count != actual.Count) throw new InvalidDataException("Native DWG closed-polyline contour count differs from canonical parts/holes.");
        var remaining = Enumerable.Range(0, actual.Count).ToHashSet();
        foreach (var ring in expected)
        {
            var canonical = new List<Segment>(); AddRing(canonical, ring);
            var matching = remaining.Where(index => { var candidate = new List<Segment>(); AddRing(candidate, actual[index]);
                return Covered(canonical, candidate, ct) && Covered(candidate, canonical, ct); }).ToArray();
            if (matching.Length != 1) throw new InvalidDataException("Native DWG has a missing, combined or ambiguous part/hole contour.");
            int selected = matching[0]; remaining.Remove(selected);
            if (calibration && Math.Abs(ring.Max(p => p.X) - ring.Min(p => p.X) - 1) < 1e-12 &&
                Math.Abs(ring.Max(p => p.Y) - ring.Min(p => p.Y) - 1) < 1e-12 &&
                (Math.Abs(actual[selected].Max(p => p.X) - actual[selected].Min(p => p.X) - 1) > Tolerance ||
                 Math.Abs(actual[selected].Max(p => p.Y) - actual[selected].Min(p => p.Y) - 1) > Tolerance))
                throw new InvalidDataException("Native DWG one-inch calibration failed.");
        }
        // Exact binary-coordinate orientation is independent of the producer's
        // tolerance matcher and cannot erase a tiny crossing as a small error.
        long work = 0;
        for (int r = 0; r < actual.Count; r++)
        for (int s = r; s < actual.Count; s++)
        for (int i = 0; i < actual[r].Length; i++)
        for (int j = s == r ? i + 1 : 0; j < actual[s].Length; j++)
        {
            if (++work > 20000000) throw new InvalidDataException("Independent CAD topology exceeds its work limit.");
            if ((work & 127) == 0) ct.ThrowIfCancellationRequested();
            if (r == s && (j == i + 1 || i == 0 && j == actual[r].Length - 1)) continue;
            var a = actual[r][i]; var b = actual[r][(i + 1) % actual[r].Length];
            var c = actual[s][j]; var d = actual[s][(j + 1) % actual[s].Length];
            if (Math.Max(a.X, b.X) < Math.Min(c.X, d.X) || Math.Max(c.X, d.X) < Math.Min(a.X, b.X) ||
                Math.Max(a.Y, b.Y) < Math.Min(c.Y, d.Y) || Math.Max(c.Y, d.Y) < Math.Min(a.Y, b.Y)) continue;
            int x = Turn(a, b, c), y = Turn(a, b, d), z = Turn(c, d, a), w = Turn(c, d, b);
            if (x * y <= 0 && z * w <= 0) throw new InvalidDataException("Native DWG cuts cross or touch outside their own adjacent vertices.");
        }
    }
    private static void RequireUnique(List<Segment> segments, CancellationToken ct)
    {
        long work = 0;
        for (int i = 0; i < segments.Count; i++) for (int j = i + 1; j < segments.Count; j++)
        {
            if (++work > 20000000) throw new InvalidDataException("Independent CAD multiplicity exceeds its work limit.");
            if ((work & 127) == 0) ct.ThrowIfCancellationRequested();
            var a = segments[i]; var b = segments[j];
            if (Length(a) <= 1e-12 || Length(b) <= 1e-12) throw new InvalidDataException("Native CAD has a zero-length cut.");
            if (Turn(a.A, a.B, b.A) != 0 || Turn(a.A, a.B, b.B) != 0) continue;
            bool horizontal = Math.Abs(a.A.X - a.B.X) >= Math.Abs(a.A.Y - a.B.Y);
            double a0 = horizontal ? a.A.X : a.A.Y, a1 = horizontal ? a.B.X : a.B.Y;
            double b0 = horizontal ? b.A.X : b.A.Y, b1 = horizontal ? b.B.X : b.B.Y;
            if (Math.Min(Math.Max(a0, a1), Math.Max(b0, b1)) > Math.Max(Math.Min(a0, a1), Math.Min(b0, b1)))
                throw new InvalidDataException("Native CAD repeats or backtracks a cutting segment.");
        }
    }
    private static int Turn(Point a, Point b, Point c) => ((Binary(b.X) - Binary(a.X)) * (Binary(c.Y) - Binary(a.Y)) -
        (Binary(b.Y) - Binary(a.Y)) * (Binary(c.X) - Binary(a.X))).Sign;
    private static BigInteger Binary(double value)
    {
        long bits = BitConverter.DoubleToInt64Bits(value); int exponent = (int)((bits >> 52) & 2047);
        BigInteger mantissa = (bits & 0xFFFFFFFFFFFFF) | (exponent == 0 ? 0L : 1L << 52);
        BigInteger scaled = mantissa << Math.Max(0, exponent - 1); return bits < 0 ? -scaled : scaled;
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
