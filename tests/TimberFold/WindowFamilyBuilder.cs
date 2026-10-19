using Autodesk.Revit.DB;

namespace RevitThyme.TimberFold.Fixtures;

internal static class WindowFamilyBuilder
{
    // Width/height and bar dimensions are authored here in feet. The installed Autodesk
    // wall-hosted template supplies hosting infrastructure only, never product geometry.
    public static string Create(Autodesk.Revit.ApplicationServices.Application application, string destination,
        bool opaquePanes, bool thinBar, List<string> warnings)
    {
        string template = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Autodesk", "RVT 2027", "Family Templates", "English-Imperial", "Window.rft");
        if (!File.Exists(template)) throw new FileNotFoundException("Install the English-Imperial Revit 2027 Window family template.", template);
        string familyPath = Path.Combine(destination, "TF Original Two Pane Window.rfa");
        var family = application.NewFamilyDocument(template);
        bool familyClosed = false;
        try
        {
            using (var transaction = OriginalFixtureBuilder.Start(family, "Author original two-pane window", warnings))
            {
                // A template may contain an opening or sample forms. Replace all of those
                // with the explicit recipe so installed content cannot supply glazing.
                var old = new FilteredElementCollector(family).WhereElementIsNotElementType()
                    .Where(e => e is GenericForm or Opening).Select(e => e.Id).ToArray();
                if (old.Length != 0) family.Delete(old);
                var host = new FilteredElementCollector(family).OfClass(typeof(Wall)).Cast<Wall>().Single();
                var manager = family.FamilyManager;
                var previousTypes = manager.Types.Cast<FamilyType>().ToArray();
                var originalType = manager.NewType("48 x 48 - Original");
                foreach (var previousType in previousTypes) { manager.CurrentType = previousType; manager.DeleteCurrentType(); }
                manager.CurrentType = originalType;
                var width = manager.get_Parameter(BuiltInParameter.FAMILY_WIDTH_PARAM);
                var height = manager.get_Parameter(BuiltInParameter.FAMILY_HEIGHT_PARAM);
                if (width is not null && !width.IsReadOnly) manager.Set(width, 4);
                if (height is not null && !height.IsReadOnly) manager.Set(height, 4);
                var glass = (Material)family.GetElement(Material.Create(family, opaquePanes ? "TF Original Opaque Panel" : "TF Original Clear Glass"));
                glass.MaterialClass = opaquePanes ? "Wood" : "Glass";
                glass.Transparency = opaquePanes ? 0 : 85;
                glass.Color = opaquePanes ? new Color(150, 100, 65) : new Color(170, 215, 235);
                var frame = (Material)family.GetElement(Material.Create(family, "TF Original Frame"));
                frame.MaterialClass = "Wood"; frame.Color = new Color(145, 105, 65);
                OriginalFixtureBuilder.Tag(family.FamilyCreate.NewOpening(host,
                    OriginalFixtureBuilder.Polygon([new(-2, 0, 0), new(2, 0, 0), new(2, 0, 4), new(-2, 0, 4)])), "window-4ft-square-opening");
                // On a wall template the X/Z plane is the wall plane; positive Y is
                // the extrusion normal. Both panes have their own native Glass solid.
                var plane = SketchPlane.Create(family, Plane.CreateByNormalAndOrigin(XYZ.BasisY, XYZ.Zero));
                void Box(double x0, double x1, double z0, double z1, double depth, ElementId material, string role)
                {
                    var profile = new CurveArrArray();
                    profile.Append(OriginalFixtureBuilder.Polygon([new(x0, 0, z0), new(x1, 0, z0), new(x1, 0, z1), new(x0, 0, z1)]));
                    var extrusion = family.FamilyCreate.NewExtrusion(true, profile, plane, depth);
                    extrusion.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM).Set(material);
                    OriginalFixtureBuilder.Tag(extrusion, role);
                }
                Box(-2, -1.75, 0, 4, 0.25, frame.Id, "left-frame");
                Box(1.75, 2, 0, 4, 0.25, frame.Id, "right-frame");
                Box(-1.75, 1.75, 0, 0.25, 0.25, frame.Id, "bottom-frame");
                Box(-1.75, 1.75, 3.75, 4, 0.25, frame.Id, "top-frame");
                double halfBar = thinBar ? 0.0625 : 0.125;
                Box(-halfBar, halfBar, 0.25, 3.75, 0.25, frame.Id, "centre-bar");
                Box(-1.75, -halfBar, 0.25, 3.75, 1.0 / 48, glass.Id, "left-pane");
                Box(halfBar, 1.75, 0.25, 3.75, 1.0 / 48, glass.Id, "right-pane");
                family.Regenerate();
                OriginalFixtureBuilder.Commit(transaction);
            }
            family.SaveAs(familyPath, new SaveAsOptions { OverwriteExistingFile = false, Compact = true });
            if (!family.Close(false)) throw new InvalidDataException("Revit did not close the newly saved original window family.");
            familyClosed = true;
            File.WriteAllText(Path.Combine(destination, "window-template-provenance.json"), System.Text.Json.JsonSerializer.Serialize(new
            {
                template, templateSha256 = OriginalFixtureBuilder.Hash(template), familyPath,
                familySha256 = OriginalFixtureBuilder.Hash(familyPath), originalGeometry = "One 4 x 4 ft native hosted opening; five frame extrusions, two pane extrusions.",
                frameWidthFeet = 0.25, centreBarWidthFeet = thinBar ? 0.125 : 0.25, paneThicknessFeet = 1.0 / 48,
                materialClassification = opaquePanes ? "Wood (negative glazing fixture)" : "Glass"
            }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            return familyPath;
        }
        finally
        {
            if (!familyClosed && family.IsValidObject && !family.Close(false))
                throw new InvalidDataException("Revit did not close the owned original window family during cleanup.");
        }
    }
}
