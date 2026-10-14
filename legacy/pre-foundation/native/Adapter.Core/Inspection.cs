using System.Text;

namespace RevitThyme.Native;
public enum Category { Doors, Rooms, Views, Sheets, Levels, Walls, Floors, Ceilings, LightingFixtures, LightingDevices, ElectricalEquipment, Warnings }
public record ReadQuery(Category Category, string AfterId, int Limit, string NameContains, bool MissingMark, string[] Ids)
{
    public void Validate()
    {
        if (!Enum.IsDefined(Category) || Limit is < 1 or > 100 || NameContains.EnumerateRunes().Count() > 128
            || !long.TryParse(AfterId, out long after) || after < 0 || AfterId != after.ToString()
            || MissingMark && Category != Category.Doors || Ids.Length > 20 || Ids.Distinct().Count() != Ids.Length
            || Ids.Any(id => !long.TryParse(id, out long n) || n <= 0 || id != n.ToString())
            || Ids.Length > 0 && (NameContains != "" || MissingMark || Category == Category.Warnings))
            throw new Rejection("invalid_arguments", "Use canonical positive element IDs, a page limit 1-100 and supported category filters.");
    }
}
public record ElementRow(string ElementId, string UniqueId, string Name, string Category, string TypeName,
    string FamilyName, string LevelId, string LevelName, string? Mark, string? SheetNumber,
    bool IsTemplate, bool TemplateEligible, string? ViewTemplateId, string? ViewKind, bool? Placed, double? AreaSquareFeet,
    string[] RelatedIds);
public record ModelContext(Target Target, string DocumentName, string ViewName, string ViewKind,
    bool IsModified, bool IsReadOnly, bool IsWorkshared, string RevitVersion, string InternalLengthUnit);
public record ElementPage(Target Target, Category Category, ElementRow[] Items, string NextAfterId,
    bool HasMore, bool ScanComplete, int Scanned, string[] MissingIds, string[] Diagnostics);
public record InspectionResult(ModelContext? Context = null, ElementPage? Page = null);
// Separate from view-range readiness: inspection does not require a supported plan or geometry capture.
public interface IReadModel
{
    ModelContext Context();
    ElementPage Elements(Target target, ReadQuery query);
}
public static class InspectionPages
{
    public static ElementPage Create(Target target, ReadQuery query, List<ElementRow> rows, bool complete, int scanned, string[] missing)
    {
        bool more = rows.Count > query.Limit;
        var items = rows.Take(query.Limit).ToList();
        ElementPage Page() => new(target, query.Category, items.ToArray(), items.LastOrDefault()?.ElementId ?? query.AfterId,
            more, complete, scanned, missing, complete ? [] : ["Native scan capped at 20000 elements; findings are a lower bound."]);
        // JSON escapes can be much larger than the original strings. Bound bytes and preserve the continuation cursor.
        while (System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(Page(), Wire.Json).Length > 55_000)
        {
            if (items.Count <= 1) throw new Rejection("result_limit", "One element exceeds the native result budget.");
            items.RemoveAt(items.Count - 1); more = true;
        }
        return Page();
    }
}
