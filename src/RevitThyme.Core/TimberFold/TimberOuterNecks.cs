using Q = RevitThyme.Core.TimberPrismRational;
using G = RevitThyme.Core.TimberFragilityGeometry;
using P = RevitThyme.Core.TimberFragilityGeometry.P;

namespace RevitThyme.Core;

internal static class TimberOuterNecks
{
    internal sealed record Witness(int FirstEdge, int SecondEdge, P CutStart, P CutEnd, P FirstDisc, P SecondDisc, double Width);
    internal static Witness? Find(P[][] rings, double minimum, G.Budget budget)
    {
        var outer = rings[0]; var limit = G.Grid(minimum - .00001); var radius = G.Grid(minimum) / 2;
        if (limit <= 0) return null;
        bool concave = false;
        for (int i = 0; i < outer.Length; i++)
        {
            budget.Check();
            if (G.Cross(outer[i] - outer[(i + outer.Length - 1) % outer.Length], outer[(i + 1) % outer.Length] - outer[i]) < 0) concave = true;
        }
        if (!concave) return null;
        for (int i = 0; i < outer.Length; i++)
        for (int j = i + 2; j < outer.Length; j++)
        {
            budget.Check(); if (i == 0 && j == outer.Length - 1) continue;
            var a = outer[i]; var b = outer[(i + 1) % outer.Length]; var c = outer[j]; var d = outer[(j + 1) % outer.Length];
            foreach (var (p, q) in Candidates(a, b, c, d))
            {
                budget.Check(); var length2 = G.Length2(q - p);
                if (length2 == 0 || length2 >= limit * limit || !G.InteriorSection(p, q, rings, budget)) continue;
                var first = Lobe(outer, i, j, p, q, budget); var second = Lobe(outer, j, i, q, p, budget);
                var firstDisc = Disc(first); var secondDisc = firstDisc is not null ? Disc(second) : null;
                if (firstDisc is { } w && secondDisc is { } z)
                    return new(i, j, p, q, w, z, Math.Sqrt(length2.Approximate()) / 100000000);
            }
        }
        return null;
        P? Disc(P[] lobe)
        {
            foreach (var w in G.Witnesses(lobe, budget)) if (G.Disc(w, lobe, rings, radius * radius, budget)) return w;
            return null;
        }
    }
    private static IEnumerable<(P, P)> Candidates(P a, P b, P c, P d)
    {
        // Parallel finite edges need an interior connector, not merely an endpoint
        // which may sit on the adjacent boundary and cannot partition the material.
        if (G.Cross(b - a, d - c) == 0)
        {
            var v = b - a; var length2 = G.Length2(v);
            var tc = G.Dot(c - a, v) / length2; var td = G.Dot(d - a, v) / length2;
            var lo = tc < td ? tc : td; var hi = tc > td ? tc : td;
            if (lo < 0) lo = 0; if (hi > 1) hi = 1;
            if (lo < hi) { var p = a + v * ((lo + hi) / 2); yield return (p, G.Projection(p, c, d)); }
        }
        yield return (a, G.Projection(a, c, d)); yield return (b, G.Projection(b, c, d));
        yield return (G.Projection(c, a, b), c); yield return (G.Projection(d, a, b), d);
    }
    private static P[] Lobe(P[] outer, int first, int last, P p, P q, G.Budget budget)
    {
        var points = new List<P> { p };
        for (int k = (first + 1) % outer.Length; ; k = (k + 1) % outer.Length)
        { budget.Check(); if (points[^1] != outer[k]) points.Add(outer[k]); if (k == last) break; }
        if (points[^1] != q) points.Add(q);
        if (points[^1] == points[0]) points.RemoveAt(points.Count - 1);
        return points.ToArray();
    }
}
