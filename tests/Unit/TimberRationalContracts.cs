using System.Numerics;
using RevitThyme.Core;
using Q = RevitThyme.Core.TimberPrismRational;

namespace RevitThyme.Unit;

internal static class TimberRationalContracts
{
    public static void Run(Action<string, bool> check)
    {
        bool Is(Q value, BigInteger numerator, BigInteger denominator) => value.Numerator == numerator && value.Denominator == denominator;
        void Reject(string name, string code, Action action)
        {
            bool rejected = false;
            try { action(); } catch (TimberPrismUnresolved error) { rejected = error.Code == code; }
            check(name, rejected);
        }
        check("rational normalizes a negative denominator", Is(new(1, -2), -1, 2));
        check("rational normalizes two negative signs", Is(new(-1, -2), 1, 2));
        check("rational reduces common factors", Is(new(6, 9), 2, 3));
        check("zero and default rationals have unit denominators", Is(new(0, -7), 0, 1) && Is(default, 0, 1) && default(Q).Sign == 0);
        Reject("zero divisor is rejected", "degenerate_geometry", () => _ = new Q(1, 0));
        BigInteger envelope = (BigInteger.One << 8192) - 1;
        check("8192-bit numerator is accepted", Is(new(envelope, 1), envelope, 1));
        check("8192-bit denominator is accepted", Is(new(1, envelope), 1, envelope));
        Reject("8193-bit numerator is rejected independently", "arithmetic_limit", () => _ = new Q(envelope + 1, 1));
        Reject("8193-bit denominator is rejected independently", "arithmetic_limit", () => _ = new Q(1, envelope + 1));
        check("bit envelope applies after canonical reduction", Is(new(envelope + 1, envelope + 1), 1, 1));
        foreach (double value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, Math.BitIncrement(1e12), -Math.BitIncrement(1e12) })
            Reject("binary input outside the finite component envelope: " + value, "invalid_geometry", () => _ = Q.From(value));
        check("positive component envelope boundary is accepted", Is(Q.From(1e12), 1000000000000, 1));
        check("negative component envelope boundary is accepted", Is(Q.From(-1e12), -1000000000000, 1));
        check("binary zero is canonical", Is(Q.From(0), 0, 1) && Is(Q.From(-0.0), 0, 1));
        check("normal binary inputs retain exact values", Is(Q.From(1.5), 3, 2) && Is(Q.From(-1.5), -3, 2));
        check("smallest subnormals retain exact values", Is(Q.From(double.Epsilon), 1, BigInteger.One << 1074) && Is(Q.From(-double.Epsilon), -1, BigInteger.One << 1074));
        check("a rounded binary third retains its supplied bits", Is(Q.From(1.0 / 3), 6004799503160661, 18014398509481984));
        check("approximation of canonical zero remains zero", default(Q).Approximate() == 0);
        check("approximation of signed thirds rounds to nearest binary value", new Q(1, 3).Approximate() == 1.0 / 3 && new Q(-1, 3).Approximate() == -1.0 / 3);
        check("large numerator and denominator approximate without intermediate overflow", new Q((BigInteger.One << 200) + 1, (BigInteger.One << 201) + 3).Approximate() == .5);
        Q half = new(1, 2), third = new(1, 3), equal = new(2, 4);
        check("rational addition is exact", Is(half + third, 5, 6));
        check("rational subtraction is exact", Is(half - third, 1, 6));
        check("rational multiplication is exact", Is(half * third, 1, 6));
        check("rational division is exact", Is(half / third, 3, 2));
        Reject("division by a zero rational is rejected", "degenerate_geometry", () => _ = half / default(Q));
        Q integer = -2;
        check("implicit integer has a unit denominator", Is(integer, -2, 1));
        check("less-than distinguishes less, equal and greater", third < half && !(half < equal) && !(half < third));
        check("less-or-equal distinguishes less, equal and greater", third <= half && half <= equal && !(half <= third));
        check("greater-than distinguishes greater, equal and less", half > third && !(half > equal) && !(third > half));
        check("greater-or-equal distinguishes greater, equal and less", half >= third && half >= equal && !(third >= half));
        check("typed value equality follows the canonical rational", half.Equals(equal) && !half.Equals(third) && !half.Equals(new Q(1, 3)) && !half.Equals(new Q(3, 2)));
        check("object equality rejects null and other types", half.Equals((object)equal) && !half.Equals(null) && !half.Equals("half"));
        check("equality operators distinguish values", half == equal && !(half == third) && half != third && !(half != equal));
        check("equal canonical rationals have equal hashes", half.GetHashCode() == equal.GetHashCode());
        check("comparison returns the correct order", half.CompareTo(third) > 0 && half.CompareTo(equal) == 0 && third.CompareTo(half) < 0);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        var cancelledBudget = new TimberPrismBudget(cancelled.Token);
        Reject("cancelled arithmetic work is rejected", "cancelled", () => cancelledBudget.Check());
        Reject("cancelled candidate work is rejected", "cancelled", () => cancelledBudget.Candidate());
        check("cancellation does not record a candidate", cancelledBudget.Candidates == 0);
        var work = new TimberPrismBudget(default);
        work.Check(2000000); work.Check(0);
        check("exact work envelope is accepted", true);
        Reject("work above the envelope is rejected", "work_limit", () => work.Check());
        var candidates = new TimberPrismBudget(default);
        for (int i = 0; i < 100000; i++) candidates.Candidate();
        check("exact candidate envelope is accepted and counted", candidates.Candidates == 100000);
        Reject("candidate above the envelope is rejected", "candidate_limit", () => candidates.Candidate());
    }
}
