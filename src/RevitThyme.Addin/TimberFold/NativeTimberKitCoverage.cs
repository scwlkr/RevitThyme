using System.IO;
using RevitThyme.Core;

namespace RevitThyme.Addin;

internal sealed record NativeTimberKitSegment(TimberPoint2 A, TimberPoint2 B);

internal static class NativeTimberKitCoverage
{
    private const double BudgetInches = .000005;
    internal static bool Covers(IReadOnlyList<NativeTimberKitSegment> source, IReadOnlyList<NativeTimberKitSegment> target,
        CancellationToken ct, Action step)
    {
        foreach (var s in source)
        {
            ct.ThrowIfCancellationRequested(); double length = Length(s);
            if (length <= 1e-12) return false;
            var intervals = new List<(double Low, double High)>(); bool finiteCoverage = false;
            foreach (var t in target)
            {
                step();
                // Distance to a finite segment is convex. If BOTH source endpoints
                // lie in this segment's half-budget capsule, its entire edge does.
                // This proves collapsed corners without accepting arbitrary tiny edges.
                if (FiniteDistanceSquared(s.A, t) <= BudgetInches * BudgetInches &&
                    FiniteDistanceSquared(s.B, t) <= BudgetInches * BudgetInches)
                { finiteCoverage = true; break; }
                double a = Project(t.A), b = Project(t.B);
                if (Distance(t.A, a) <= BudgetInches && Distance(t.B, b) <= BudgetInches)
                {
                    double low = Math.Max(0, Math.Min(a, b)), high = Math.Min(length, Math.Max(a, b));
                    if (high > low) intervals.Add((low, high));
                }
            }
            if (finiteCoverage) continue;
            if (intervals.Count == 0) return false;
            double covered = 0;
            foreach (var interval in intervals.OrderBy(i => i.Low))
            {
                if (interval.Low > covered + BudgetInches) return false;
                covered = Math.Max(covered, interval.High);
            }
            if (covered + BudgetInches < length) return false;
            double Project(TimberPoint2 p) => ((p.X - s.A.X) * (s.B.X - s.A.X) + (p.Y - s.A.Y) * (s.B.Y - s.A.Y)) / length;
            double Distance(TimberPoint2 p, double d) => Math.Sqrt(Math.Pow(p.X - s.A.X - (s.B.X - s.A.X) * d / length, 2) +
                Math.Pow(p.Y - s.A.Y - (s.B.Y - s.A.Y) * d / length, 2));
        }
        return true;
    }
    internal static double Length(NativeTimberKitSegment s) => Math.Sqrt(Math.Pow(s.B.X - s.A.X, 2) + Math.Pow(s.B.Y - s.A.Y, 2));
    private static double FiniteDistanceSquared(TimberPoint2 p, NativeTimberKitSegment s)
    {
        double dx = s.B.X - s.A.X, dy = s.B.Y - s.A.Y, square = dx * dx + dy * dy;
        if (square <= 0) return double.PositiveInfinity;
        double parameter = Math.Clamp(((p.X - s.A.X) * dx + (p.Y - s.A.Y) * dy) / square, 0, 1);
        double x = p.X - s.A.X - parameter * dx, y = p.Y - s.A.Y - parameter * dy;
        return x * x + y * y;
    }
}
