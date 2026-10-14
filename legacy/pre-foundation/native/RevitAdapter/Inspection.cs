using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using RevitThyme.Native;
using Category = RevitThyme.Native.Category;

namespace RevitThyme.RevitAdapter;
public sealed partial class Model
{
    private const int ScanLimit = 20_000;
    private Document InspectDocument()
    {
        var doc = app.ActiveUIDocument?.Document ?? throw new Rejection("no_document", "Open the approved project.");
        host.AssertDocument(doc);
        if (doc.IsFamilyDocument) throw new Rejection("family_document", "Model inspection requires a project document.");
        if (doc.IsModifiable) throw new Rejection("active_edit", "Finish the active transaction before inspection.");
        return doc;
    }
    private Target InspectTarget(Document doc) => new(host.ProcessId, host.StartTicks, host.SessionId,
        host.DocumentId(doc), doc.ActiveView.UniqueId, host.Revision);
    public ModelContext Context()
    {
        var doc = InspectDocument();
        return new(InspectTarget(doc), Text(doc.Title), Text(doc.ActiveView.Name), doc.ActiveView.ViewType.ToString(),
            doc.IsModified, doc.IsReadOnly, doc.IsWorkshared,
            System.Diagnostics.FileVersionInfo.GetVersionInfo(typeof(Document).Assembly.Location).FileVersion ?? app.Application.VersionBuild, "ft");
    }
    public ElementPage Elements(Target target, ReadQuery query)
    {
        query.Validate();
        var doc = InspectDocument();
        if (InspectTarget(doc) != target) throw new Rejection("stale_target", "Process, document, view or revision changed. Read context again.");
        if (query.Category == Category.Warnings) return Warnings(doc, target, query);
        using var collector = Collector(doc, query.Category);
        var ids = query.Ids.Length > 0 ? query.Ids.Select(id => new ElementId(long.Parse(id))).ToArray()
            : collector.Select(element => element.Id).Take(ScanLimit + 1).ToArray();
        bool complete = ids.Length <= ScanLimit;
        var ordered = ids.Take(ScanLimit).OrderBy(id => id.Value).ToArray();
        var rows = new List<ElementRow>(); var missing = new List<string>();
        long after = long.Parse(query.AfterId);
        foreach (var id in ordered.Where(id => id.Value > after))
        {
            var element = doc.GetElement(id);
            if (element is null) { missing.Add(id.Value.ToString()); continue; }
            var row = Describe(doc, element);
            if (!row.Name.Contains(query.NameContains, StringComparison.OrdinalIgnoreCase)
                || query.MissingMark && !string.IsNullOrWhiteSpace(row.Mark)) continue;
            rows.Add(row);
            if (rows.Count > query.Limit) break;
        }
        return InspectionPages.Create(target, query, rows, complete, ordered.Length, missing.ToArray());
    }
    private static FilteredElementCollector Collector(Document doc, Category category)
    {
        var collector = new FilteredElementCollector(doc);
        if (category == Category.Views) return collector.OfClass(typeof(View));
        if (category == Category.Sheets) return collector.OfClass(typeof(ViewSheet));
        if (category == Category.Levels) return collector.OfClass(typeof(Level));
        var builtIn = category switch
        {
            Category.Doors => BuiltInCategory.OST_Doors, Category.Rooms => BuiltInCategory.OST_Rooms,
            Category.Walls => BuiltInCategory.OST_Walls, Category.Floors => BuiltInCategory.OST_Floors,
            Category.Ceilings => BuiltInCategory.OST_Ceilings, Category.LightingFixtures => BuiltInCategory.OST_LightingFixtures,
            Category.LightingDevices => BuiltInCategory.OST_LightingDevices, Category.ElectricalEquipment => BuiltInCategory.OST_ElectricalEquipment,
            _ => throw new Rejection("invalid_category", "Unsupported category.")
        };
        return collector.OfCategory(builtIn).WhereElementIsNotElementType();
    }
    private static ElementRow Describe(Document doc, Element element)
    {
        var level = doc.GetElement(element.LevelId) as Level;
        var type = doc.GetElement(element.GetTypeId()) as ElementType;
        var view = element as View;
        var room = element as Room;
        bool eligible = view is { IsTemplate: false } && view.ViewType is ViewType.FloorPlan or ViewType.CeilingPlan
            or ViewType.EngineeringPlan or ViewType.Section or ViewType.Elevation or ViewType.ThreeD
            or ViewType.Detail or ViewType.AreaPlan or ViewType.DraftingView;
        return new(element.Id.Value.ToString(), element.UniqueId, Text(element.Name), Text(element.Category?.Name),
            Text(type?.Name), Text(type?.FamilyName), level?.Id.Value.ToString() ?? "", Text(level?.Name),
            element.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString(),
            element is ViewSheet sheet ? Text(sheet.SheetNumber) : null,
            view?.IsTemplate ?? false, eligible, view?.ViewTemplateId.Value.ToString(), view?.ViewType.ToString(), room is null ? null : room.Location is not null,
            room?.Area, []);
    }
    private static ElementPage Warnings(Document doc, Target target, ReadQuery query)
    {
        var warnings = doc.GetWarnings(); var rows = new List<ElementRow>();
        int scanned = Math.Min(warnings.Count, ScanLimit);
        long after = long.Parse(query.AfterId);
        for (int i = (int)Math.Min(after, scanned); i < scanned; i++)
        {
            var warning = warnings[i]; var description = Text(warning.GetDescriptionText());
            if (!description.Contains(query.NameContains, StringComparison.OrdinalIgnoreCase)) continue;
            rows.Add(new((i + 1).ToString(), warning.GetFailureDefinitionId().Guid.ToString(), description, "Warnings",
                "", "", "", "", null, null, false, false, null, null, null, null,
                warning.GetFailingElements().Concat(warning.GetAdditionalElements()).Distinct().Take(20).Select(id => id.Value.ToString()).ToArray()));
            if (rows.Count > query.Limit) break;
        }
        return InspectionPages.Create(target, query, rows, warnings.Count <= ScanLimit, scanned, []);
    }
    private static string Text(string? value) => value ?? "";
}
