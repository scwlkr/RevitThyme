// Trusted reference baseline, executed through run_modify in a fresh disposable fixture.
// Geometry uses wall centerlines at x=0/14, y=0/16. Host owns the transaction.
var level = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.Elevation).First();
var wallType = new FilteredElementCollector(doc).OfClass(typeof(WallType)).Cast<WallType>().First(w => w.Kind == WallKind.Basic);
XYZ[] points = { new(0,0,0), new(14,0,0), new(14,16,0), new(0,16,0) };
var walls = new List<Wall>();
var loop = new CurveLoop(); var roofLoop = new CurveArray();
for (int i=0; i<4; i++) {
    cancellationToken.ThrowIfCancellationRequested();
    var edge = Line.CreateBound(points[i], points[(i+1)%4]); loop.Append(edge); roofLoop.Append(edge);
    walls.Add(Wall.Create(doc, edge, wallType.Id, level.Id, 9, 0, false, false));
}
walls[3].Pinned = true;
var floorType = new FilteredElementCollector(doc).OfClass(typeof(FloorType)).Cast<FloorType>().First();
var floor = Floor.Create(doc, new List<CurveLoop> { loop }, floorType.Id, level.Id);
var roofType = new FilteredElementCollector(doc).OfClass(typeof(RoofType)).Cast<RoofType>().First();
ModelCurveArray roofEdges = new();
var roof = doc.Create.NewFootPrintRoof(roofLoop, level, roofType, out roofEdges);
roof.get_Parameter(BuiltInParameter.ROOF_LEVEL_OFFSET_PARAM).Set(9);
foreach (ModelCurve edge in roofEdges) {
    bool slope = Math.Abs(edge.GeometryCurve.GetEndPoint(1).Y - edge.GeometryCurve.GetEndPoint(0).Y) < 0.01;
    roof.set_DefinesSlope(edge, slope); if(slope) roof.set_SlopeAngle(edge, Math.Atan(0.5));
}
string library = @"C:\ProgramData\Autodesk\RVT 2027\Libraries\English-Imperial\US\";
FamilySymbol Load(string relative) {
    Family family; if (!doc.LoadFamily(library + relative, out family)) throw new Exception("Family load failed: " + relative);
    var symbol = (FamilySymbol)doc.GetElement(family.GetFamilySymbolIds().OrderBy(id => id.Value).First());
    symbol.Activate(); doc.Regenerate(); return symbol;
}
var door = Load(@"Doors\Door-Single-Panel.rfa");
doc.Create.NewFamilyInstance(new XYZ(4,0,0), door, walls[0], level, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
var window = Load(@"Windows\Window-Fixed.rfa");
var secondWindow = (FamilySymbol)window.Duplicate("Cabin alternate window");
secondWindow.get_Parameter(BuiltInParameter.WINDOW_WIDTH).Set(2.5); secondWindow.Activate();
doc.Create.NewFamilyInstance(new XYZ(14,5,3), window, walls[1], level, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
doc.Create.NewFamilyInstance(new XYZ(14,11,3), secondWindow, walls[1], level, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
var receptacle = Load(@"Electrical\MEP\Electric Power\Terminals\Duplex Receptacle.rfa");
var lightSwitch = Load(@"Electrical\MEP\Electric Power\Terminals\Switch.rfa");
doc.Create.NewFamilyInstance(new XYZ(8,0,1.5), receptacle, walls[0], level, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
doc.Create.NewFamilyInstance(new XYZ(6,0,4), lightSwitch, walls[0], level, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
var refrigerator = Load(@"Specialty Equipment\Domestic\Refrigerator-Built-in.rfa");
var range = Load(@"Specialty Equipment\Domestic\Mid-Range\Range-Electric.rfa");
doc.Create.NewFamilyInstance(new XYZ(2,12,0), refrigerator, level, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
doc.Create.NewFamilyInstance(new XYZ(6,12,0), range, level, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
doc.Regenerate();
Reference Center(Wall wall) => Reference.ParseFromStableRepresentation(doc, wall.UniqueId + ":-9999:1");
var refs = new ReferenceArray(); refs.Append(Center(walls[3])); refs.Append(Center(walls[1]));
var lengthDimension = doc.Create.NewDimension(doc.ActiveView, Line.CreateBound(new XYZ(0,-3,0), new XYZ(14,-3,0)), refs);
var length = GlobalParameter.Create(doc, "Cabin Length", SpecTypeId.Length); length.LabelDimension(lengthDimension.Id); length.SetValue(new DoubleParameterValue(14));
refs = new ReferenceArray(); refs.Append(Center(walls[0])); refs.Append(Center(walls[2]));
doc.Create.NewDimension(doc.ActiveView, Line.CreateBound(new XYZ(-3,0,0), new XYZ(-3,16,0)), refs);
doc.Regenerate();
return new { wallCount = walls.Count, lengthParameter = length.Id.Value, floor = floor.Id.Value, roof = roof.Id.Value };
