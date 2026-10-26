using System.Text.Json;
using RevitThyme.Core;

namespace RevitThyme.McpTests;

internal static class GenericContract
{
    public static void Run()
    {
        Queries(); Ids(); Edits(); Envelope();
    }

    private static OperationRequest Request(FakeGenericModel model, OperationKind operation) => new(Guid.NewGuid(), operation, model.Target);
    private static OperationResult Execute(FakeGenericModel model, OperationRequest request) => new OperationExecutor().Execute(model, request);
    private static void Reject(FakeGenericModel model, OperationRequest request, string code, string name)
    {
        var result = Execute(model, request);
        Program.Check(name, result.Code == code && result.Outcome == Outcome.Rejected && model.Reads == 0 && model.Transactions == 0);
    }

    private static void Queries()
    {
        foreach (var query in new[] { new ElementQuery(), new(Offset: 1000000, Limit: 100, NameContains: new string('x', 200)),
            new(Offset: 0, Limit: 1, CategoryId: long.MinValue.ToString(System.Globalization.CultureInfo.InvariantCulture), IncludeTypes: true) })
        {
            var model = new FakeGenericModel();
            var result = Execute(model, Request(model, OperationKind.QueryElements) with { Query = query });
            Program.Check("valid query boundaries reach the native query unchanged", result.Outcome == Outcome.Inspected && result.Elements?.Items.Length == 1 && model.LastQuery == query && model.Reads == 1);
        }
        foreach (var query in new[] { new ElementQuery(Offset: -1), new(Offset: 1000001), new(Limit: 0), new(Limit: 101),
            new(NameContains: new string('x', 201)), new(CategoryId: "0"), new(CategoryId: "1"), new(CategoryId: "not-an-id") })
        {
            var model = new FakeGenericModel();
            Reject(model, Request(model, OperationKind.QueryElements) with { Query = query }, "invalid_query", "query bounds reject before native enumeration");
        }
        foreach (var query in new[] { new ElementQuery(CategoryId: "-1"), new(IncludeTypes: true) })
        {
            var model = new FakeGenericModel();
            Reject(model, Request(model, OperationKind.ListViews) with { Query = query }, "invalid_query", "view queries reject element-only filters");
        }
    }

    private static void Ids()
    {
        foreach (int count in new[] { 1, 5 })
        {
            var model = new FakeGenericModel(); var ids = Enumerable.Range(0, count).Select(i => "element-" + i).ToArray();
            var result = Execute(model, Request(model, OperationKind.InspectElements) with { ElementIds = ids, ParameterOffset = 10000 });
            Program.Check("inspection accepts inclusive item and parameter bounds", result.Details?.Length == count && model.LastParameterOffset == 10000 && model.Transactions == 0);
        }
        foreach (var ids in new string[]?[] { null, [], ["duplicate", "duplicate"], [""], [new string('x', 201)],
            Enumerable.Range(0, 6).Select(i => "element-" + i).ToArray(), JsonSerializer.Deserialize<string[]>("[null]") })
        {
            var model = new FakeGenericModel();
            Reject(model, Request(model, OperationKind.InspectElements) with { ElementIds = ids }, "invalid_elements", "inspection rejects malformed native id lists before reads");
        }
        foreach (int offset in new[] { -1, 10001 })
        {
            var model = new FakeGenericModel();
            Reject(model, Request(model, OperationKind.InspectElements) with { ElementIds = ["element"], ParameterOffset = offset }, "invalid_query", "inspection rejects out-of-range parameter pages");
        }
        foreach (int count in new[] { 0, 100 })
        {
            var model = new FakeGenericModel(); var ids = Enumerable.Range(0, count).Select(i => i == 0 ? new string('x', 200) : "element-" + i).ToArray();
            var result = Execute(model, Request(model, OperationKind.SetSelection) with { ElementIds = ids });
            Program.Check("selection accepts clear and maximum native id boundaries", result.Outcome is Outcome.Applied or Outcome.Unchanged && model.Selection.SequenceEqual(ids) && model.Transactions == 0);
        }
        var oversized = new FakeGenericModel();
        Reject(oversized, Request(oversized, OperationKind.SetSelection) with { ElementIds = Enumerable.Range(0, 101).Select(i => "element-" + i).ToArray() }, "invalid_elements", "selection rejects oversized lists before reading native selection");
    }

    private static void Edits()
    {
        foreach (var value in new[] { new ParameterValue("String", Text: new string('x', 512)), new("Integer", Integer: int.MinValue),
            new("Integer", Integer: int.MaxValue), new("Double", Number: -1000000) })
        {
            var model = new FakeGenericModel(); model.Values["1"] = value with { Text = value.Text is null ? null : "original" };
            var edit = new ParameterEdit("element", "1", model.Values["1"], value);
            var result = Execute(model, Request(model, OperationKind.SetParameters) with { Edits = [edit] });
            Program.Check("typed value boundaries preserve storage and native value", result.Outcome is Outcome.Applied or Outcome.Unchanged && model.Values["1"] == value);
        }
        foreach (var value in new[] { new ParameterValue("String"), new("String", Text: new string('x', 513)),
            new("Integer", Integer: (long)int.MaxValue + 1), new("Integer", Integer: (long)int.MinValue - 1),
            new("Double", Number: double.NaN), new("Double", Number: double.PositiveInfinity), new("Double", Number: 1, Text: "mixed"), new("Unknown") })
        {
            var model = new FakeGenericModel();
            Reject(model, Request(model, OperationKind.SetParameters) with { Edits = [new("element", "1", model.Values["1"], value)] }, "invalid_edits", "malformed typed values reject before native parameter reads");
        }
        foreach (int count in new[] { 20, 21 })
        {
            var model = new FakeGenericModel();
            var edits = Enumerable.Range(1, count).Select(i => { string id = i.ToString(System.Globalization.CultureInfo.InvariantCulture); model.Values[id] = new("Integer", Integer: i); return new ParameterEdit("element", id, model.Values[id], model.Values[id]); }).ToArray();
            var request = Request(model, OperationKind.SetParameters) with { Edits = edits };
            if (count == 21) Reject(model, request, "invalid_edits", "oversized parameter batch rejects before native reads");
            else Program.Check("maximum unchanged parameter batch reads without a transaction", Execute(model, request).Outcome == Outcome.Unchanged && model.Reads == 20 && model.Transactions == 0);
        }
    }

    private static void Envelope()
    {
        var model = new FakeGenericModel();
        foreach (var request in new[]
        {
            Request(model, OperationKind.ListViews) with { Name = "unused" },
            Request(model, OperationKind.GetSelection) with { Query = new() },
            Request(model, OperationKind.QueryElements) with { ElementIds = [] },
            Request(model, OperationKind.SetSelection) with { Query = new(), ElementIds = [] },
            Request(model, OperationKind.GetSelection) with { Edits = [] },
            Request(model, OperationKind.QueryElements) with { ParameterOffset = 1 }
        }) Reject(model, request, "invalid_request", "unused generic arguments reject before native operations");
        Reject(model, Request(model, OperationKind.QueryElements) with { Target = model.Target with { Revision = 0 }, Query = new(Limit: 0) }, "stale_target", "stale generic target precedes malformed query validation");
        Program.Check("query diagnostic retains its exact public text", Execute(model,
            Request(model, OperationKind.QueryElements) with { Query = new(Limit: 0) }).Message == "Use offset 0–1000000, limit 1–100 and a negative category ID.");
        Program.Check("inspection diagnostic retains its exact public text", Execute(model,
            Request(model, OperationKind.InspectElements) with { ElementIds = [] }).Message == "Use distinct native unique IDs; inspect 1–5, select 0–100.");
        model.ReadOnly = true;
        Reject(model, Request(model, OperationKind.SetParameters), "write_blocked", "write refusal precedes missing parameter batch validation");
    }
}
