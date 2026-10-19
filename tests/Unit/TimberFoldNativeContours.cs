using RevitThyme.Addin;
using RevitThyme.Core;

namespace RevitThyme.Unit;

internal static class TimberFoldNativeContours
{
    public static void Run(Action<string, bool> check)
    {
        TimberPoint2[] square = [new(0, 0), new(1, 0), new(1, 1), new(0, 1)];
        TimberPoint2[] bowtie = [new(0, 0), new(.5, 0), new(.500004, .000002), new(.5, .000002), new(.500004, 0), new(1, 0), new(1, 1), new(0, 1)];
        check("native consumer rejects a sub-tolerance self-crossing contour even when edge coverage passes",
            !NativeTimberKitContourCheck.Check(square, bowtie));
        TimberPoint2[] tiny = [new(0, 0), new(.000001, 0), new(.000001, .000001), new(0, .000001)];
        var shifted = tiny.Select(p => new TimberPoint2(p.X + .000004, p.Y)).ToArray();
        check("native consumer accepts finite endpoint coverage inside the conservative distance budget",
            NativeTimberKitContourCheck.Check(tiny, shifted));
        check("native consumer rejects a tiny contour displaced beyond the geometric error gate",
            !NativeTimberKitContourCheck.Check(tiny, tiny.Select(p => new TimberPoint2(p.X + .00002, p.Y)).ToArray()));
        TimberPoint2[] corner = [new(0, 0), new(.00000001, 0), new(1, 1), new(0, 1)];
        TimberPoint2[] collapsed = [new(0, 0), new(1, 1), new(0, 1)];
        check("native consumer accepts a measured collapsed one-hundred-millionth-inch corner only with finite coverage",
            NativeTimberKitContourCheck.Check(corner, collapsed));
        MeasuredReference(check);
        TimberPoint2[] touching = [new(0, 0), new(.5, 0), new(.500004, .000002), new(.5, 0), new(1, 0), new(1, 1), new(0, 1)];
        check("native consumer rejects a nonadjacent contour touch", !NativeTimberKitContourCheck.Check(square, touching));
        TimberPoint2[] segmented = [new(0, 0), new(.5, 0), new(1, 0), new(1, 1), new(.5, 1), new(0, 1)];
        check("native consumer accepts a simple contour with different collinear segmentation", NativeTimberKitContourCheck.Check(square, segmented));
    }
    private static void MeasuredReference(Action<string, bool> check)
    {
        TimberPoint2[] expected = [new(4.25, 5.37600001), new(4.25100001, 5.37600001), new(4.25100001, 5.32037384), new(4.25100001, 2.875),
            new(7.99899999, 2.875), new(7.99899999, 5.37600001), new(8, 5.37600001), new(8, 5.43599998), new(7.99999999, 5.43599998),
            new(6.15843544, 6.35678226), new(6.15843544, 6.35828227), new(6.12650002, 6.37424998), new(4.25000001, 5.43599998), new(4.25, 5.43599998)];
        TimberPoint2[] actual = [new(4.25, 5.376000010000001), new(4.251000010000001, 5.376000010000001), new(4.251000010000001, 5.32037384),
            new(4.251000010000001, 2.8750000000000004), new(7.99899999, 2.8750000000000004), new(7.99899999, 5.376000010000001), new(8, 5.376000010000001),
            new(8, 5.43599998), new(6.158435440000002, 6.356782260000001), new(6.158435440000002, 6.358282270000001), new(6.12650002, 6.374249980000002),
            new(4.250000010000001, 5.43599998), new(4.25, 5.43599998)];
        check("native consumer replays the actual fourteen-to-thirteen-edge reference import without losing a contour",
            NativeTimberKitContourCheck.Check(expected, actual));
    }
}
