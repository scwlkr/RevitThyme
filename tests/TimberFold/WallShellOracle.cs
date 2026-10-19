using System.Text.Json;
using Autodesk.Revit.DB;

namespace RevitThyme.TimberFold.Fixtures;

/// <summary>Independent literal footprint/roof coverage checks for the four original buildings.</summary>
internal static class WallShellOracle
{
    private const double ToleranceFeet = 1e-6;

    public static object Inspect(Document doc, string fixture, string? diagnosticsPath)
    {
        var expected = ExpectedWalls(fixture).ToArray();
        var walls = new FilteredElementCollector(doc).OfClass(typeof(Wall)).Cast<Wall>().ToArray();
        var bottomFaces = new FilteredElementCollector(doc).OfClass(typeof(RoofBase)).Cast<RoofBase>()
            .SelectMany(roof => Solids(roof.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Fine }))
                .SelectMany(solid => solid.Faces.Cast<Face>()).OfType<PlanarFace>().Where(face => face.FaceNormal.Z < -1e-7)
                .Select(face => new RoofBottom(roof.Id.Value, face))).ToArray();
        var errors = new List<string>();
        var evidence = new List<object>();
        foreach (var spec in expected)
        {
            var matching = walls.Where(wall => Role(wall) == spec.Role).ToArray();
            if (matching.Length != 1) { errors.Add(spec.Role + ": expected exactly one native wall."); continue; }
            var wall = matching[0];
            var attachedRoofIds = wall.GetAttachmentIds(AttachmentLocation.Top).Select(id => id.Value).ToHashSet();
            var outward = new XYZ(spec.B.X - spec.A.X, spec.B.Y - spec.A.Y, 0).CrossProduct(XYZ.BasisZ).Normalize();
            double orientationDot = wall.Orientation.DotProduct(outward);
            if (orientationDot < 1 - 1e-8) errors.Add(spec.Role + ": native exterior orientation points away from the expected outward direction.");
            var solids = Solids(wall.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Fine })).ToArray();
            var exteriorFaces = solids.SelectMany(solid => solid.Faces.Cast<Face>()).OfType<PlanarFace>()
                .Where(face => face.FaceNormal.DotProduct(outward) > 1 - 1e-8 &&
                    Math.Abs((face.Origin - spec.A).DotProduct(outward)) <= ToleranceFeet).ToArray();
            if (exteriorFaces.Length == 0) errors.Add(spec.Role + ": no actual solid exterior face lies on its approved footprint boundary.");
            var exteriorVertices = Unique(exteriorFaces.SelectMany(Vertices)).ToArray();
            var allVertices = Unique(solids.SelectMany(solid => solid.Faces.Cast<Face>()).SelectMany(Vertices)).ToArray();
            var pointEvidence = new List<object>();
            var topPointEvidence = new List<object>();
            CoveringRoof[] Covering(XYZ point) => bottomFaces.Select(bottom => new { bottom.RoofId, height = BottomHeight(bottom.Face, point) })
                .Where(candidate => candidate.height is not null).Select(candidate => new CoveringRoof(candidate.RoofId,
                    candidate.height!.Value, attachedRoofIds.Contains(candidate.RoofId))).ToArray();
            int unsupportedVertices = 0, aboveRoofVertices = 0, mismatchedTopVertices = 0, fittedTopVertices = 0;
            if (!spec.Parapet)
            {
                foreach (var vertex in allVertices)
                {
                    var coveringRoofs = Covering(vertex);
                    double? roofHeight = coveringRoofs.Length == 0 ? null : coveringRoofs.Max(candidate => candidate.HeightFeet);
                    if (roofHeight is null) unsupportedVertices++;
                    else if (vertex.Z > roofHeight + ToleranceFeet) aboveRoofVertices++;
                    pointEvidence.Add(new { pointFeet = Point(vertex), highestCoveringNativeRoofBottomFeet = roofHeight,
                        amountAboveRoofFeet = roofHeight is null ? (double?)null : vertex.Z - roofHeight, coveringRoofs });
                }
                if (unsupportedVertices != 0 || aboveRoofVertices != 0)
                    errors.Add($"{spec.Role}: orphan native wall geometry; uncovered vertices={unsupportedVertices}, above-roof vertices={aboveRoofVertices}.");
                if (spec.MatchRoofTop)
                {
                    foreach (var vertex in exteriorVertices.Where(p => p.Z >= spec.EaveFeet - ToleranceFeet))
                    {
                        fittedTopVertices++;
                        var coveringRoofs = Covering(vertex);
                        var eligible = attachedRoofIds.Count == 0 ? coveringRoofs : coveringRoofs.Where(candidate => candidate.Attached).ToArray();
                        bool matched = eligible.Any(candidate => Math.Abs(vertex.Z - candidate.HeightFeet) <= ToleranceFeet);
                        if (!matched) mismatchedTopVertices++;
                        topPointEvidence.Add(new { pointFeet = Point(vertex), matched, matchUsesNativeAttachment = attachedRoofIds.Count != 0,
                            coveringRoofs, highestEnvelopeGapFeet = coveringRoofs.Length == 0 ? (double?)null : coveringRoofs.Max(candidate => candidate.HeightFeet) - vertex.Z,
                            nearestEligibleUndersideGapFeet = eligible.Length == 0 ? (double?)null : eligible.Min(candidate => Math.Abs(candidate.HeightFeet - vertex.Z)) });
                    }
                    if (fittedTopVertices < 2 || mismatchedTopVertices != 0)
                        errors.Add($"{spec.Role}: native exterior top does not follow covering native roof undersides; top vertices={fittedTopVertices}, mismatches={mismatchedTopVertices}.");
                }
            }
            evidence.Add(new { role = spec.Role, id = wall.Id.Value, flipped = wall.Flipped, outwardExpected = Point(outward),
                nativeOrientation = Point(wall.Orientation), orientationDot, approvedExteriorBoundary = new[] { Point(spec.A), Point(spec.B) },
                nativeExteriorFaces = exteriorFaces.Select(face => new { originFeet = Point(face.Origin), normal = Point(face.FaceNormal),
                    verticesFeet = Unique(Vertices(face)).Select(Point).ToArray() }).ToArray(),
                parapetHeightExemption = spec.Parapet, spec.MatchRoofTop, spec.EaveFeet, fittedTopVertices, mismatchedTopVertices,
                unsupportedVertices, aboveRoofVertices, attachedRoofIds = attachedRoofIds.Order().ToArray(),
                nativeWallVertexRoofComparisons = pointEvidence, nativeExteriorTopAttachmentComparisons = topPointEvidence });
        }
        var report = new { fixture, passed = errors.Count == 0, toleranceFeet = ToleranceFeet, errors, walls = evidence,
            roofBottomPlaneCount = bottomFaces.Length,
            roofBottomPlanes = bottomFaces.Select(bottom => new { roofId = bottom.RoofId, originFeet = Point(bottom.Face.Origin), normal = Point(bottom.Face.FaceNormal) }).ToArray() };
        if (diagnosticsPath is not null) File.WriteAllText(diagnosticsPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        if (errors.Count != 0) throw new InvalidDataException("Original wall shell failed native orientation/roof coverage checks: " + string.Join(" ", errors));
        return report;
    }

    private static double? BottomHeight(PlanarFace face, XYZ point)
    {
        var n = face.FaceNormal;
        double z = face.Origin.Z - (n.X * (point.X - face.Origin.X) + n.Y * (point.Y - face.Origin.Y)) / n.Z;
        var onPlane = new XYZ(point.X, point.Y, z);
        var projection = face.Project(onPlane);
        if (projection is not null && face.IsInside(projection.UVPoint)) return z;
        foreach (EdgeArray loop in face.EdgeLoops)
        foreach (Edge edge in loop)
        {
            if (edge.AsCurve() is not Line line) continue;
            var a = line.GetEndPoint(0); var b = line.GetEndPoint(1); var delta = b - a;
            double t = Math.Clamp((onPlane - a).DotProduct(delta) / delta.DotProduct(delta), 0, 1);
            if (onPlane.DistanceTo(a + delta * t) <= ToleranceFeet) return z;
        }
        return null;
    }

    private static IEnumerable<WallExpectation> ExpectedWalls(string fixture)
    {
        (double X, double Y)[] rectangle = [(0, 0), (24, 0), (24, 16), (0, 16)];
        (double X, double Y)[] outer = [(0, 0), (28, 0), (28, 20), (0, 20)];
        (double X, double Y)[] setback = [(8, 5), (20, 5), (20, 15), (8, 15)];
        if (fixture == "gable") return Boundary(rectangle, "ground", 10, true);
        if (fixture == "irregular") return Boundary([(0, 0), (24, 0), (24, 28), (12, 28), (12, 16), (0, 16)], "L-shell", 10, true)
            .Select((spec, i) => i == 1 ? spec with { Role = "L-shell-wall-1-profile-spanning-two-roofs" } : spec);
        if (fixture == "multistorey") return Boundary(rectangle, "ground", 10, false).Concat(Boundary(rectangle, "first", 18, true));
        if (fixture == "flat") return Boundary(outer, "ground", 10, true).Concat(Boundary(setback, "setback", 18, true))
            .Concat(Boundary(outer, "terrace-parapet", 10, false, true)).Concat(Boundary(setback, "upper-parapet", 18, false, true));
        return [];
    }

    private static IEnumerable<WallExpectation> Boundary((double X, double Y)[] polygon, string role, double eave, bool fit, bool parapet = false) =>
        Enumerable.Range(0, polygon.Length).Select(i => new WallExpectation(role + "-wall-" + i,
            new(polygon[i].X, polygon[i].Y, 0), new(polygon[(i + 1) % polygon.Length].X, polygon[(i + 1) % polygon.Length].Y, 0), eave, fit, parapet));
    private static string? Role(Element element)
    {
        var comments = element.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString();
        return comments is null ? null : comments[(comments.IndexOf(':') + 1)..];
    }
    private static IEnumerable<XYZ> Vertices(Face face) => face.EdgeLoops.Cast<EdgeArray>().SelectMany(loop => loop.Cast<Edge>())
        .SelectMany(edge => edge.AsCurve().Tessellate());
    private static IEnumerable<XYZ> Unique(IEnumerable<XYZ> points) => points.GroupBy(point =>
        (Math.Round(point.X, 8), Math.Round(point.Y, 8), Math.Round(point.Z, 8))).Select(group => group.First());
    private static IEnumerable<Solid> Solids(GeometryElement? geometry)
    {
        if (geometry is null) yield break;
        foreach (GeometryObject obj in geometry)
        {
            if (obj is Solid { Volume: > 0 } solid) yield return solid;
            if (obj is GeometryInstance instance)
                foreach (var nested in Solids(instance.GetInstanceGeometry())) yield return nested;
        }
    }
    private static double[] Point(XYZ point) => [point.X, point.Y, point.Z];
    private sealed record RoofBottom(long RoofId, PlanarFace Face);
    private sealed record CoveringRoof(long RoofId, double HeightFeet, bool Attached);
    private sealed record WallExpectation(string Role, XYZ A, XYZ B, double EaveFeet, bool MatchRoofTop, bool Parapet);
}
