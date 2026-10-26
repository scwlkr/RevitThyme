using System.Numerics;
using RevitThyme.Core;

namespace RevitThyme.Unit;

internal static class TimberRationalRounding
{
    public static void Run(Action<string, bool> check)
    {
        // Expected IEEE-754 neighbors are explicit; the oracle does not call the
        // production conversion/comparison code to decide whether a bound is safe.
        (string Name, BigInteger Numerator, BigInteger Denominator, long Bits)[] cases =
        [
            ("zero", 0, 1, 0),
            ("exact half", 1, 2, 0x3fe0000000000000L),
            ("positive third", 1, 3, 0x3fd5555555555556L),
            ("negative third", -1, 3, unchecked((long)0xbfd5555555555555UL)),
            ("positive tenth", 1, 10, 0x3fb999999999999aL),
            ("negative tenth", -1, 10, unchecked((long)0xbfb9999999999999UL)),
            ("positive half subnormal", 1, BigInteger.One << 1075, 1),
            ("negative half subnormal", -1, BigInteger.One << 1075, long.MinValue),
        ];
        foreach (var item in cases)
        {
            var rational = new TimberPrismRational(item.Numerator, item.Denominator);
            double upper = rational.UpperApproximation();
            check("rational upper bound preserves the expected binary result for " + item.Name,
                BitConverter.DoubleToInt64Bits(upper) == item.Bits);
        }
    }
}
