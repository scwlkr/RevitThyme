using Autodesk.Revit.DB;
using RevitThyme.Core;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RevitThyme.Addin;

// Revit objects never escape this adapter. All serialized coordinates remain in feet.
internal static class NativeTimberGeometry
{
    internal static TimberPoint3 Point(XYZ p) => new(p.X, p.Y, p.Z);
    internal static XYZ Vector(TimberPoint3 p) => new(p.X, p.Y, p.Z);
    internal static string Hash(object value) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value)))).ToLowerInvariant();

    internal static IEnumerable<Solid> Solids(GeometryElement geometry)
    {
        foreach (var item in geometry)
        {
            if (item is Solid solid && solid.Faces.Size > 0) yield return solid;
            else if (item is Mesh mesh && mesh.NumTriangles > 0)
                throw new InvalidOperationException("Native geometry contains an unaccounted mesh; planar solid extraction is incomplete.");
            else if (item is GeometryInstance instance)
            {
                using var nested = instance.GetInstanceGeometry();
                foreach (var child in Solids(nested)) yield return child;
            }
        }
    }

    internal static TimberFace Face(PlanarFace face, string source, TimberFaceKind kind,
        double chordFeet, double planarFeet)
    {
        var n = face.FaceNormal.Normalize();
        var u = XYZ.BasisZ.CrossProduct(n);
        if (u.GetLength() < 1e-9) u = XYZ.BasisX;
        u = u.Normalize();
        var v = n.CrossProduct(u).Normalize();
        // Use a deterministic boundary datum rather than Revit's UV origin. Keeping local
        // coordinates near the panel avoids cancellation in georeferenced documents.
        var origin = face.GetEdgesAsCurveLoops().SelectMany(loop => loop.SelectMany(c => Sample(c, chordFeet)))
            .OrderBy(p => p.X).ThenBy(p => p.Y).ThenBy(p => p.Z).FirstOrDefault()
            ?? throw new InvalidOperationException("Native face has no boundary datum.");
        var frame = new TimberFace("", source, kind, Point(origin), Point(u), Point(v), Point(n), []);
        var rings = Rings(face.GetEdgesAsCurveLoops(), frame, chordFeet, planarFeet);
        return frame with { RingsFeet = rings };
    }

    internal static string FaceKey(TimberFace face) => Hash(new
    {
        face.Kind,
        Origin = Rounded(face.OriginFeet), Normal = Rounded(face.Normal),
        Rings = face.RingsFeet.Select(r => r.Select(p => new[] { Math.Round(p.X, 10), Math.Round(p.Y, 10) }))
    });
    private static double[] Rounded(TimberPoint3 p) => [Math.Round(p.X, 10), Math.Round(p.Y, 10), Math.Round(p.Z, 10)];

    internal static TimberPoint2[][] Rings(IEnumerable<CurveLoop> loops, TimberFace frame,
        double chordFeet, double? planarFeet = null, XYZ? projectionDirection = null)
    {
        var origin = Vector(frame.OriginFeet); var u = Vector(frame.U); var v = Vector(frame.V);
        var n = Vector(frame.Normal);
        var rings = new List<TimberPoint2[]>();
        foreach (var loop in loops)
        {
            var points = new List<TimberPoint2>();
            foreach (var curve in loop)
            {
                var sampled = Sample(curve, chordFeet);
                foreach (var p in sampled.Take(sampled.Length - 1))
                {
                    var d = p.Subtract(origin);
                    if (projectionDirection is not null)
                    {
                        var denominator = projectionDirection.DotProduct(n);
                        if (Math.Abs(denominator) < 1e-10)
                            throw new InvalidOperationException("Cutout projection direction is parallel to the host face.");
                        d = d.Subtract(projectionDirection.Multiply(d.DotProduct(n) / denominator));
                    }
                    if (planarFeet.HasValue && Math.Abs(d.DotProduct(n)) > planarFeet.Value)
                        throw new InvalidOperationException("Face boundary is not planar within the configured physical contour tolerance.");
                    var local = new TimberPoint2(d.DotProduct(u), d.DotProduct(v));
                    if (points.Count == 0 || Distance(points[^1], local) > 1e-10) points.Add(local);
                }
            }
            if (points.Count > 1 && Distance(points[0], points[^1]) < 1e-10) points.RemoveAt(points.Count - 1);
            if (points.Count < 3 || Math.Abs(Area(points)) < 1e-12)
                throw new InvalidOperationException("Boundary has fewer than three distinct vertices or negligible projected area.");
            rings.Add(points.ToArray());
        }
        if (rings.Count == 0) throw new InvalidOperationException("Native face has no boundary loops.");
        // Revit does not promise that the first edge loop is the outside.
        var ordered = rings.OrderByDescending(r => Math.Abs(Area(r))).ToArray();
        for (var i = 1; i < ordered.Length; i++)
        {
            if (!Contains(ordered[0], ordered[i][0]) ||
                Enumerable.Range(1, i - 1).Any(j => Contains(ordered[j], ordered[i][0])))
                throw new InvalidOperationException("Face has disconnected or nested exterior rings; cannot assign one panel boundary safely.");
        }
        return new[] { Canonical(ordered[0], true) }
            .Concat(ordered.Skip(1).Select(r => Canonical(r, false)).OrderBy(r => Hash(r), StringComparer.Ordinal)).ToArray();
    }

    internal static TimberPoint2[] Canonical(TimberPoint2[] points, bool positive)
    {
        var ring = (TimberPoint2[])points.Clone();
        if ((Area(ring) > 0) != positive) Array.Reverse(ring);
        var start = Enumerable.Range(0, ring.Length).OrderBy(i => ring[i].X).ThenBy(i => ring[i].Y).First();
        return Enumerable.Range(0, ring.Length).Select(i => ring[(start + i) % ring.Length]).ToArray();
    }

    internal static double Area(IReadOnlyList<TimberPoint2> ring)
    {
        double area = 0;
        for (var i = 0; i < ring.Count; i++)
        { var a = ring[i]; var b = ring[(i + 1) % ring.Count]; area += a.X * b.Y - b.X * a.Y; }
        return area / 2;
    }

    internal static bool Contains(TimberPoint2[] ring, TimberPoint2 p)
    {
        var inside = false;
        for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
        {
            var a = ring[i]; var b = ring[j];
            if ((a.Y > p.Y) != (b.Y > p.Y) && p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X)
                inside = !inside;
        }
        return inside;
    }

    private static double Distance(TimberPoint2 a, TimberPoint2 b) => Math.Sqrt(
        Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));

    internal static XYZ[] Sample(Curve curve, double chordFeet)
    {
        if (curve is Line) return [curve.GetEndPoint(0), curve.GetEndPoint(1)];
        if (curve is not Arc arc) throw new InvalidOperationException(
            $"Unsupported boundary curve {curve.GetType().Name}; only lines and circular arcs are supported.");
        var step = 2 * Math.Acos(Math.Clamp(1 - chordFeet / arc.Radius, -1, 1));
        if (!double.IsFinite(step) || step <= 0) throw new InvalidOperationException("Arc chord tolerance is below numeric resolution.");
        var count = Math.Max(2, Math.Ceiling(arc.Length / arc.Radius / step));
        if (count > 100000) throw new InvalidOperationException("Arc requires more than 100000 segments at the configured tolerance.");
        return Enumerable.Range(0, (int)count + 1).Select(i => arc.Evaluate(i / count, true)).ToArray();
    }
}
