using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitThyme.Lab;
internal static class Fixture
{
    // Only the separately approved startup setting calls this. Never overwrite or open a source model.
    public static UIDocument Create(UIApplication app, string path)
    {
        LabConfig.PlainPath(path);
        if (File.Exists(path) || app.Application.Documents.Size != 0)
            throw new InvalidDataException("Fixture creation requires a new empty Revit session and an unused path.");
        var doc = app.Application.NewProjectDocument(UnitSystem.Imperial);
        try
        {
            using var transaction = new Transaction(doc, "Create approved RevitThyme test fixture");
            transaction.Start();
            var level = Level.Create(doc, 0); level.Name = "Lab Level 1";
            var upper = Level.Create(doc, 10); upper.Name = "Lab Level 2";
            var type = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                .First(t => t.ViewFamily == ViewFamily.FloorPlan);
            var view = ViewPlan.Create(doc, type.Id, level.Id); view.Name = "RevitThyme Lab Plan";
            using var range = view.GetViewRange();
            var planes = new[] { PlanViewPlane.TopClipPlane, PlanViewPlane.CutPlane, PlanViewPlane.BottomClipPlane, PlanViewPlane.ViewDepthPlane };
            double[] offsets = [8, 4, 0, -1];
            for (int i = 0; i < planes.Length; i++) { range.SetLevelId(planes[i], level.Id); range.SetOffset(planes[i], offsets[i]); }
            view.SetViewRange(range);
            var points = new[] { new XYZ(0, 0, 0), new XYZ(12, 0, 0), new XYZ(12, 1, 0), new XYZ(0, 1, 0) };
            var loop = new CurveLoop();
            for (int i = 0; i < points.Length; i++) loop.Append(Line.CreateBound(points[i], points[(i + 1) % points.Length]));
            var solid = GeometryCreationUtilities.CreateExtrusionGeometry([loop], XYZ.BasisZ, 9);
            var shape = DirectShape.CreateElement(doc, new ElementId(BuiltInCategory.OST_GenericModel));
            shape.Name = "Lab geometry"; shape.SetShape([solid]);
            if (transaction.Commit() != TransactionStatus.Committed) throw new InvalidDataException("Fixture transaction did not commit.");
            doc.SaveAs(path, new SaveAsOptions { OverwriteExistingFile = false });
            doc.Close(false); // Close only our new saved fixture so it can be activated through the UI API.
            return app.OpenAndActivateDocument(path);
        }
        catch
        {
            if (doc.IsValidObject) doc.Close(false);
            throw; // Any file created before a failure is retained for review.
        }
    }
}
