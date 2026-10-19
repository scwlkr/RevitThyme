using Clipper2Lib;
using Q = RevitThyme.Core.TimberPrismRational;

namespace RevitThyme.Core;

// Candidate discovery is deliberately incomplete. Every reported defect has an exact
// certificate on the canonical grid; a missing witness says nothing about strength.
internal static class TimberFragilityGeometry
{
    internal readonly record struct P(Q X, Q Y)
    {
        internal static P From(Point64 p) => new(new(p.X, 1), new(p.Y, 1));
        public static P operator +(P a, P b) => new(a.X + b.X, a.Y + b.Y);
        public static P operator -(P a, P b) => new(a.X - b.X, a.Y - b.Y);
        public static P operator *(P a, Q f) => new(a.X * f, a.Y * f);
    }
    internal sealed class Budget(CancellationToken cancellation)
    {
        private int work;
        internal void Check()
        {
            cancellation.ThrowIfCancellationRequested();
            if (++work > 250000) throw new TimberPrismUnresolved("fragility_work_limit",
                "Outer-detail witness search exceeded 250000 exact steps; fragility remains unresolved.");
        }
    }
    internal static Q Dot(P a, P b) => a.X * b.X + a.Y * b.Y;
    internal static Q Cross(P a, P b) => a.X * b.Y - a.Y * b.X;
    internal static Q Length2(P a) => Dot(a, a);
    internal static Q Grid(double inches) => Q.From(inches) * new Q(100000000, 1);
    internal static P Projection(P p, P a, P b)
    {
        var v = b - a; var t = Dot(p - a, v) / Length2(v);
        return a + v * (t < 0 ? 0 : t > 1 ? 1 : t);
    }
    internal static Q Distance2(P p, P a, P b) => Length2(p - Projection(p, a, b));
    internal static bool On(P p, P a, P b) => Cross(p - a, b - a) == 0 && Dot(p - a, p - b) <= 0;
    internal static bool Inside(P p, IReadOnlyList<P> ring, Budget budget)
    {
        bool inside = false;
        for (int i = 0; i < ring.Count; i++)
        {
            budget.Check(); var a = ring[i]; var b = ring[(i + 1) % ring.Count];
            if (On(p, a, b)) return false;
            if ((a.Y > p.Y) != (b.Y > p.Y) && p.X < a.X + (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y)) inside = !inside;
        }
        return inside;
    }
    internal static bool Material(P p, P[][] rings, Budget budget) => Inside(p, rings[0], budget) &&
        !rings.Skip(1).Any(r => Inside(p, r, budget));
    internal static bool InteriorSection(P p, P q, P[][] rings, Budget budget)
    {
        if (p == q || !Material((p + q) * new Q(1, 2), rings, budget)) return false;
        foreach (var ring in rings)
        for (int i = 0; i < ring.Length; i++)
        {
            budget.Check(); var a = ring[i]; var b = ring[(i + 1) % ring.Length];
            var x = Cross(q - p, a - p); var y = Cross(q - p, b - p);
            var u = Cross(b - a, p - a); var v = Cross(b - a, q - a);
            if (x.Sign * y.Sign < 0 && u.Sign * v.Sign < 0) return false;
            if (On(a, p, q) && a != p && a != q || On(b, p, q) && b != p && b != q) return false;
            if (x == 0 && y == 0 && (On(p, a, b) || On(q, a, b))) return false;
        }
        return true;
    }
    internal static bool Disc(P p, IReadOnlyList<P> lobe, P[][] rings, Q radius2, Budget budget)
    {
        if (!Inside(p, lobe, budget) || !Material(p, rings, budget)) return false;
        foreach (var ring in rings.Append(lobe.ToArray()))
        for (int i = 0; i < ring.Length; i++)
        {
            budget.Check();
            if (Distance2(p, ring[i], ring[(i + 1) % ring.Length]) < radius2) return false;
        }
        return true;
    }
    internal static IEnumerable<P> Witnesses(IReadOnlyList<P> lobe, Budget budget)
    {
        Q area = 0, x = 0, y = 0;
        Q minX = lobe[0].X, maxX = minX, minY = lobe[0].Y, maxY = minY;
        for (int i = 0; i < lobe.Count; i++)
        {
            budget.Check();
            var a = lobe[i]; var b = lobe[(i + 1) % lobe.Count]; var c = Cross(a, b);
            area += c; x += (a.X + b.X) * c; y += (a.Y + b.Y) * c;
            if (a.X < minX) minX = a.X; if (a.X > maxX) maxX = a.X;
            if (a.Y < minY) minY = a.Y; if (a.Y > maxY) maxY = a.Y;
        }
        if (area != 0) yield return new(x / (area * 3), y / (area * 3));
        yield return new((minX + maxX) / 2, (minY + maxY) / 2);
        for (int i = 1; i + 1 < lobe.Count; i++)
        { budget.Check(); yield return (lobe[0] + lobe[i] + lobe[i + 1]) * new Q(1, 3); }
    }
    internal static string Inches(P p) => FormattableString.Invariant($"({p.X.Approximate() / 100000000:G9}, {p.Y.Approximate() / 100000000:G9})");
}
