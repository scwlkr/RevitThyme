// Run once with dev-modify against a fresh disposable project. The host owns its transaction.
// All scratch files stay beside the copied project. No working model is accessed.
if (doc.IsFamilyDocument || doc.IsWorkshared) throw new Exception("A disposable nonworkshared project is required.");
var runFolder = System.IO.Path.GetDirectoryName(doc.PathName)!;
var familyPath = System.IO.Path.Combine(runFolder, "Parameter Identity Fixture.rfa");
var sharedPath = System.IO.Path.Combine(runFolder, "parameter-identity-shared.txt");
if (System.IO.File.Exists(familyPath) || System.IO.File.Exists(sharedPath))
    throw new Exception("Fixture scratch files already exist. Use a fresh run instead of retrying preparation.");
var template = @"C:\ProgramData\Autodesk\RVT 2027\Family Templates\English-Imperial\Generic Model.rft";
var familyDoc = uiapp.Application.NewFamilyDocument(template);
try
{
    using (var familyTransaction = new Transaction(familyDoc, "Create parameter identity test family"))
    {
        familyTransaction.Start();
        var manager = familyDoc.FamilyManager;
        manager.NewType("Identity Fixture");
        manager.AddParameter("PI Ordinary", GroupTypeId.Data, SpecTypeId.String.Text, true);
        var profile = new CurveArray();
        XYZ[] corners = { new(0,0,0), new(1,0,0), new(1,1,0), new(0,1,0) };
        for (int i = 0; i < 4; i++) profile.Append(Line.CreateBound(corners[i], corners[(i + 1) % 4]));
        var profiles = new CurveArrArray(); profiles.Append(profile);
        var plane = SketchPlane.Create(familyDoc, Plane.CreateByNormalAndOrigin(XYZ.BasisZ, XYZ.Zero));
        familyDoc.FamilyCreate.NewExtrusion(true, profiles, plane, 1);
        familyTransaction.Commit();
    }
    familyDoc.SaveAs(familyPath, new SaveAsOptions { OverwriteExistingFile = false });
}
finally { familyDoc.Close(false); }
Family loadedFamily;
if (!doc.LoadFamily(familyPath, out loadedFamily)) throw new Exception("Fixture family failed to load.");
var symbol = (FamilySymbol)doc.GetElement(loadedFamily.GetFamilySymbolIds().Single());
symbol.Activate(); doc.Regenerate();
var level = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(x => x.Elevation).First();
var element = doc.Create.NewFamilyInstance(new XYZ(0,0,level.Elevation), symbol, level,
    Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
doc.Regenerate();
var builtIn = element.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
if (builtIn == null) throw new Exception("Fixture does not expose native instance Comments.");
var sameName = builtIn.Definition.Name;
builtIn.Set("built-in Comments sentinel");
element.LookupParameter("PI Ordinary").Set("ordinary nonshared sentinel");
System.IO.File.WriteAllText(sharedPath,
    "# Disposable parameter identity fixture\r\n*META\tVERSION\tMINVERSION\r\nMETA\t2\t1\r\n*GROUP\tID\tNAME\r\n*PARAM\tGUID\tNAME\tDATATYPE\tDATACATEGORY\tGROUP\tVISIBLE\tDESCRIPTION\tUSERMODIFIABLE\tHIDEWHENNOVALUE\r\n");
var previousSharedPath = uiapp.Application.SharedParametersFilename;
var firstGuid = new Guid("d9358383-ce0c-4108-91ec-000000000001");
var secondGuid = new Guid("d9358383-ce0c-4108-91ec-000000000002");
var assignedGuid = new Guid("d9358383-ce0c-4108-91ec-000000000003");
var unassignedGuid = new Guid("d9358383-ce0c-4108-91ec-000000000004");
var zeroGuid = new Guid("d9358383-ce0c-4108-91ec-000000000005");
var emptyGuid = new Guid("d9358383-ce0c-4108-91ec-000000000006");
var longGuid = new Guid("d9358383-ce0c-4108-91ec-000000000007");
try
{
    uiapp.Application.SharedParametersFilename = sharedPath;
    var definitionFile = uiapp.Application.OpenSharedParameterFile();
    if (definitionFile == null) throw new Exception("Cannot open disposable shared parameter file.");
    var categories = uiapp.Application.Create.NewCategorySet();
    categories.Insert(doc.Settings.Categories.get_Item(BuiltInCategory.OST_GenericModel));
    var binding = uiapp.Application.Create.NewInstanceBinding(categories);
    void Add(string groupName, string name, Guid guid, ForgeTypeId dataType)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var group = definitionFile.Groups.Create(groupName);
        var options = new ExternalDefinitionCreationOptions(name, dataType)
        { GUID = guid, Visible = true, UserModifiable = true };
        var definition = group.Definitions.Create(options);
        if (!doc.ParameterBindings.Insert(definition, binding, GroupTypeId.Data))
            throw new Exception("Binding failed for fixture field " + groupName + ".");
    }
    Add("First duplicate", sameName, firstGuid, SpecTypeId.String.Text);
    for (int i = 0; i < 22; i++)
    {
        var guid = new Guid("d9358383-ce0c-4108-91ec-" + (100 + i).ToString("D12"));
        Add("Boundary " + i.ToString("D2"), "PI Boundary " + i.ToString("D2"), guid, SpecTypeId.String.Text);
    }
    Add("Second duplicate", sameName, secondGuid, SpecTypeId.String.Text);
    Add("Assigned", "PI Assigned", assignedGuid, SpecTypeId.String.Text);
    Add("Unassigned", "PI Unassigned", unassignedGuid, SpecTypeId.Int.Integer);
    Add("Zero", "PI Zero", zeroGuid, SpecTypeId.Int.Integer);
    Add("Empty", "PI Empty", emptyGuid, SpecTypeId.String.Text);
    Add("Long", "PI Long", longGuid, SpecTypeId.String.Text);
}
finally { uiapp.Application.SharedParametersFilename = previousSharedPath; }
doc.Regenerate();
element.get_Parameter(firstGuid).Set("first shared Comments sentinel");
element.get_Parameter(secondGuid).Set("second shared Comments sentinel");
element.get_Parameter(assignedGuid).Set("assigned sentinel");
element.get_Parameter(zeroGuid).Set(0);
element.get_Parameter(emptyGuid).Set("");
element.get_Parameter(longGuid).Set(new string('L', 600));
doc.Regenerate();
var ordered = element.Parameters.Cast<Parameter>().OrderBy(p => p.Id.Value).ToArray();
int FirstIndex(Guid guid) => Array.FindIndex(ordered, p => p.IsShared && p.GUID == guid);
var firstIndex = FirstIndex(firstGuid); var secondIndex = FirstIndex(secondGuid);
if (firstIndex < 0 || secondIndex - firstIndex < 21) throw new Exception("Fixture duplicate fields do not span pagination.");
var ordinary = element.LookupParameter("PI Ordinary");
if (ordinary.IsShared || ordinary.Definition is not InternalDefinition ordinaryDefinition ||
    ordinaryDefinition.BuiltInParameter != BuiltInParameter.INVALID)
    throw new Exception("Fixture ordinary field does not have the required nonshared/non-built-in identity.");
string Id(Parameter p) => p.Id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
return new
{
    elementUniqueId = element.UniqueId, ordinaryElementUniqueId = element.UniqueId,
    elementId = element.Id.Value, sameName, selectedSharedGuid = firstGuid,
    duplicateSharedGuids = new[] { firstGuid, secondGuid },
    duplicateIndices = new[] { firstIndex, secondIndex },
    longStringParameterId = Id(element.get_Parameter(longGuid)),
    ordinaryParameterId = Id(ordinary), builtInParameterId = Id(builtIn),
    expectedCases = new
    {
        assigned = Id(element.get_Parameter(assignedGuid)), unassigned = Id(element.get_Parameter(unassignedGuid)),
        zero = Id(element.get_Parameter(zeroGuid)), empty = Id(element.get_Parameter(emptyGuid))
    },
    nativeHasValue = new
    {
        assigned = element.get_Parameter(assignedGuid).HasValue, unassigned = element.get_Parameter(unassignedGuid).HasValue,
        zero = element.get_Parameter(zeroGuid).HasValue, empty = element.get_Parameter(emptyGuid).HasValue
    },
    parameterCount = ordered.Length, testValue = "Parameter identity exact ID edit",
    restoredSharedParameterPath = uiapp.Application.SharedParametersFilename == previousSharedPath,
    familyPath, sharedPath
};
