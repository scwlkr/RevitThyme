using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitThyme.TimberFold.Fixtures;

/// <summary>Test-only native fixture production. No existing document is modified or saved.</summary>
public static partial class OriginalFixtureBuilder
{
    public const string RecipeVersion = "original-shells-v1";
    private static readonly HashSet<string> Cases = ["gable", "irregular", "multistorey", "flat", "curved-wall",
        "slanted-wall", "oversized-wall", "ambiguous-glazing", "thin-bar", "arc-roof", "phase-scope", "performance-100"];
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static object Build(UIApplication uiapp, string destination, string fixture, CancellationToken cancellationToken,
        string builderAssemblyPath, string builderAssemblySha256)
    {
        // Byte-loaded assemblies have an empty Assembly.Location. Verify the supplied
        // original file against the hash of the bytes the runner actually loaded,
        // before creating a directory or opening any new Revit documents.
        if (!Path.IsPathFullyQualified(builderAssemblyPath) || !File.Exists(builderAssemblyPath)
            || builderAssemblySha256 is null || builderAssemblySha256.Length != 64 || !builderAssemblySha256.All(Uri.IsHexDigit)
            || !string.Equals(Hash(builderAssemblyPath), builderAssemblySha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Fixture assembly provenance is missing or changed. Rebuild and regenerate the runner.");
        builderAssemblySha256 = builderAssemblySha256.ToLowerInvariant();
        string projectTemplate = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Autodesk", "RVT 2027", "Templates", "Default_I_ENU.rte");
        if (!File.Exists(projectTemplate)) throw new FileNotFoundException("Install the Revit 2027 default Imperial project template.", projectTemplate);
        string projectTemplateSha256 = Hash(projectTemplate);
        var active = uiapp.ActiveUIDocument?.Document ?? throw new InvalidOperationException("A pinned development document is required.");
        var pinned = Environment.GetEnvironmentVariable("REVITTHYME_FOUNDATION_FIXTURE");
        if (Environment.GetEnvironmentVariable("REVITTHYME_TRUSTED_CODE") != "1" || pinned is null
            || !string.Equals(Path.GetFullPath(active.PathName), Path.GetFullPath(pinned), StringComparison.OrdinalIgnoreCase)
            || active.IsWorkshared || uiapp.Application.Documents.Size != 1)
            throw new InvalidOperationException("Use only a pinned disposable development process with exactly its one baseline document open.");
        if (!Cases.Contains(fixture)) throw new ArgumentException("Unknown fixture. Read tests/TimberFold/README.md.", nameof(fixture));
        if (!Path.IsPathFullyQualified(destination) || Directory.Exists(destination) || File.Exists(destination))
            throw new InvalidOperationException("Supply a new absolute output directory; existing output is never overwritten.");
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(destination);
        string modelPath = Path.Combine(destination, fixture + ".rvt");
        var warnings = new List<string>();
        Document? project = null;
        try
        {
            string? familyPath = null;
            if (fixture is "gable" or "irregular" or "multistorey" or "flat" or "ambiguous-glazing" or "thin-bar")
                familyPath = WindowFamilyBuilder.Create(uiapp.Application, destination, fixture == "ambiguous-glazing", fixture == "thin-bar", warnings);
            cancellationToken.ThrowIfCancellationRequested();
            project = uiapp.Application.NewProjectDocument(projectTemplate);
            if (new FilteredElementCollector(project).WhereElementIsNotElementType().Any(e =>
                e is Wall or RoofBase or Opening or FamilyInstance or Floor or Ceiling or RevitLinkInstance or ImportInstance or DirectShape or Toposolid))
                throw new InvalidDataException("Installed project template contains model geometry; fixture generation requires an empty architectural template.");
            using (var transaction = Start(project, "Original TimberFold " + fixture, warnings))
            {
                FamilySymbol? window = null;
                if (familyPath is not null)
                {
                    if (!project.LoadFamily(familyPath, out var family)) throw new InvalidDataException("Original window family did not load.");
                    window = (FamilySymbol)project.GetElement(family.GetFamilySymbolIds().Single());
                    window.Activate();
                }
                var ground = Level.Create(project, 0); ground.Name = "TF Ground";
                var eave = Level.Create(project, 10); eave.Name = "TF Eaves or First Floor";
                var upper = Level.Create(project, 18); upper.Name = "TF Upper Roof";
                var wallType = (WallType)new FilteredElementCollector(project).OfClass(typeof(WallType)).Cast<WallType>()
                    .First(t => t.Kind == WallKind.Basic).Duplicate("TF Exterior 6 inch");
                wallType.SetCompoundStructure(CompoundStructure.CreateSimpleCompoundStructure(
                    [new CompoundStructureLayer(0.5, MaterialFunctionAssignment.Structure, ElementId.InvalidElementId)]));
                wallType.get_Parameter(BuiltInParameter.FUNCTION_PARAM).Set((int)WallFunction.Exterior);
                var roofType = (RoofType)new FilteredElementCollector(project).OfClass(typeof(RoofType)).Cast<RoofType>()
                    .First(t => t.GetCompoundStructure() is not null).Duplicate("TF Roof 6 inch");
                roofType.SetCompoundStructure(CompoundStructure.CreateSimpleCompoundStructure(
                    [new CompoundStructureLayer(0.5, MaterialFunctionAssignment.Structure, ElementId.InvalidElementId)]));
                var context = new Context(project, ground, wallType, roofType, window, cancellationToken);
                switch (fixture)
                {
                    case "gable": BuildGable(context, eave); break;
                    case "irregular": BuildIrregular(context, eave); break;
                    case "multistorey": BuildMultistorey(context, eave, upper); break;
                    case "flat": BuildFlat(context, eave, upper); break;
                    default: BuildSmall(context, fixture, eave); break;
                }
                AddViews(project, ground);
                project.ProjectInformation.Name = "Original TimberFold " + fixture;
                project.ProjectInformation.Author = "WLKR LABS / RevitThyme contributors";
                project.ProjectInformation.Number = "TF-ORIGINAL-V1-" + fixture.ToUpperInvariant();
                project.ProjectInformation.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.Set(
                    "Procedurally authored qualification model; no copied project or third-party family geometry. Recipe: " + RecipeVersion);
                project.Regenerate();
                Commit(transaction);
            }
            cancellationToken.ThrowIfCancellationRequested();
            object qualification = FixtureQualification.Inspect(project, fixture);
            string geometryFingerprint = GeometryFingerprint(project);
            project.SaveAs(modelPath, new SaveAsOptions { OverwriteExistingFile = false, Compact = true });
            if (!File.Exists(modelPath)) throw new IOException("Native fixture save produced no RVT.");
            string hash = Hash(modelPath);
            var receipt = new
            {
                schema = 1, fixture, recipe = RecipeVersion, nativeFixtureChecksPassed = true,
                provenance = "Original procedural native Revit walls, roofs and family geometry. No sample building or private comparison data used.",
                lengthUnit = "Revit internal feet", originalSourceLicense = "Repository license; Autodesk templates and API binaries are external prerequisites, not redistributed.",
                nativeRevitVersion = uiapp.Application.VersionNumber, nativeRevitBuild = uiapp.Application.VersionBuild,
                builderAssemblyPath, builderAssemblySha256, projectTemplate, projectTemplateSha256, geometryFingerprint, rvtSha256 = hash,
                sourceMap = qualification, modelPath, familyPath, warnings,
                expectedPhysicalBarInchesAt48 = fixture == "thin-bar" ? 0.03125 : 0.0625,
                physicalCuttingAndAssembly = "Outstanding; digital fixture production does not qualify physical fabrication.",
                specification = Description(fixture)
            };
            File.WriteAllText(Path.Combine(destination, "fixture-receipt.json"), JsonSerializer.Serialize(receipt, Json));
            return new { fixture, modelPath, receipt = Path.Combine(destination, "fixture-receipt.json"), rvtSha256 = hash, geometryFingerprint };
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(destination, "fixture-failed.json"), JsonSerializer.Serialize(new { fixture, error = error.ToString(), warnings }, Json));
            throw;
        }
        finally
        {
            if (project is { IsValidObject: true }) project.Close(false);
            if (!active.Equals(uiapp.ActiveUIDocument?.Document)) throw new InvalidOperationException("Active development document changed unexpectedly.");
        }
    }

    private static void AddViews(Document doc, Level level)
    {
        var types = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>().ToArray();
        var plan = ViewPlan.Create(doc, types.First(v => v.ViewFamily == ViewFamily.FloorPlan).Id, level.Id);
        plan.Name = "TimberFold Source Plan";
        var view = View3D.CreateIsometric(doc, types.First(v => v.ViewFamily == ViewFamily.ThreeDimensional).Id);
        view.Name = "TimberFold Source 3D"; view.DetailLevel = ViewDetailLevel.Fine;
    }

    internal static Transaction Start(Document doc, string name, List<string> warnings)
    {
        var transaction = new Transaction(doc, name); transaction.Start();
        transaction.SetFailureHandlingOptions(transaction.GetFailureHandlingOptions()
            .SetFailuresPreprocessor(new Failures(warnings)).SetClearAfterRollback(true).SetForcedModalHandling(false));
        return transaction;
    }
    internal static void Commit(Transaction transaction)
    {
        if (transaction.Commit() != TransactionStatus.Committed) throw new InvalidDataException("Native fixture transaction did not commit.");
    }
    internal static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    internal static void Tag(Element element, string role) => element.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.Set("TF-V1:" + role);
    internal static CurveArray Polygon(IReadOnlyList<XYZ> points)
    {
        var curves = new CurveArray();
        for (int i = 0; i < points.Count; i++) curves.Append(Line.CreateBound(points[i], points[(i + 1) % points.Count]));
        return curves;
    }

    private static string GeometryFingerprint(Document doc)
    {
        var lines = new List<string>();
        string Point(XYZ p) => string.Join(",", new[] { p.X, p.Y, p.Z }.Select(n => Math.Round(n, 8).ToString("F8", CultureInfo.InvariantCulture)));
        void Record(GeometryElement geometry, string role)
        {
            foreach (GeometryObject obj in geometry)
            {
                if (obj is GeometryInstance instance) Record(instance.GetInstanceGeometry(), role);
                if (obj is not Solid { Volume: > 0 } solid) continue;
                foreach (Face face in solid.Faces)
                {
                    var mesh = face.Triangulate();
                    for (int i = 0; i < mesh.NumTriangles; i++)
                    {
                        var triangle = mesh.get_Triangle(i);
                        lines.Add(role + ":" + string.Join("|", Enumerable.Range(0, 3).Select(n => Point(triangle.get_Vertex(n))).Order(StringComparer.Ordinal)));
                    }
                }
            }
        }
        foreach (var element in new FilteredElementCollector(doc).WhereElementIsNotElementType().Where(e => e is Wall or RoofBase or FamilyInstance))
            if (element.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Fine }) is { } geometry)
                Record(geometry, element.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString() ?? element.GetType().Name);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", lines.Order(StringComparer.Ordinal)))));
    }

    private sealed class Failures(List<string> warnings) : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
        {
            foreach (var failure in accessor.GetFailureMessages())
            {
                warnings.Add(failure.GetSeverity() + ": " + failure.GetDescriptionText());
                if (failure.GetSeverity() == FailureSeverity.Warning) accessor.DeleteWarning(failure);
                else return FailureProcessingResult.ProceedWithRollBack;
            }
            return FailureProcessingResult.Continue;
        }
    }
}
