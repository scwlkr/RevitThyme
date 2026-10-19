using System.Numerics;

namespace RevitThyme.Core;

// Exact arithmetic for the supplied finite binary geometry, not a claim about extraction error.
internal readonly struct TimberPrismRational : IComparable<TimberPrismRational>, IEquatable<TimberPrismRational>
{
    internal BigInteger Numerator { get; }
    private readonly BigInteger denominator;
    internal BigInteger Denominator => denominator.IsZero ? BigInteger.One : denominator;
    internal int Sign => Numerator.Sign;
    internal TimberPrismRational(BigInteger numerator, BigInteger divisor)
    {
        if (divisor.IsZero) throw new TimberPrismUnresolved("degenerate_geometry", "Exact geometry produced a zero divisor.");
        if (divisor.Sign < 0) { numerator = -numerator; divisor = -divisor; }
        var common = BigInteger.GreatestCommonDivisor(BigInteger.Abs(numerator), divisor);
        Numerator = numerator / common; denominator = divisor / common;
        if (BigInteger.Abs(Numerator).GetBitLength() > 8192 || denominator.GetBitLength() > 8192)
            throw new TimberPrismUnresolved("arithmetic_limit", "Exact prism arithmetic exceeded the 8192-bit resource envelope.");
    }
    internal static TimberPrismRational From(double value)
    {
        if (!double.IsFinite(value) || Math.Abs(value) > 1e12)
            throw new TimberPrismUnresolved("invalid_geometry", "Prism inputs must be finite and within the 1e12 component envelope.");
        long bits = BitConverter.DoubleToInt64Bits(value);
        long mantissa = bits & 0x000fffffffffffffL;
        int exponent = (int)((bits >> 52) & 0x7ff);
        if (exponent != 0) mantissa |= 1L << 52;
        int power = exponent == 0 ? -1074 : exponent - 1075;
        BigInteger numerator = bits < 0 ? -new BigInteger(mantissa) : new BigInteger(mantissa);
        return power >= 0 ? new(numerator << power, 1) : new(numerator, BigInteger.One << -power);
    }
    internal double Approximate()
    {
        if (Sign == 0) return 0;
        var n = BigInteger.Abs(Numerator); var d = Denominator;
        int ns = (int)Math.Max(0, n.GetBitLength() - 53), ds = (int)Math.Max(0, d.GetBitLength() - 53);
        return Sign * Math.ScaleB((double)(n >> ns) / (double)(d >> ds), ns - ds);
    }
    public int CompareTo(TimberPrismRational other) => (Numerator * other.Denominator).CompareTo(other.Numerator * Denominator);
    public bool Equals(TimberPrismRational other) => Numerator == other.Numerator && Denominator == other.Denominator;
    public override bool Equals(object? obj) => obj is TimberPrismRational other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Numerator, Denominator);
    public static implicit operator TimberPrismRational(int value) => new(value, 1);
    public static TimberPrismRational operator +(TimberPrismRational a, TimberPrismRational b) => new(a.Numerator * b.Denominator + b.Numerator * a.Denominator, a.Denominator * b.Denominator);
    public static TimberPrismRational operator -(TimberPrismRational a, TimberPrismRational b) => new(a.Numerator * b.Denominator - b.Numerator * a.Denominator, a.Denominator * b.Denominator);
    public static TimberPrismRational operator *(TimberPrismRational a, TimberPrismRational b) => new(a.Numerator * b.Numerator, a.Denominator * b.Denominator);
    public static TimberPrismRational operator /(TimberPrismRational a, TimberPrismRational b) => new(a.Numerator * b.Denominator, a.Denominator * b.Numerator);
    public static bool operator ==(TimberPrismRational a, TimberPrismRational b) => a.Equals(b);
    public static bool operator !=(TimberPrismRational a, TimberPrismRational b) => !a.Equals(b);
    public static bool operator <(TimberPrismRational a, TimberPrismRational b) => a.CompareTo(b) < 0;
    public static bool operator >(TimberPrismRational a, TimberPrismRational b) => a.CompareTo(b) > 0;
    public static bool operator <=(TimberPrismRational a, TimberPrismRational b) => a.CompareTo(b) <= 0;
    public static bool operator >=(TimberPrismRational a, TimberPrismRational b) => a.CompareTo(b) >= 0;
}

internal sealed class TimberPrismUnresolved(string code, string message) : Exception(message)
{ internal string Code { get; } = code; }

internal sealed class TimberPrismBudget(CancellationToken cancellation)
{
    private int work;
    internal long Candidates { get; private set; }
    internal void Check(int amount = 1)
    {
        if (cancellation.IsCancellationRequested) throw new TimberPrismUnresolved("cancelled", "Assembly verification was cancelled; no zero-overlap result is inferred.");
        work += amount;
        if (work > 2000000) throw new TimberPrismUnresolved("work_limit", "Assembly verification exceeded its 2000000-step work envelope.");
    }
    internal void Candidate()
    {
        Check(); Candidates++;
        if (Candidates > 100000) throw new TimberPrismUnresolved("candidate_limit", "Assembly verification exceeded 100000 candidate cell pairs.");
    }
}
