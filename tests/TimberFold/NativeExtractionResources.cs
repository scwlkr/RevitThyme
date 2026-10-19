using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitThyme.TimberFold.Fixtures;

// Native regression at the installed public extraction seam. The caller must use
// dev-query so its owned current-document transaction rolls back these probes.
public static class NativeExtractionResources
{
    public static object Check(UIApplication app, string retainedGeneration, CancellationToken ct)
    {
        var doc = app.ActiveUIDocument?.Document ?? throw new InvalidOperationException("Pinned project required.");
        string? pinned = Environment.GetEnvironmentVariable("REVITTHYME_FOUNDATION_FIXTURE");
        if (Environment.GetEnvironmentVariable("REVITTHYME_TRUSTED_CODE") != "1" || pinned is null || doc.IsWorkshared ||
            !doc.IsModifiable || !Path.GetFullPath(pinned).Equals(Path.GetFullPath(doc.PathName), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Use dev-query against the exact owned disposable project.");
        string runs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RevitThyme", "TimberFold", "runs") + Path.DirectorySeparatorChar;
        if (!Path.IsPathFullyQualified(retainedGeneration) || !Path.GetFullPath(retainedGeneration).StartsWith(runs, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Owned retained generation inputs required.");
        var addin = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "RevitThyme.Addin");
        var core = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "RevitThyme.Core");
        var options = (JsonSerializerOptions)core.GetType("RevitThyme.Core.Protocol", true)!.GetField("Json")!.GetValue(null)!;
        var planType = core.GetType("RevitThyme.Core.TimberNativeKitPlan", true)!;
        var plan = JsonSerializer.Deserialize(File.ReadAllBytes(Path.Combine(retainedGeneration, "native-plan.json")), planType, options)!;
        object Property(object item, string name) => item.GetType().GetProperty(name)!.GetValue(item)!;
        object target = Property(Property(plan, "SourceContext"), "Target");
        var extract = addin.GetType("RevitThyme.Addin.NativeTimberExtraction", true)!.GetMethod("Extract", BindingFlags.Public | BindingFlags.Static)!;
        JsonElement Capture()
        {
            ct.ThrowIfCancellationRequested();
            var snapshot = extract.Invoke(null, [doc, target, Property(plan, "Profile"), Property(plan, "Scope")])!;
            return JsonSerializer.SerializeToElement(snapshot, snapshot.GetType(), options);
        }
        static string[] Sources(JsonElement snapshot) => snapshot.GetProperty("sources").EnumerateArray()
            .Select(s => s.GetProperty("id").GetString()!).Order(StringComparer.Ordinal).ToArray();
        string savedHash = Hash(doc.PathName);
        long viewId = doc.ActiveView.Id.Value;
        var before = Capture();
        var material = doc.GetElement(Material.Create(doc, "TF resource regression " + Guid.NewGuid().ToString("N")))!;
        doc.Regenerate();
        var afterMaterial = Capture();
        bool materialIsMetadata = Sources(before).SequenceEqual(Sources(afterMaterial)) &&
            before.GetProperty("documentFingerprint").GetString() == afterMaterial.GetProperty("documentFingerprint").GetString() &&
            before.GetProperty("faces").GetRawText() == afterMaterial.GetProperty("faces").GetRawText() &&
            before.GetProperty("openings").GetRawText() == afterMaterial.GetProperty("openings").GetRawText();
        var sheet = ViewSheet.Create(doc, ElementId.InvalidElementId);
        doc.Regenerate();
        var afterSheet = Capture();
        bool sheetIsDocumentation = Sources(before).SequenceEqual(Sources(afterSheet)) &&
            before.GetProperty("documentFingerprint").GetString() == afterSheet.GetProperty("documentFingerprint").GetString() &&
            before.GetProperty("faces").GetRawText() == afterSheet.GetProperty("faces").GetRawText() &&
            before.GetProperty("openings").GetRawText() == afterSheet.GetProperty("openings").GetRawText();
        var cutPath = Path.Combine(retainedGeneration, "cutting", "CUT-001.dxf");
        using var stage = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(retainedGeneration, "cutting", "cut-staging.json")));
        var cut = Get(stage.RootElement, "files").EnumerateArray().Single(f => Get(f, "name").GetString() == "CUT-001.dxf");
        if (!Hash(cutPath).Equals(Get(cut, "sha256").GetString(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Retained probe cutting input hash differs from its stage.");
        var drafting = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>().First(t => t.ViewFamily == ViewFamily.Drafting);
        var view = ViewDrafting.Create(doc, drafting.Id); view.Scale = 1;
        using var importOptions = new DWGImportOptions { Unit = ImportUnit.Default, Placement = ImportPlacement.Origin, ThisViewOnly = true,
            OrientToView = false, AutoCorrectAlmostVHLines = false };
        if (!doc.Import(cutPath, importOptions, view, out var importId)) throw new InvalidOperationException("Native regression import failed.");
        doc.Regenerate();
        var afterImport = Capture();
        string importIdentity = importId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        bool unknownImportBlocks = afterImport.GetProperty("sources").EnumerateArray().Any(s => s.GetProperty("id").GetString() == importIdentity &&
            s.GetProperty("disposition").GetString() == "Unsupported") && afterImport.GetProperty("diagnostics").EnumerateArray()
            .Any(d => d.GetProperty("sourceId").GetString() == importIdentity && d.GetProperty("code").GetString() == "unsupported_architecture");
        return new
        {
            passed = materialIsMetadata && sheetIsDocumentation && unknownImportBlocks && doc.ActiveView.Id.Value == viewId &&
                savedHash == Hash(doc.PathName),
            materialIsMetadata, sheetIsDocumentation, unknownImportBlocks, materialId = material.Id.Value, sheetId = sheet.Id.Value, importId = importId.Value,
            beforeSources = Sources(before).Length, afterMaterialSources = Sources(afterMaterial).Length,
            afterSheetSources = Sources(afterSheet).Length,
            addedMaterialSourceIds = Sources(afterMaterial).Except(Sources(before)).ToArray(),
            addedSheetSourceIds = Sources(afterSheet).Except(Sources(afterMaterial)).ToArray(),
            callerMustVerifyQueryRollback = true, installedAddin = addin.Location,
            installedAddinSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(addin.Location)))
        };
    }
    private static JsonElement Get(JsonElement value, string name) => value.EnumerateObject()
        .Single(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)).Value;
    private static string Hash(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
