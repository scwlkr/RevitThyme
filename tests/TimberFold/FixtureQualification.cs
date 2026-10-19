using Autodesk.Revit.DB;

namespace RevitThyme.TimberFold.Fixtures;

/// <summary>Native oracle: fixed source counts are independent of product extraction.</summary>
public static class FixtureQualification
{
    public static object Inspect(Document document, string fixture)
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
        var roofFaces = roofElements.SelectMany(roof => HostObjectUtils.GetTopFaces(roof).Select(reference =>
        {
            var face = (Face)roof.GetGeometryObjectFromReference(reference);
            return new { roofId = roof.Id.Value, reference = reference.ConvertToStableRepresentation(document),
                planar = face is PlanarFace, boundaryLoops = face.EdgeLoops.Size, areaSquareFeet = face.Area };
        })).ToArray();
        if (roofFaces.Any(f => !f.planar) || walls + roofFaces.Length != expected.AnalyticShellPanels)
            throw new InvalidDataException("Actual native roof top-face count/planarity differs from the analytic shell expectation.");
        if (fixture == "flat" && roofFaces.Any(f => f.boundaryLoops != 2))
            throw new InvalidDataException("Both flat roof faces must retain one explicit native opening loop.");
        if (fixture == "arc-roof" && Math.Abs(roofFaces.Single().areaSquareFeet - 64 * Math.PI) > 1e-6)
            throw new InvalidDataException("Circular roof top-face area differs from the independent 64*pi square-foot oracle.");
        var hostIds = windows.Select(w => w.Host?.Id.Value ?? -1).ToArray();
        if (hostIds.Any(id => id <= 0)) throw new InvalidDataException("Window is not hosted by a native wall.");
        int[] glassSolids = windows.Select(w => GlassSolids(document, w.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Fine }))).ToArray();
        int expectedGlass = fixture == "ambiguous-glazing" ? 0 : 2;
        if (glassSolids.Any(n => n != expectedGlass))
            throw new InvalidDataException("Native glass solid count differs from the independently specified two-pane family.");
        var mapped = new FilteredElementCollector(document).WhereElementIsNotElementType()
            .Where(e => e is Wall or RoofBase or Opening || e is FamilyInstance fi && fi.Category.Id.Value == (long)BuiltInCategory.OST_Windows)
            .OrderBy(e => e.Id.Value).Select(e => new { id = e.Id.Value, uniqueId = e.UniqueId, category = e.Category?.Name,
                role = e.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString() }).ToArray();
        return new { expected, observed = new { walls, roofs, windows = windows.Length, openings, glassSolids, roofFaces }, mapped,
            partCountStatus = "Analytic pre-joint shell-panel count only; product contour/source reconciliation must qualify delivered parts independently." };
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
}
