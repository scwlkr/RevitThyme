using RevitThyme.Native;
using RevitThyme.AdapterChecks;

internal static class InspectionFixture
{
    public static void Stage(FakeModel model, string stage)
    {
        if (Environment.CurrentManagedThreadId != model.OwnerThread) throw new InvalidOperationException("Inspection outside owner thread.");
        model.Trace.Add(stage);
    }
    private static ElementRow Row(long id, string name) => new(id.ToString(), "fixture-" + id, name, "Fixture",
        "Test type", "Test family", "4294967299", "Level 1", "A", null, false, false, null, null, null, null, []);
    public static ElementPage Page(Target target, ReadQuery q, string mode)
    {
        q.Validate();
        ElementRow[] source = q.Category switch
        {
            Category.Doors => [Row(4294967301, "Door one") with { Mark = null }, Row(4294967302, "Door two") with { Mark = "  " },
                Row(4294967303, "Door three") with { Mark = "" }, Row(4294967304, "Marked door")],
            Category.Views => [Row(1, "Ground floor") with { TemplateEligible = true, ViewTemplateId = "-1" },
                Row(2, " ground FLOOR ") with { TemplateEligible = true, ViewTemplateId = "91" },
                Row(3, "Ground floor") with { IsTemplate = true },
                Row(4, "Ground floor") with { ViewKind = "DrawingSheet" }],
            Category.Sheets => [Row(1, "Plan") with { SheetNumber = "A101" }, Row(2, "Alt plan") with { SheetNumber = "a101" }],
            Category.Rooms => [Row(1, "Unplaced room") with { Placed = false, AreaSquareFeet = 0 },
                Row(2, "Unenclosed room") with { Placed = true, AreaSquareFeet = 0 }],
            Category.Warnings => [Row(1, "Known test warning") with { RelatedIds = ["4294967301", "4294967303"] }],
            _ => []
        };
        if (q.Category == Category.Doors && mode is "many" or "wide" or "oversized")
            source = Enumerable.Range(0, mode == "many" ? 2001 : mode == "wide" ? 100 : 1)
                .Select(i => Row(4294967301 + i, mode == "wide" ? new('你', 1024) : mode == "oversized" ? new('你', 20_000) : "Door") with { Mark = null }).ToArray();
        var chosen = source.Where(r => long.Parse(r.ElementId) > long.Parse(q.AfterId)
            && (q.Ids.Length == 0 || q.Ids.Contains(r.ElementId))
            && r.Name.Contains(q.NameContains, StringComparison.OrdinalIgnoreCase)
            && (!q.MissingMark || string.IsNullOrWhiteSpace(r.Mark))).OrderBy(r => long.Parse(r.ElementId)).ToArray();
        return InspectionPages.Create(target, q, chosen.Take(q.Limit + 1).ToList(), mode != "partial", source.Length,
            q.Ids.Where(id => !source.Any(r => r.ElementId == id)).ToArray());
    }
}
