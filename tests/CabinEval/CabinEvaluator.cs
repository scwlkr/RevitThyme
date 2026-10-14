using Autodesk.Revit.DB;

namespace RevitThyme.CabinEval;

public sealed record ScoreItem(string Area, string Name, int Maximum, bool Passed, string Evidence);
public sealed record CabinReport(int Score, int Maximum, ScoreItem[] Items, bool ProbeRestored);

// Independent of submitted source, returned values, and the execution implementation.
// Qualified fixture contract: one cabin, wall centerline rectangle 14 x 16 feet, 9-foot walls.
public static class CabinEvaluator
{
    public static CabinReport Evaluate(Document doc)
    {
        var items = new List<ScoreItem>();
        void Award(string area, string name, int points, bool passed, object evidence) => items.Add(new(area, name, points, passed, evidence.ToString() ?? ""));
        var walls = Elements<Wall>(doc); var floors = Elements<Floor>(doc); var roofs = Elements<FootPrintRoof>(doc);
        var instances = Elements<FamilyInstance>(doc); var dimensions = Elements<Dimension>(doc);
        double[] extent = Extent(walls);
        Award("Geometry", "14 x 16 centerline rectangle and 9-foot walls", 10, Rectangle(walls, 14, 16), string.Join(",", extent));
        Award("Geometry", "Native floor covers footprint", 5, floors.Length == 1 && Close(floors[0].get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED).AsDouble(), 224), $"floors={floors.Length}");
        Award("Geometry", "Native gable roof has two opposing equal slopes", 10, roofs.Length == 1 && Gable(roofs[0]), $"roofs={roofs.Length}");
        Award("BIM", "Four native basic walls with valid types", 5, walls.Length == 4 && walls.All(w => w.WallType.Kind == WallKind.Basic), $"walls={walls.Length}");
        var doors = Category(instances, BuiltInCategory.OST_Doors); var windows = Category(instances, BuiltInCategory.OST_Windows);
        Award("BIM", "Hosted native door", 5, doors.Length == 1 && doors.All(f => f.Host is Wall && Volume(f) > 0), $"doors={doors.Length}");
        Award("BIM", "Two hosted windows of distinct native types", 5, windows.Length == 2 && windows.Select(w => w.GetTypeId()).Distinct().Count() == 2 && windows.All(w => w.Host is Wall && Volume(w) > 0), $"windows={windows.Length}");
        var electrical = instances.Where(f => f.Category?.Id.Value is (long)BuiltInCategory.OST_ElectricalFixtures or (long)BuiltInCategory.OST_LightingDevices).ToArray();
        Award("BIM", "Electrical fixtures/devices with native connectors", 5, electrical.Length >= 2 && electrical.All(f => f.MEPModel?.ConnectorManager?.Connectors.Size > 0), $"electrical={electrical.Length}");
        var appliances = Category(instances, BuiltInCategory.OST_SpecialityEquipment);
        Award("BIM", "Two distinct native appliances with geometry", 5, appliances.Length >= 2 && appliances.Select(a => a.Symbol.Family.Id).Distinct().Count() >= 2 && appliances.All(a => Volume(a) > 0), $"appliances={appliances.Length}");
        var parameter = doc.GetElement(GlobalParametersManager.FindByName(doc, "Cabin Length")) as GlobalParameter;
        bool parameterValid = parameter?.GetValue() is DoubleParameterValue v && Close(v.Value, 14) && !parameter.IsReporting && parameter.GetLabeledDimensions().Count > 0;
        Award("Parametric", "Native length parameter drives a dimension", 5, parameterValid, parameter?.Name ?? "missing");
        var originalIds = Ids(doc); bool stretched = false, floorFollows = false, roofFollows = false, hostedFollows = false, dimensionsFollow = false;
        string probeError = ""; bool restored;
        using (var group = new TransactionGroup(doc, "Cabin Eval temporary 14 to 18 foot probe"))
        {
            if (group.Start() != TransactionStatus.Started) throw new InvalidOperationException("Cannot start independent probe.");
            try
            {
                if (parameterValid)
                {
                    using var transaction = new Transaction(doc, "Change only Cabin Length"); transaction.Start();
                    parameter!.SetValue(new DoubleParameterValue(18)); doc.Regenerate();
                    stretched = Rectangle(walls, 18, 16);
                    floorFollows = floors.Length == 1 && Close(floors[0].get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED).AsDouble(), 288);
                    roofFollows = roofs.Length == 1 && Close(Span(roofs[0], 0), 18, 0.1) && Gable(roofs[0]);
                    hostedFollows = stretched && windows.Length == 2 && windows.All(w => w.Host is Wall wall && Math.Abs(((LocationPoint)w.Location).Point.X - Midpoint(wall).X) < 0.1);
                    dimensionsFollow = dimensions.Any(d => Close(d.Value ?? 0, 18)) && dimensions.Any(d => Close(d.Value ?? 0, 16));
                    if (transaction.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Probe commit failed.");
                }
            }
            catch (Exception e) { probeError = e.Message; stretched = floorFollows = roofFollows = hostedFollows = dimensionsFollow = false; }
            finally { if (group.RollBack() != TransactionStatus.RolledBack) throw new InvalidOperationException("Independent probe rollback failed."); }
        }
        restored = Ids(doc).SequenceEqual(originalIds) && Extent(Elements<Wall>(doc)).Zip(extent).All(p => Close(p.First, p.Second))
            && (!parameterValid || parameter!.GetValue() is DoubleParameterValue original && Close(original.Value, 14));
        Award("Parametric", "Walls respond to length 18, retain depth and height", 10, stretched, probeError);
        Award("Parametric", "Floor follows length", 5, floorFollows, "temporary native area 288 sq ft");
        Award("Parametric", "Roof follows length", 5, roofFollows, "temporary native roof bounds and slopes");
        Award("Parametric", "Hosted windows follow moved wall", 5, hostedFollows, "native host and location");
        Award("Parametric", "Associative dimensions read 18 and 16", 5, dimensionsFollow, "native dimension values");
        Award("Integrity", "No native warnings", 5, walls.Length > 0 && doc.GetWarnings().Count == 0, $"warnings={doc.GetWarnings().Count}");
        Award("Integrity", "Physical wall/floor/roof solids have volume", 5, walls.Length == 4 && floors.Length == 1 && roofs.Length == 1 && walls.Cast<Element>().Concat(floors).Concat(roofs).All(e => Volume(e) > 0), "native solids");
        Award("Integrity", "Probe restores native IDs, bounds and parameter", 5, restored && parameterValid, "rollback independently checked");
        return new(items.Where(i => i.Passed).Sum(i => i.Maximum), items.Sum(i => i.Maximum), items.ToArray(), restored);
    }
    private static T[] Elements<T>(Document doc) where T : Element => new FilteredElementCollector(doc).OfClass(typeof(T)).Cast<T>().ToArray();
    private static FamilyInstance[] Category(FamilyInstance[] all, BuiltInCategory category) => all.Where(f => f.Category?.Id.Value == (long)category).ToArray();
    private static long[] Ids(Document doc) => new FilteredElementCollector(doc)
        .WherePasses(new LogicalOrFilter(new ElementIsElementTypeFilter(), new ElementIsElementTypeFilter(true)))
        .ToElementIds().Select(i => i.Value).Order().ToArray();
    private static bool Close(double a, double b, double tolerance = 1e-4) => Math.Abs(a - b) < tolerance;
    private static XYZ Midpoint(Wall w) => ((LocationCurve)w.Location).Curve.Evaluate(0.5, true);
    private static double[] Extent(Wall[] walls)
    {
        var p = walls.SelectMany(w => new[] { ((LocationCurve)w.Location).Curve.GetEndPoint(0), ((LocationCurve)w.Location).Curve.GetEndPoint(1) }).ToArray();
        return p.Length == 0 ? [0, 0] : [p.Max(p => p.X) - p.Min(p => p.X), p.Max(p => p.Y) - p.Min(p => p.Y)];
    }
    private static bool Rectangle(Wall[] walls, double x, double y)
    {
        var extent = Extent(walls);
        return walls.Length == 4 && Close(extent[0], x) && Close(extent[1], y) && walls.All(w => Close(w.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM).AsDouble(), 9))
            && walls.Count(w => Close(((LocationCurve)w.Location).Curve.Length, x)) == 2 && walls.Count(w => Close(((LocationCurve)w.Location).Curve.Length, y)) == 2;
    }
    private static bool Gable(FootPrintRoof roof)
    {
        var slopes = roof.GetProfiles().Cast<ModelCurveArray>().SelectMany(a => a.Cast<ModelCurve>()).Where(roof.get_DefinesSlope).ToArray();
        return slopes.Length == 2 && roof.get_SlopeAngle(slopes[0]) > 0 && Close(roof.get_SlopeAngle(slopes[0]), roof.get_SlopeAngle(slopes[1]))
            && Math.Abs(((Line)slopes[0].GeometryCurve).Direction.DotProduct(((Line)slopes[1].GeometryCurve).Direction)) > 0.99 && Volume(roof) > 0;
    }
    private static double Span(Element e, int axis) { var box = e.get_BoundingBox(null); return axis == 0 ? box.Max.X - box.Min.X : box.Max.Y - box.Min.Y; }
    private static double Volume(Element e)
    {
        double Read(GeometryElement? geometry) => geometry?.Sum(o => o is Solid s ? s.Volume : o is GeometryInstance i ? Read(i.GetInstanceGeometry()) : 0) ?? 0;
        return Read(e.get_Geometry(new Options()));
    }
}
