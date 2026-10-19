using Q = RevitThyme.Core.TimberPrismRational;
using G = RevitThyme.Core.TimberFragilityGeometry;
using P = RevitThyme.Core.TimberFragilityGeometry.P;

namespace RevitThyme.Core;

internal static class TimberTaperedTips
{
    internal sealed record Witness(int Vertex, double Width, double Depth);
    internal static Witness? Find(P[][] rings, double minimum, G.Budget budget)
    {
        var outer = rings[0]; var threshold = G.Grid(minimum - .00001);
        if (threshold <= 0) return null;
        var depthLow = G.Grid(Math.Max(0, minimum - .00001)); var depthHigh = G.Grid(minimum + .00001);
        for (int i = 0; i < outer.Length; i++)
        {
            budget.Check(); var v = outer[i];
            var a = EndOfStraightChain(outer, i, -1, budget); var b = EndOfStraightChain(outer, i, 1, budget);
            if (G.Cross(v - a, b - v) <= 0) continue;
            var va = a - v; var vb = b - v;
            double al = Math.Sqrt(G.Length2(va).Approximate()), bl = Math.Sqrt(G.Length2(vb).Approximate());
            double dx = va.X.Approximate() / al + vb.X.Approximate() / bl;
            double dy = va.Y.Approximate() / al + vb.Y.Approximate() / bl;
            double dl = Math.Sqrt(dx * dx + dy * dy);
            if (!double.IsFinite(dl) || dl == 0) throw new TimberPrismUnresolved("tip_direction",
                "A convex tip's bisector candidate could not be represented; outer-detail qualification is unresolved.");
            // Floating normalization proposes a section only. Exact tests below prove
            // its finite depth/width/material, using the existing 1e-5 in allowance.
            var offset = new P(Q.From(dx / dl * minimum * 100000000), Q.From(dy / dl * minimum * 100000000));
            var sectionDirection = new P(0 - offset.Y, offset.X);
            var depth2 = G.Length2(offset);
            if (depth2 < depthLow * depthLow || depth2 > depthHigh * depthHigh)
                throw new TimberPrismUnresolved("tip_depth", "A proposed tip section exceeded the existing 0.00001 in depth envelope.");
            var da = G.Cross(va, sectionDirection); var db = G.Cross(vb, sectionDirection);
            if (da == 0 || db == 0) continue;
            var t = G.Cross(offset, sectionDirection) / da; var u = G.Cross(offset, sectionDirection) / db;
            if (t <= 0 || t > 1 || u <= 0 || u > 1) continue;
            var p = v + va * t; var q = v + vb * u; var width2 = G.Length2(q - p);
            if (width2 < threshold * threshold && G.InteriorSection(p, q, rings, budget))
                return new(i, Math.Sqrt(width2.Approximate()) / 100000000, Math.Sqrt(depth2.Approximate()) / 100000000);
        }
        return null;
    }
    private static P EndOfStraightChain(P[] outer, int index, int step, G.Budget budget)
    {
        int current = (index + step + outer.Length) % outer.Length;
        for (int count = 0; count < outer.Length - 2; count++)
        {
            budget.Check(); int next = (current + step + outer.Length) % outer.Length;
            var v = outer[current] - outer[index]; var extension = outer[next] - outer[current];
            if (G.Cross(v, extension) != 0 || G.Dot(v, extension) <= 0) break;
            current = next;
        }
        return outer[current];
    }
}
