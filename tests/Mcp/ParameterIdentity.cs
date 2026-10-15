using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using RevitThyme.Core;
using RevitThyme.Mcp;

namespace RevitThyme.McpTests;

internal static class ParameterIdentity
{
    public static async Task Run()
    {
        var firstGuid = Guid.Parse("b15ab870-b77a-493b-a094-82f66b86d94c");
        var secondGuid = Guid.Parse("86a1116b-5f8c-4ff4-8eda-4daaef0c8f19");
        ParameterInfo[] fields =
        [
            new("-1010106", "Comments", "text", false, new("String", Text: "built-in"), BuiltInParameter: "ALL_MODEL_INSTANCE_COMMENTS", HasValue: true),
            new("9007199254740993", "Comments", "text", false, new("String", Text: "shared"), IsShared: true, SharedGuid: firstGuid, HasValue: true),
            new("9007199254740995", "Comments", "text", false, new("String", Text: ""), IsShared: true, SharedGuid: secondGuid, HasValue: false),
            new("2147483648", "Local text", "text", false, new("String", Text: ""), HasValue: false),
            new("2147483649", "Assigned zero", "integer", false, new("Integer", Integer: 0), HasValue: true),
            new("2147483650", "Unassigned zero", "integer", false, new("Integer", Integer: 0), HasValue: false),
            new("9007199254740994", "Assigned empty", "text", false, new("String", Text: ""), HasValue: true)
        ];
        fields = fields.Concat(Enumerable.Range(1, 14).Select(i => i == 14
            ? new ParameterInfo("3000000014", "Truncated text", "text", false, new("String", Text: new string('x', 512)), Truncated: true, HasValue: true)
            : new ParameterInfo((3000000000L + i).ToString(CultureInfo.InvariantCulture), "Page field " + i, "number", false,
                new("Double", Number: i), HasValue: true))).OrderBy(p => long.Parse(p.Id, CultureInfo.InvariantCulture)).ToArray();
        var model = new FakeGenericModel(); model.Values.Clear();
        foreach (var field in fields) { model.Parameters.Add(field.Id, field); model.Values.Add(field.Id, field.Value); }
        var before = JsonSerializer.Serialize(new { model.Values, model.Target, model.Name, model.Selection }, Core.Protocol.Json);
        var executor = new OperationExecutor();
        OperationResult Inspect(int offset) => executor.Execute(model, new(Guid.NewGuid(), OperationKind.InspectElements,
            model.Target, ElementIds: ["element"], ParameterOffset: offset));
        var first = Inspect(0); var last = Inspect(20);
        Program.Check("parameter identity survives the operation contract across a page boundary", first.Details is [{ Parameters.Length: 20, NextParameterOffset: 20 }]
            && last.Details is [{ Parameters.Length: 1, NextParameterOffset: null }]
            && first.Details[0].Parameters.Concat(last.Details[0].Parameters).SequenceEqual(fields));
        Program.Check("same-name shared fields with different GUIDs span inspection pages", first.Details![0].Parameters.Any(p => p.Name == "Comments"
                && p.Id == "9007199254740993" && p.SharedGuid == firstGuid)
            && first.Details[0].Parameters.All(p => p.Id != "9007199254740995")
            && last.Details![0].Parameters is [{ Name: "Comments", Id: "9007199254740995" } finalShared] && finalShared.SharedGuid == secondGuid);
        Program.Check("inspection retains a truncated string's exact clipped value and flag", first.Details[0].Parameters.Single(p => p.Id == "3000000014")
            is { Truncated: true, Value.Text.Length: 512 } clipped && clipped.Value.Text == new string('x', 512));
        Program.Check("parameter identity inspection leaves model and transaction state unchanged", model.Transactions == 0 && model.Writes == 0
            && before == JsonSerializer.Serialize(new { model.Values, model.Target, model.Name, model.Selection }, Core.Protocol.Json));

        var all = first with { Details = [first.Details![0] with { Parameters = fields, NextParameterOffset = null }] };
        var json = JsonSerializer.SerializeToElement(all, Core.Protocol.Json);
        var roundtrip = json.Deserialize<OperationResult>(Core.Protocol.Json)!;
        Program.Check("parameter identity and raw values round-trip through operation JSON", roundtrip.Details![0].Parameters.SequenceEqual(fields));
        var rows = json.GetProperty("details")[0].GetProperty("parameters").EnumerateArray().ToArray();
        JsonElement Row(string id) => rows.Single(row => row.GetProperty("id").GetString() == id);
        Program.Check("built-in identity has explicit null shared GUID", Row("-1010106").GetProperty("sharedGuid").ValueKind == JsonValueKind.Null
            && !Row("-1010106").GetProperty("isShared").GetBoolean()
            && Row("-1010106").GetProperty("builtInParameter").GetString() == "ALL_MODEL_INSTANCE_COMMENTS");
        Program.Check("same-name shared fields retain distinct GUIDs and exact long ID strings", Row("9007199254740993").GetProperty("sharedGuid").GetGuid() == firstGuid
            && Row("9007199254740995").GetProperty("sharedGuid").GetGuid() == secondGuid
            && Row("9007199254740993").GetProperty("isShared").GetBoolean()
            && Row("9007199254740995").GetProperty("isShared").GetBoolean()
            && Row("9007199254740993").GetProperty("builtInParameter").ValueKind == JsonValueKind.Null
            && fields.Count(p => p.Name == "Comments") == 3);
        Program.Check("nonshared identity serializes explicit null identities", Row("2147483648").GetProperty("sharedGuid").ValueKind == JsonValueKind.Null
            && Row("2147483648").GetProperty("builtInParameter").ValueKind == JsonValueKind.Null
            && !Row("2147483648").GetProperty("isShared").GetBoolean());
        Program.Check("HasValue remains independent of zero and empty raw values", Row("2147483649").GetProperty("hasValue").GetBoolean()
            && !Row("2147483650").GetProperty("hasValue").GetBoolean()
            && Row("2147483649").GetProperty("value").GetProperty("integer").GetInt64() == 0
            && Row("2147483650").GetProperty("value").GetProperty("integer").GetInt64() == 0
            && Row("9007199254740994").GetProperty("hasValue").GetBoolean()
            && !Row("2147483648").GetProperty("hasValue").GetBoolean()
            && Row("9007199254740994").GetProperty("value").GetProperty("text").GetString() == ""
            && Row("2147483648").GetProperty("value").GetProperty("text").GetString() == "");

        await Mcp(model, executor, fields);
        var selected = model.Parameters["9007199254740993"];
        OperationRequest Edit(ParameterValue expected) => new(Guid.NewGuid(), OperationKind.SetParameters, model.Target,
            Edits: [new("element", selected.Id, expected, new("String", Text: "selected only"))]);
        var edit = Edit(model.Values[selected.Id]);
        var changed = executor.Execute(model, edit);
        Program.Check("identity metadata preserves exact-ID edits among same-name fields", changed.Outcome == Outcome.Applied
            && model.Values[selected.Id].Text == "selected only" && model.Values["9007199254740995"].Text == ""
            && model.Values["-1010106"].Text == "built-in" && changed.ParameterChanges is [{ ParameterId: "9007199254740993" }]);
        int transactions = model.Transactions;
        Program.Check("identity metadata retains expected-value guard", executor.Execute(model, edit with { Id = Guid.NewGuid() }).Code == "stale_parameter"
            && model.Transactions == transactions);
        foreach (string guard in new[] { "read-only", "truncated" })
        {
            model.Parameters[selected.Id] = selected with { IsReadOnly = guard == "read-only", Truncated = guard == "truncated" };
            Program.Check("identity metadata retains " + guard + " edit guard", executor.Execute(model, Edit(model.Values[selected.Id])).Code == "parameter_blocked"
                && model.Transactions == transactions);
        }
    }

    private static async Task Mcp(FakeGenericModel model, OperationExecutor executor, ParameterInfo[] fields)
    {
        string folder = Path.Combine(Path.GetTempPath(), "RevitThyme-identity-" + Guid.NewGuid()); Directory.CreateDirectory(folder);
        string sessionFile = Path.Combine(folder, "session.json");
        using var process = Process.GetCurrentProcess(); string sessionId = Guid.NewGuid().ToString();
        var binding = new SessionBinding(Core.Protocol.Version, process.Id, process.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture),
            sessionId, $"RevitThyme-Foundation-{process.Id}-{sessionId}", "Fake");
        try
        {
            File.WriteAllText(sessionFile, JsonSerializer.Serialize(binding, Core.Protocol.Json));
            var connection = new RevitConnection(new(folder, sessionFile), _ => new FakeClient(model, executor));
            var tools = new RevitTools(connection); var context = await connection.Context(CancellationToken.None);
            var first = await tools.InspectElements(context.CaptureId, ["element"]);
            var last = await tools.InspectElements(context.CaptureId, ["element"], 20);
            var collected = new List<ParameterInfo>();
            foreach (var reply in new[] { first, last })
            {
                string text = reply.Content.OfType<TextContentBlock>().Single().Text;
                Program.Check("MCP identity text and structured content agree", reply.IsError != true
                    && reply.StructuredContent is { } structured && text == structured.GetRawText());
                using var data = JsonDocument.Parse(text);
                collected.AddRange(data.RootElement.GetProperty("details")[0].GetProperty("parameters").Deserialize<ParameterInfo[]>(Core.Protocol.Json)!);
            }
            Program.Check("MCP inspection preserves all identity and HasValue fields across pages", collected.SequenceEqual(fields)
                && model.Transactions == 0 && model.Writes == 0);
        }
        finally { File.Delete(sessionFile); Directory.Delete(folder); }
    }

    private sealed class FakeClient(FakeGenericModel model, OperationExecutor executor) : IOperationClient
    {
        public Task<OperationResult> ExecuteAsync(OperationRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(executor.Execute(model, request));
    }
}
