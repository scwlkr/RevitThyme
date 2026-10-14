using System.IO;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitThyme.RevitIntegration;

internal static class Fixture
{
    public static void Create(UIApplication app, string path)
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
            if (transaction.Commit() != TransactionStatus.Committed) throw new InvalidDataException("Fixture creation failed.");
            document.SaveAs(path, new SaveAsOptions { OverwriteExistingFile = false });
            document.Close(false); app.OpenAndActivateDocument(path);
        }
        catch { if (document.IsValidObject) document.Close(false); throw; }
    }
}
