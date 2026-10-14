using System.IO;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitThyme.RevitIntegration;

internal static class UtilityFixture
{
    public static void Create(UIApplication app, string path)
    {
        if (File.Exists(path) || app.Application.Documents.Size != 0) throw new InvalidOperationException("New fixture and empty owned process required.");
        var doc = app.Application.NewProjectDocument(UnitSystem.Imperial);
        using (var t = new Transaction(doc, "Create utilities fixture"))
        {
            t.Start(); var level = Level.Create(doc, 0); level.Name = "Utilities Level";
            Level.Create(doc, 12).Name = "Upper Level";
            Level.Create(doc, 24).Name = "Long Level " + new string('\u2603', 100);
            var types = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>().ToArray();
            var plan = ViewPlan.Create(doc, types.First(t => t.ViewFamily == ViewFamily.FloorPlan).Id, level.Id); plan.Name = "Utilities Plan";
            plan.Scale = 48;
            var ceiling = ViewPlan.Create(doc, types.First(t => t.ViewFamily == ViewFamily.CeilingPlan).Id, level.Id); ceiling.Name = "Utilities Ceiling";
            var templateView = ViewPlan.Create(doc, types.First(t => t.ViewFamily == ViewFamily.FloorPlan).Id, level.Id); templateView.Name = "Template Controlled";
            templateView.ViewTemplateId = plan.CreateViewTemplate().Id;
            View3D.CreateIsometric(doc, types.First(t => t.ViewFamily == ViewFamily.ThreeDimensional).Id).Name = "Utilities 3D";
            using (var r = plan.GetViewRange())
            {
                foreach (var p in new[] { PlanViewPlane.TopClipPlane, PlanViewPlane.CutPlane, PlanViewPlane.BottomClipPlane, PlanViewPlane.ViewDepthPlane }) r.SetLevelId(p, level.Id);
                r.SetOffset(PlanViewPlane.TopClipPlane, 8); r.SetOffset(PlanViewPlane.CutPlane, 4); r.SetOffset(PlanViewPlane.BottomClipPlane, 0); r.SetOffset(PlanViewPlane.ViewDepthPlane, -1);
                plan.SetViewRange(r);
            }
            var wallType = new FilteredElementCollector(doc).OfClass(typeof(WallType)).Cast<WallType>().First(w => w.Kind == WallKind.Basic);
            void Room(string name, XYZ origin, double angle, bool irregular = false)
            {
                double c = Math.Cos(angle), s = Math.Sin(angle);
                XYZ P(double x, double y) => origin + new XYZ(c * x - s * y, s * x + c * y, 0);
                XYZ[] points = irregular ? [P(0, 0), P(14, 0), P(14, 8), P(8, 8), P(8, 16), P(0, 16)] : [P(0, 0), P(14, 0), P(14, 16), P(0, 16)];
                for (int i = 0; i < points.Length; i++) Wall.Create(doc, Line.CreateBound(points[i], points[(i + 1) % points.Length]), wallType.Id, level.Id, 9, 0, false, false);
                doc.Regenerate(); XYZ center = irregular ? P(4, 4) : P(7, 8);
                var room = doc.Create.NewRoom(level, new UV(center.X, center.Y)); room.Name = name;
            }
            Room("Rectangle", XYZ.Zero, 0); Room("Rotated Rectangle", new XYZ(30, 0, 0), Math.PI / 6); Room("L Shape", new XYZ(60, 0, 0), 0, true);
            doc.LoadFamily(@"C:\ProgramData\Autodesk\RVT 2027\Libraries\English-Imperial\US\Annotations\Room Tag.rfa");
            var firstRoom = new FilteredElementCollector(doc).OfClass(typeof(SpatialElement)).OfType<Autodesk.Revit.DB.Architecture.Room>().First(r => r.Name.StartsWith("Rectangle", StringComparison.Ordinal));
            doc.Create.NewRoomTag(new LinkElementId(firstRoom.Id), new UV(7, 8), plan.Id); doc.Regenerate();
            if (t.Commit() != TransactionStatus.Committed) throw new InvalidDataException("Fixture creation failed.");
        }
        doc.SaveAs(path, new SaveAsOptions { OverwriteExistingFile = false }); doc.Close(false); app.OpenAndActivateDocument(path);
    }
}
