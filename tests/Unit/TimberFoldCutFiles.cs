using RevitThyme.Core;
using System.Globalization;
using System.Text;

namespace RevitThyme.Unit;

internal static class TimberFoldCutFiles
{
    public static void Run(Action<string, bool> check)
    {
        string directory = Path.Combine(Path.GetTempPath(), "RevitThyme-cut-tests-" + Guid.NewGuid().ToString("N"));
        var panels = new[] { TimberFoldStock.Panel("A", 6, 3) };
        var profile = new TimberFoldProfile();
        var stock = TimberStockLayout.Create(panels, profile);
        var result = TimberCutFiles.Stage(directory, panels, stock, profile);
        check("cut staging emits an inch calibration, sheet, source map and hash receipt", result.Staged &&
            result.Files.Length == 3 && result.Parts is [{ PanelId: "A", SourceId: "A", ContourCount: 1 }] &&
            File.Exists(Path.Combine(directory, "CUT-001.dxf")) && File.Exists(Path.Combine(directory, "CALIBRATION.dxf")) &&
            File.Exists(Path.Combine(directory, "cut-staging.json")) && !result.ManufacturingReady &&
            result.NativeImportOutstanding && result.PhysicalChecksOutstanding);
        var independentlyRead = TimberCutFiles.Verify(directory, panels, stock, profile);
        check("independent readback verifies all nominal inches and the staged hashes", independentlyRead.Staged &&
            independentlyRead.Files.SequenceEqual(result.Files));
        string repeat = directory + "-repeat";
        var repeated = TimberCutFiles.Stage(repeat, panels, stock, profile);
        check("repeat staging has identical full DXF and JSON bytes in a different directory", repeated.Staged &&
            result.Files.All(f => File.ReadAllBytes(Path.Combine(directory, f.Name)).SequenceEqual(File.ReadAllBytes(Path.Combine(repeat, f.Name)))));
        string saved = Path.Combine(directory, "CUT-001.dxf");
        byte[] original = File.ReadAllBytes(saved);
        check("staging preserves existing directories and every prior byte", !TimberCutFiles.Stage(directory, panels, stock, profile).Staged &&
            original.SequenceEqual(File.ReadAllBytes(saved)));

        var perforated = TimberFoldStock.Panel("hole", 20, 6) with
        {
            RingsInches = [[new(-2, -1), new(18, -1), new(18, 5), new(-2, 5)],
                [new(0, 0), new(0, 2), new(2, 2), new(2, 0)]]
        };
        var rotatedProfile = new TimberFoldProfile(StockWidthInches: 8, StockHeightInches: 22);
        var rotatedStock = TimberStockLayout.Create([perforated], rotatedProfile);
        string rotatedDirectory = directory + "-rotated";
        var rotated = TimberCutFiles.Stage(rotatedDirectory, [perforated], rotatedStock, rotatedProfile);
        var literalCoordinates = Coordinates(File.ReadAllLines(Path.Combine(rotatedDirectory, "CUT-001.dxf")));
        check("rotated off-origin outer and hole contours retain literal inch coordinates", rotated.Staged &&
            rotated.Parts is [{ ContourCount: 2, Placement.QuarterTurn: true }] &&
            literalCoordinates.SequenceEqual(new double[] { 6.25, .25, 6.25, 20.25, .25, 20.25, .25, .25,
                5.25, 2.25, 3.25, 2.25, 3.25, 4.25, 5.25, 4.25 }) &&
            TimberCutFiles.Verify(rotatedDirectory, [perforated], rotatedStock, rotatedProfile).Staged);

        // Every corruption retains the old receipt; the diagnostic must come from
        // semantic DXF readback before the receipt hash check, not just hashing.
        Corrupt("reader rejects an open cut contour", "70", "0", "Contours must be closed");
        Corrupt("reader rejects unexpected INSERT geometry", "0", "INSERT", "Only CUT LWPOLYLINE");
        Corrupt("reader rejects a relabelled non-CUT contour", "8", "GUIDE", "Contours must be closed");
        Corrupt("reader rejects nonzero bulge", "42", "0.01", "zero elevation", append: true);
        Corrupt("reader rejects nonzero elevation", "38", "0.01", "zero elevation", append: true);
        Corrupt("reader rejects a nonidentity extrusion", "230", "-1", "zero elevation", append: true);
        Corrupt("reader rejects width compensation", "43", "0.01", "zero elevation", append: true);
        Corrupt("reader rejects a shifted canonical vertex", "10", "0.251", "canonical physical-inch");
        Corrupt("reader rejects unknown entity extensions", "999", "untrusted", "Unexpected entity code", append: true);
        HeaderCorrupt("reader rejects millimetre declarations", "$INSUNITS", "4", "header $INSUNITS");
        HeaderCorrupt("reader rejects an unexpected UCS origin", "$UCSORG", "1.0", "header $UCSORG");
        var diagonal = Encoding.ASCII.GetString(original).Split("\r\n");
        int diagonalStart = Array.IndexOf(diagonal, "LWPOLYLINE") + 1;
        for (int i = diagonalStart; i + 1 < diagonal.Length && diagonal[i].Trim() != "0"; i += 2)
            if (diagonal[i].Trim() is "10" or "20")
                diagonal[i + 1] = (double.Parse(diagonal[i + 1], CultureInfo.InvariantCulture) + .000009).ToString("R", CultureInfo.InvariantCulture);
        File.WriteAllText(saved, string.Join("\r\n", diagonal), Encoding.ASCII);
        check("reader rejects diagonal displacement beyond the 1e-5 inch contour gate", TimberCutFiles.Verify(directory, panels, stock, profile) is
            { Staged: false, Diagnostics: var diagonalErrors } && diagonalErrors.Any(d => d.Message.Contains("canonical physical-inch", StringComparison.Ordinal)));
        File.WriteAllBytes(saved, original);
        File.WriteAllText(Path.Combine(directory, "cut-staging.json"), "{}", Encoding.UTF8);
        check("reader rejects a modified source map or hash receipt", !TimberCutFiles.Verify(directory, panels, stock, profile).Staged);
        check("cut staging rejects incomplete placement inventories before writing",
            !TimberCutFiles.Stage(directory + "-bad", panels, stock with { Placements = [] }, profile).Staged && !Directory.Exists(directory + "-bad"));
        string calibration = Path.Combine(repeat, "CALIBRATION.dxf");
        var calibrationLines = File.ReadAllLines(calibration);
        int firstPolyline = Array.IndexOf(calibrationLines, "LWPOLYLINE");
        var squareX = new List<int>();
        for (int i = firstPolyline + 1; i + 1 < calibrationLines.Length && calibrationLines[i].Trim() != "0"; i += 2)
            if (calibrationLines[i].Trim() == "10") squareX.Add(i);
        double maximumX = squareX.Max(i => double.Parse(calibrationLines[i + 1], CultureInfo.InvariantCulture));
        for (int i = firstPolyline + 1; i + 1 < calibrationLines.Length; i += 2)
            if (calibrationLines[i].Trim() == "10" && double.Parse(calibrationLines[i + 1], CultureInfo.InvariantCulture) == maximumX)
            { calibrationLines[i + 1] = (maximumX + .00002).ToString("R", CultureInfo.InvariantCulture); break; }
        File.WriteAllLines(calibration, calibrationLines, Encoding.ASCII);
        check("calibration square fails independently when stretched beyond 1e-5 inch", TimberCutFiles.Verify(repeat, panels, stock, profile) is
            { Staged: false, Diagnostics: var calibrationErrors } && calibrationErrors.Any(d => d.Message.Contains("canonical physical-inch", StringComparison.Ordinal)));
        calibrationLines = File.ReadAllLines(Path.Combine(directory, "CALIBRATION.dxf"));
        firstPolyline = Array.IndexOf(calibrationLines, "LWPOLYLINE");
        double minimumX = squareX.Min(i => double.Parse(calibrationLines[i + 1], CultureInfo.InvariantCulture));
        for (int i = firstPolyline + 1; i + 1 < calibrationLines.Length && calibrationLines[i].Trim() != "0"; i += 2)
            if (calibrationLines[i].Trim() == "10") calibrationLines[i + 1] =
                (double.Parse(calibrationLines[i + 1], CultureInfo.InvariantCulture) == minimumX ? minimumX - .000009 : maximumX + .000009)
                .ToString("R", CultureInfo.InvariantCulture);
        File.WriteAllLines(calibration, calibrationLines, Encoding.ASCII);
        check("calibration measures actual width rather than accepting two endpoint errors", TimberCutFiles.Verify(repeat, panels, stock, profile) is
            { Staged: false, Diagnostics: var widthErrors } && widthErrors.Any(d => d.Message.Contains("one-inch", StringComparison.Ordinal)));
        string cancelledDirectory = directory + "-cancel";
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        bool cancellationObserved = false;
        try { TimberCutFiles.Stage(cancelledDirectory, panels, stock, profile, cancelled.Token); }
        catch (OperationCanceledException) { cancellationObserved = true; }
        check("cut staging observes cancellation before creating any output", cancellationObserved && !Directory.Exists(cancelledDirectory));
        var thickProfile = new TimberFoldProfile(ThicknessInches: 2, JointClearanceInches: .25);
        var thickStock = TimberStockLayout.Create(panels, thickProfile);
        string thickDirectory = directory + "-thick";
        var thick = TimberCutFiles.Stage(thickDirectory, panels, thickStock, thickProfile);
        var thickCoordinates = Coordinates(File.ReadAllLines(Path.Combine(thickDirectory, "CALIBRATION.dxf"))).TakeLast(12).ToArray();
        double cornerMinimumX = thickCoordinates.Where((_, i) => i % 2 == 0).Min();
        double cornerMinimumY = thickCoordinates.Where((_, i) => i % 2 == 1).Min();
        check("corner calibration scales its arms to avoid self-overlap at large thickness", thick.Staged &&
            thickCoordinates.Select((v, i) => v - (i % 2 == 0 ? cornerMinimumX : cornerMinimumY))
                .SequenceEqual(new double[] { 0, 0, 4.5, 0, 4.5, 2.25, 2.25, 2.25, 2.25, 4.5, 0, 4.5 }));
        var smallPanels = new[] { TimberFoldStock.Panel("small", .5, .5) };
        var smallProfile = new TimberFoldProfile(StockWidthInches: 4, StockHeightInches: 4);
        var smallStock = TimberStockLayout.Create(smallPanels, smallProfile);
        string smallDirectory = directory + "-small";
        var small = TimberCutFiles.Stage(smallDirectory, smallPanels, smallStock, smallProfile);
        var smallCoordinates = Coordinates(File.ReadAllLines(Path.Combine(smallDirectory, "CALIBRATION.dxf")));
        check("calibration coupons pack inside valid small stock with its configured border", small.Staged &&
            smallCoordinates.All(v => v >= .25 && v <= 3.75) &&
            smallCoordinates.Take(8).SequenceEqual(new double[] { 1.375, .25, 2.375, .25, 2.375, 1.25, 1.375, 1.25 }));
        var impossibleProfile = smallProfile with { StockWidthInches = 1.25, StockHeightInches = 1.25 };
        var impossibleStock = TimberStockLayout.Create(smallPanels, impossibleProfile);
        string impossibleDirectory = directory + "-impossible";
        var impossible = TimberCutFiles.Stage(impossibleDirectory, smallPanels, impossibleStock, impossibleProfile);
        check("calibration stock failure is explicit and occurs before writing any files", !impossible.Staged &&
            impossible.Diagnostics.Any(d => d.Code == "calibration_stock") && !Directory.Exists(impossibleDirectory));

        void Corrupt(string name, string code, string value, string diagnostic, bool append = false)
        {
            var lines = Encoding.ASCII.GetString(original).Split("\r\n").ToList();
            int entity = lines.FindIndex(s => s == "LWPOLYLINE") - 1;
            if (append) lines.InsertRange(entity + 2, [code, value]);
            else for (int i = entity; i + 1 < lines.Count; i += 2)
                if (lines[i].Trim() == code) { lines[i + 1] = value; break; }
            File.WriteAllText(saved, string.Join("\r\n", lines), Encoding.ASCII);
            var read = TimberCutFiles.Verify(directory, panels, stock, profile);
            check(name, !read.Staged && read.Diagnostics.Any(d => d.Message.Contains(diagnostic, StringComparison.Ordinal)));
            File.WriteAllBytes(saved, original);
        }
        void HeaderCorrupt(string name, string variable, string value, string diagnostic)
        {
            var lines = Encoding.ASCII.GetString(original).Split("\r\n");
            lines[Array.IndexOf(lines, variable) + 2] = value;
            File.WriteAllText(saved, string.Join("\r\n", lines), Encoding.ASCII);
            var read = TimberCutFiles.Verify(directory, panels, stock, profile);
            check(name, !read.Staged && read.Diagnostics.Any(d => d.Message.Contains(diagnostic, StringComparison.Ordinal)));
            File.WriteAllBytes(saved, original);
        }
    }

    private static double[] Coordinates(string[] lines)
    {
        var coordinates = new List<double>(); bool entities = false;
        for (int i = 0; i + 1 < lines.Length; i += 2)
        {
            if (lines[i + 1] == "ENTITIES") entities = true;
            if (lines[i + 1] == "ENDSEC") entities = false;
            if (entities && lines[i].Trim() is "10" or "20") coordinates.Add(double.Parse(lines[i + 1], CultureInfo.InvariantCulture));
        }
        return coordinates.ToArray();
    }
}
