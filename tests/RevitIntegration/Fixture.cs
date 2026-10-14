using System.IO;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitThyme.RevitIntegration;

internal static class Fixture
{
    public static void Create(UIApplication app, string path, bool mcp = false)
    {
        if (File.Exists(path) || app.Application.Documents.Size != 0)
            throw new InvalidOperationException("Only a new fixture in an empty test process is allowed.");
        var document = app.Application.NewProjectDocument(UnitSystem.Imperial);
        try
        {
            using var transaction = new Transaction(document, "Create Foundation test fixture"); transaction.Start();
            var level = Level.Create(document, 0); level.Name = "Foundation Level";
            var type = new FilteredElementCollector(document).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                .First(t => t.ViewFamily == ViewFamily.FloorPlan);
            var view = ViewPlan.Create(document, type.Id, level.Id); view.Name = "Foundation Plan";
            var second = ViewPlan.Create(document, type.Id, level.Id); second.Name = "Occupied Name";
            if (mcp)
            {
                var wallType = new FilteredElementCollector(document).OfClass(typeof(WallType)).Cast<WallType>().First(t => t.Kind == WallKind.Basic);
                for (int i = 0; i < 2; i++)
                {
                    var wall = Wall.Create(document, Line.CreateBound(new XYZ(0, i * 15, 0), new XYZ(10, i * 15, 0)), wallType.Id, level.Id, 9, 0, false, false);
                    wall.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS).Set(i == 0 ? "Original A" : "Original B");
                    wall.get_Parameter(BuiltInParameter.WALL_ATTR_ROOM_BOUNDING).Set(1);
                }
            }
            if (transaction.Commit() != TransactionStatus.Committed) throw new InvalidDataException("Fixture creation failed.");
            document.SaveAs(path, new SaveAsOptions { OverwriteExistingFile = false });
            document.Close(false); app.OpenAndActivateDocument(path);
        }
        catch { if (document.IsValidObject) document.Close(false); throw; }
    }
}
