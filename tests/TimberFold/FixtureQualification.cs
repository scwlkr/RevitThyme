using Autodesk.Revit.DB;

namespace RevitThyme.TimberFold.Fixtures;

/// <summary>Native oracle: fixed source counts are independent of product extraction.</summary>
public static class FixtureQualification
{
    public static object Inspect(Document document, string fixture, string? diagnosticsPath = null)
    {
        var expected = fixture switch
        {
            "gable" => new Counts(4, 1, 2, 1, 6),
            "irregular" => new Counts(6, 2, 3, 1, 10),
            "multistorey" => new Counts(8, 1, 4, 1, 10),
            "flat" => new Counts(16, 2, 4, 3, 18),
            "curved-wall" or "slanted-wall" or "oversized-wall" => new Counts(1, 0, 0, 0, 1),
            "ambiguous-glazing" or "thin-bar" => new Counts(1, 0, 1, 0, 1),
            "arc-roof" => new Counts(0, 1, 0, 0, 1),
            "phase-scope" => new Counts(2, 0, 0, 0, 2),
            "performance-100" => new Counts(100, 0, 0, 0, 100),
            _ => throw new ArgumentException("Unknown original TimberFold fixture.", nameof(fixture))
        };
        int walls = new FilteredElementCollector(document).OfClass(typeof(Wall)).GetElementCount();
        var roofElements = new FilteredElementCollector(document).OfClass(typeof(RoofBase)).Cast<RoofBase>().ToArray();
        int roofs = roofElements.Length;
        var windows = new FilteredElementCollector(document).OfCategory(BuiltInCategory.OST_Windows)
            .WhereElementIsNotElementType().OfType<FamilyInstance>().ToArray();
        int openings = new FilteredElementCollector(document).OfClass(typeof(Opening)).GetElementCount();
        if ((walls, roofs, windows.Length, openings) != (expected.Walls, expected.Roofs, expected.Windows, expected.Openings))
            throw new InvalidDataException($"Native source inventory differs from {fixture}: walls={walls}, roofs={roofs}, windows={windows.Length}, openings={openings}.");
        // Inspect every actual solid face first. HostObjectUtils references are a
        // separate provider and can fail to resolve even when geometry is present.
        var roofSolidFaces = roofElements.SelectMany(InspectRoofSolids).ToArray();
        var roofFaces = roofSolidFaces.Where(f => f.normalZ > 1e-7).ToArray();
        var roofReferenceDiagnostics = roofElements.Select(roof =>
        {
            var references = HostObjectUtils.GetTopFaces(roof).Select(reference => InspectRoofReference(document, roof, reference)).ToArray();
            return new { roofId = roof.Id.Value, referenceCount = references.Length,
                resolvedReferenceCount = references.Count(r => r.resolved), unresolvedReferenceCount = references.Count(r => !r.resolved), references };
        }).ToArray();
        double expectedRoofArea = fixture switch
        {
            "gable" or "multistorey" => 384 * Math.Sqrt(1.25), // 24*16 ft horizontal footprint, 6:12 slopes
            "irregular" => 624 * Math.Sqrt(1.25), // Independent 24*16 + 12*20 ft roof footprints, deliberately overlapping
            "flat" => 544, // (28*20 - 12*10) + (12*10 - 4*4) square feet
            "arc-roof" => 64 * Math.PI,
            _ => 0
        };
        double actualRoofArea = roofFaces.Sum(f => f.areaSquareFeet);
        double boundaryRoofArea = roofFaces.Sum(f => f.boundaryAreaSquareFeet
            ?? throw new InvalidDataException("An upward roof face has no exact planar boundary area."));
        var roofProfileDiagnostics = roofElements.Select(InspectRoofProfile).ToArray();
        if (diagnosticsPath is not null)
            File.WriteAllText(diagnosticsPath, System.Text.Json.JsonSerializer.Serialize(new
            {
                fixture, expected, actualRoofAreaSquareFeet = actualRoofArea, expectedRoofAreaSquareFeet = expectedRoofArea,
                roofAreaDifferenceSquareFeet = actualRoofArea - expectedRoofArea,
                analyticBoundaryRoofAreaSquareFeet = boundaryRoofArea, analyticBoundaryAreaDifferenceSquareFeet = boundaryRoofArea - expectedRoofArea,
                areaOracle = "Oriented native line/circular-arc boundary integral; RevitAPI.xml documents Face.Area as potentially estimated.",
                expectedModelSlopeRiseOverRun = fixture is "gable" or "irregular" or "multistorey" ? 0.5 : 0,
                roofFaces, roofSolidFaces, roofReferenceDiagnostics, roofProfileDiagnostics
            }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        if (roofFaces.Any(f => !f.planar) || walls + roofFaces.Length != expected.AnalyticShellPanels)
            throw new InvalidDataException($"Actual native roof top-face count/planarity differs: walls={walls}, upwardRoofFaces={roofFaces.Length}, expectedShellPanels={expected.AnalyticShellPanels}.");
        if (Math.Abs(boundaryRoofArea - expectedRoofArea) > 1e-6)
            throw new InvalidDataException($"Native planar boundary roof area differs: analytic={boundaryRoofArea:R}, expected={expectedRoofArea:R}, nativeFaceArea={actualRoofArea:R} square ft; see native-fixture-inspection.json.");
        // Installed RevitAPI.xml, Face.Area: in some cases the returned area is estimated.
        // Keep its previously qualified straight-roof check; use the exact circle
        // boundary integral at the unchanged tolerance for the circular fixture.
        if (fixture != "arc-roof" && Math.Abs(actualRoofArea - expectedRoofArea) > 1e-6)
            throw new InvalidDataException($"Native solid roof-face area differs: actual={actualRoofArea:R}, expected={expectedRoofArea:R}, difference={actualRoofArea - expectedRoofArea:R} square ft; see native-fixture-inspection.json.");
        if (fixture == "flat" && roofFaces.Any(f => f.boundaryLoops != 2))
            throw new InvalidDataException("Both flat roof faces must retain one explicit native opening loop.");
        var hostIds = windows.Select(w => w.Host?.Id.Value ?? -1).ToArray();
        if (hostIds.Any(id => id <= 0)) throw new InvalidDataException("Window is not hosted by a native wall.");
        int[] glassSolids = windows.Select(w => GlassSolids(document, w.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Fine }))).ToArray();
        int expectedGlass = fixture == "ambiguous-glazing" ? 0 : 2;
        if (glassSolids.Any(n => n != expectedGlass))
            throw new InvalidDataException("Native glass solid count differs from the independently specified two-pane family.");
        object wallShell = WallShellOracle.Inspect(document, fixture, diagnosticsPath is null ? null :
            Path.Combine(Path.GetDirectoryName(diagnosticsPath)!, "wall-shell-inspection.json"));
        var mapped = new FilteredElementCollector(document).WhereElementIsNotElementType()
            .Where(e => e is Wall or RoofBase or Opening || e is FamilyInstance fi && fi.Category.Id.Value == (long)BuiltInCategory.OST_Windows)
            .OrderBy(e => e.Id.Value).Select(e => new { id = e.Id.Value, uniqueId = e.UniqueId, category = e.Category?.Name,
                role = e.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString() }).ToArray();
        return new { expected, observed = new { walls, roofs, windows = windows.Length, openings, glassSolids,
                roofFaces, roofSolidFaces, expectedRoofAreaSquareFeet = expectedRoofArea, actualRoofAreaSquareFeet = actualRoofArea,
                analyticBoundaryRoofAreaSquareFeet = boundaryRoofArea,
                roofReferenceDiagnostics, roofProfileDiagnostics, wallShell }, mapped,
            partCountStatus = "Analytic pre-joint shell-panel count only; product contour/source reconciliation must qualify delivered parts independently." };
    }

    private static IEnumerable<NativeRoofFace> InspectRoofSolids(RoofBase roof)
    {
        var geometry = roof.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Fine, ComputeReferences = true })
            ?? throw new InvalidDataException("A native fixture roof has no geometry.");
        int solidIndex = 0;
        foreach (var solid in Solids(geometry))
        {
            int faceIndex = 0;
            foreach (Face face in solid.Faces)
            {
                var uv = face.GetBoundingBox();
                var normal = face is PlanarFace plane ? plane.FaceNormal
                    : face.ComputeNormal(new UV((uv.Min.U + uv.Max.U) / 2, (uv.Min.V + uv.Max.V) / 2));
                var loops = face.EdgeLoops.Cast<EdgeArray>().Select(loop => loop.Cast<Edge>().Select(edge =>
                {
                    var curve = edge.AsCurve();
                    return DescribeCurve(curve);
                }).ToArray()).ToArray();
                yield return new(roof.Id.Value, solidIndex, faceIndex++, face is PlanarFace, face.EdgeLoops.Size, face.Area,
                    PlanarBoundaryOracle.Area(face), normal.X, normal.Y, normal.Z, loops);
            }
            solidIndex++;
        }
    }

    private static object InspectRoofProfile(RoofBase roof)
    {
        var edges = roof is FootPrintRoof footprint ? footprint.GetProfiles().Cast<ModelCurveArray>()
            .SelectMany((loop, loopIndex) => loop.Cast<ModelCurve>().Select((edge, edgeIndex) =>
            {
                bool definesSlope = footprint.get_DefinesSlope(edge);
                return new { loopIndex, edgeIndex, id = edge.Id.Value, definesSlope,
                    nativeSlopeValue = definesSlope ? (double?)footprint.get_SlopeAngle(edge) : null,
                    curveOffsetFeet = footprint.get_Offset(edge),
                    curve = DescribeCurve(edge.GeometryCurve) };
            })).Cast<object>().ToArray() : [];
        return new { roofId = roof.Id.Value, roofType = roof.RoofType.Name,
            compoundWidthFeet = roof.RoofType.GetCompoundStructure()?.GetWidth(),
            baseLevelId = roof.get_Parameter(BuiltInParameter.ROOF_BASE_LEVEL_PARAM)?.AsElementId().Value,
            levelOffsetFeet = roof.get_Parameter(BuiltInParameter.ROOF_LEVEL_OFFSET_PARAM)?.AsDouble(),
            eaveCutValue = roof.get_Parameter(BuiltInParameter.ROOF_EAVE_CUT_PARAM)?.AsInteger(), edges };
    }

    private static double[] Coordinates(XYZ point) => [point.X, point.Y, point.Z];

    private static NativeRoofCurve DescribeCurve(Curve curve)
    {
        var arc = curve as Arc;
        return new(curve.GetType().Name, curve.Tessellate().Select(Coordinates).ToArray(),
            curve.IsBound ? curve.Length : arc is null ? null : 2 * Math.PI * arc.Radius,
            arc?.Radius, arc is null ? null : Coordinates(arc.Center), arc is null ? null : Coordinates(arc.Normal),
            arc is { IsBound: true } ? arc.GetEndParameter(0) : null,
            arc is { IsBound: true } ? arc.GetEndParameter(1) : null);
    }

    private static IEnumerable<Solid> Solids(GeometryElement geometry)
    {
        foreach (GeometryObject obj in geometry)
        {
            if (obj is Solid { Volume: > 0 } solid) yield return solid;
            if (obj is GeometryInstance instance)
                foreach (var child in Solids(instance.GetInstanceGeometry())) yield return child;
        }
    }

    private static NativeRoofReference InspectRoofReference(Document doc, RoofBase roof, Reference reference)
    {
        string? stable = null;
        try
        {
            stable = reference.ConvertToStableRepresentation(doc);
            var geometry = roof.GetGeometryObjectFromReference(reference);
            var face = geometry as Face;
            return new(stable, face is not null, geometry?.GetType().Name, face is PlanarFace,
                face?.EdgeLoops.Size, face?.Area, face is null ? "Provider reference did not resolve to an actual face; see independent full solid inventory." : null);
        }
        catch (Exception error)
        {
            return new(stable, false, null, false, null, null, error.GetType().Name + ": " + error.Message);
        }
    }

    private static int GlassSolids(Document doc, GeometryElement geometry)
    {
        int count = 0;
        foreach (GeometryObject item in geometry)
        {
            if (item is GeometryInstance instance) count += GlassSolids(doc, instance.GetInstanceGeometry());
            if (item is not Solid { Volume: > 0 } solid) continue;
            if (solid.Faces.Cast<Face>().Any(f => doc.GetElement(f.MaterialElementId) is Material material && material.MaterialClass == "Glass")) count++;
        }
        return count;
    }

    public sealed record Counts(int Walls, int Roofs, int Windows, int Openings, int AnalyticShellPanels);
    private sealed record NativeRoofFace(long roofId, int solidIndex, int faceIndex, bool planar, int boundaryLoops, double areaSquareFeet,
        double? boundaryAreaSquareFeet, double normalX, double normalY, double normalZ, NativeRoofCurve[][] loops);
    private sealed record NativeRoofCurve(string curveType, double[][] tessellatedPointsFeet, double? nativeLengthFeet,
        double? arcRadiusFeet, double[]? arcCentreFeet, double[]? arcNormal, double? arcStartParameter, double? arcEndParameter);
    private sealed record NativeRoofReference(string? reference, bool resolved, string? geometryType, bool planar,
        int? boundaryLoops, double? areaSquareFeet, string? error);
}
