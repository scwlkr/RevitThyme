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
        HundredPartCoverage(check);
        CoverageValidity(check);
        CoverageLimits(check);
    }

    private static void HundredPartCoverage(Action<string, bool> check)
    {
        // Original analytic diagrams: each part contributes six assembled and six
        // exploded rectangular projections. No retained model or private path.
        var drawing = new DxfFile(); drawing.Header.SetDefaults();
        drawing.Header.Version = DxfAcadVersion.R2013; drawing.Header.DefaultDrawingUnits = DxfUnits.Inches;
        for (int part = 0; part < 100; part++)
        for (int projection = 0; projection < 12; projection++)
        {
            double x = part * 96, y = projection * 72;
            var ring = new DxfLwPolyline([new() { X = x, Y = y }, new() { X = x + 48, Y = y },
                new() { X = x + 48, Y = y + 24 }, new() { X = x, Y = y + 24 }])
                { IsClosed = true, Layer = "REFERENCE" };
            drawing.Entities.Add(ring);
        }
        using var stream = new MemoryStream(); drawing.Save(stream); stream.Position = 0;
        var parsed = DxfFile.Load(stream);
        var expected = parsed.Entities.Cast<DxfLwPolyline>().SelectMany(ring => ring.Vertices.Select((p, i) =>
        {
            var next = ring.Vertices[(i + 1) % ring.Vertices.Count];
            return new NativeTimberKitSegment(new(p.X, p.Y), new(next.X, next.Y));
        })).ToArray();
        var actual = expected.Reverse().Select(s => new NativeTimberKitSegment(s.B, s.A)).ToArray();
        long work = 0; bool covered = false;
        try
        {
            bool forward = NativeTimberKitCoverage.Covers(expected, actual, default, Step);
            bool reverse = NativeTimberKitCoverage.Covers(actual, expected, default, Step);
            covered = forward && reverse;
        }
        catch (InvalidDataException error)
        {
            Console.WriteLine($"Native reference scaling: {expected.Length} segments, {work} steps, {error.Message}");
        }
        Console.WriteLine($"Native reference scaling counts: {parsed.Entities.Count} rings, {expected.Length} segments, {work} steps, covered={covered}.");
        check("native reference covers 100-part 4800-segment reordered reversed diagrams both ways within the shared 20M work cap",
            expected.Length == 4800 && covered);
        void Step()
        {
            if (++work > 20000000) throw new InvalidDataException("Reference comparison exceeds its bounded work limit.");
        }
    }

    private static void CoverageValidity(Action<string, bool> check)
    {
        NativeTimberKitSegment whole = new(new(0, 0), new(2, 0));
        NativeTimberKitSegment far = new(new(100, 100), new(101, 100));
        NativeTimberKitSegment[] split = [new(new(2, 0), new(1, 0)), new(new(1, 0), new(0, 0))];
        check("native reference coverage accepts split reversed collinear segments in both directions",
            Covers([whole], split) && Covers(split, [whole]));
        check("native reference coverage rejects missing far-away geometry", !Covers([whole], [far]));
        check("native reference bidirectional coverage rejects an added far-away segment",
            Covers([whole], [whole, far]) && !Covers([whole, far], [whole]));
        NativeTimberKitSegment cap = new(new(2.000001, 0), new(2.000004, 0));
        check("native reference coverage retains finite endpoint caps inside the half-budget",
            Covers([cap], [whole]));
        NativeTimberKitSegment outside = new(new(2.000001, .000006), new(2.000004, .000006));
        check("native reference coverage rejects short edges outside the finite endpoint capsule",
            !Covers([outside], [whole]));

        static bool Covers(NativeTimberKitSegment[] source, NativeTimberKitSegment[] target)
        {
            int work = 0;
            return NativeTimberKitCoverage.Covers(source, target, default, () =>
            {
                if (++work > 1000000) throw new InvalidDataException("Small coverage control exceeded its bounded work limit.");
            });
        }
    }

    private static void CoverageLimits(Action<string, bool> check)
    {
        NativeTimberKitSegment[] source = [new(new(0, 0), new(1, 0))];
        // Every diagonal bounds/intersects the source but cannot cover it. Dense
        // candidates must retain an explicit failure rather than bypass a cap.
        var dense = Enumerable.Range(0, 1024).Select(i => new NativeTimberKitSegment(new(0, -1 - i * .001), new(1, 1 + i * .001))).ToArray();
        int work = 0; bool limited = false;
        try
        {
            NativeTimberKitCoverage.Covers(source, dense, default, () =>
            {
                if (++work > 64) throw new InvalidDataException("Analytic dense coverage work limit.");
            });
        }
        catch (InvalidDataException error) when (error.Message == "Analytic dense coverage work limit.") { limited = true; }
        check("native reference coverage reports exhausted adversarial dense work explicitly", limited && work == 65);

        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        bool stopped = false;
        try { NativeTimberKitCoverage.Covers(source, dense, cancelled.Token, () => { }); }
        catch (OperationCanceledException) { stopped = true; }
        check("native reference coverage honors cancellation before comparison", stopped);

        using var during = new CancellationTokenSource(); int visited = 0; stopped = false;
        try
        {
            NativeTimberKitCoverage.Covers(source, dense, during.Token, () =>
            {
                if (++visited == 128) during.Cancel();
                if ((visited & 127) == 0) during.Token.ThrowIfCancellationRequested();
            });
        }
        catch (OperationCanceledException) { stopped = true; }
        check("native reference coverage remains cancellable during bounded dense comparison", stopped && visited == 128);
    }
}
