using System.Numerics;
using RevitThyme.Core;

namespace RevitThyme.Addin;

// Exact predicates for finite binary-double coordinates. Rescaling all coordinates
// to a shared power of two preserves every bit without choosing a geometric epsilon.
internal static class NativeTimberKitTopology
{
    private readonly record struct Point(BigInteger X, BigInteger Y);
    internal static bool IsSimple(IReadOnlyList<TimberPoint2> ring, CancellationToken ct, Action budget)
    {
        var binary = ring.SelectMany(p => new[] { Binary(p.X), Binary(p.Y) }).ToArray();
        int exponent = binary.Where(b => !b.Mantissa.IsZero).Select(b => b.Exponent).DefaultIfEmpty(0).Min();
        var points = Enumerable.Range(0, ring.Count).Select(i => new Point(Integer(binary[2 * i]), Integer(binary[2 * i + 1]))).ToArray();
        if (points.Distinct().Count() != points.Length) return false;
        BigInteger area = 0;
        for (int i = 0; i < points.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            var a = points[i]; var b = points[(i + 1) % points.Length]; var c = points[(i + 2) % points.Length];
            area += a.X * b.Y - a.Y * b.X;
            if (Orient(a, b, c).IsZero && (b.X - a.X) * (c.X - b.X) + (b.Y - a.Y) * (c.Y - b.Y) < 0) return false;
            for (int j = i + 1; j < points.Length; j++)
            {
                budget();
                if (j == i + 1 || i == 0 && j == points.Length - 1) continue;
                if (Intersects(a, b, points[j], points[(j + 1) % points.Length])) return false;
            }
        }
        return !area.IsZero;
        BigInteger Integer((BigInteger Mantissa, int Exponent) value) => value.Mantissa.IsZero ? BigInteger.Zero : value.Mantissa << (value.Exponent - exponent);
    }
    private static bool Intersects(Point a, Point b, Point c, Point d)
    {
        var x = Orient(a, b, c); var y = Orient(a, b, d); var z = Orient(c, d, a); var w = Orient(c, d, b);
        return x.IsZero && On(a, b, c) || y.IsZero && On(a, b, d) || z.IsZero && On(c, d, a) || w.IsZero && On(c, d, b) ||
            x.Sign * y.Sign < 0 && z.Sign * w.Sign < 0;
    }
    private static BigInteger Orient(Point a, Point b, Point c) => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
    private static bool On(Point a, Point b, Point p) => p.X >= BigInteger.Min(a.X, b.X) && p.X <= BigInteger.Max(a.X, b.X) &&
        p.Y >= BigInteger.Min(a.Y, b.Y) && p.Y <= BigInteger.Max(a.Y, b.Y);
    private static (BigInteger Mantissa, int Exponent) Binary(double value)
    {
        long bits = BitConverter.DoubleToInt64Bits(value); int exponent = (int)((bits >> 52) & 0x7ff);
        long mantissa = bits & 0x000fffffffffffffL;
        if (exponent != 0) mantissa |= 1L << 52;
        return (bits < 0 ? -new BigInteger(mantissa) : new BigInteger(mantissa), exponent == 0 ? -1074 : exponent - 1075);
    }
}
