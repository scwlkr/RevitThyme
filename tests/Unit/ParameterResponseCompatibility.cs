using System.Text.Json;
using RevitThyme.Core;

namespace RevitThyme.Unit;

internal static class ParameterResponseCompatibility
{
    public static async Task Run(Action<string, bool> check)
    {
        var field = new ParameterInfo("9007199254740993", "Comments", "text", false, new("String", Text: ""),
            IsShared: true, SharedGuid: Guid.Parse("87a96cdc-7f6a-44b6-b0a7-fd5faecfcb22"), HasValue: true);
        var request = new OperationRequest(Guid.NewGuid(), OperationKind.InspectElements, ElementIds: ["element"]);
        var envelope = new BridgeRequest(Protocol.Version, "session", request);
        string oldRequest = JsonSerializer.Serialize(envelope, Protocol.Json);
        check("legacy bridge request has no new member and opts out by default", !oldRequest.Contains("includeParameterIdentity")
            && !JsonSerializer.Deserialize<BridgeRequest>(oldRequest, Protocol.Json)!.IncludeParameterIdentity);
        var enriched = envelope with { IncludeParameterIdentity = true };
        var copy = JsonSerializer.Deserialize<BridgeRequest>(JsonSerializer.Serialize(enriched, Protocol.Json), Protocol.Json)!;
        check("identity response opt-in does not change operation replay arguments", copy.IncludeParameterIdentity
            && copy.Request.EquivalentTo(request));

        var result = new OperationResult(request.Id, Outcome.Inspected, "inspected", "Inspected.",
            Details: [new(new("element", 42, "Fixture", "Walls", "-2000011", "Wall", false), [field], 20)]);
        using var stream = new MemoryStream();
        await Frames.WriteAsync(stream, result, default, ParameterResponseJson.For(false)); stream.Position = 4;
        using var legacy = await JsonDocument.ParseAsync(stream);
        var legacyRow = legacy.RootElement.GetProperty("details")[0].GetProperty("parameters")[0];
        var oldField = legacyRow.Deserialize<LegacyParameterInfo>(Protocol.Json)!;
        check("old strict parameter clients can decode the original response shape", legacyRow.EnumerateObject().Count() == 6
            && oldField.Id == field.Id && oldField.Name == field.Name && oldField.DataType == field.DataType
            && oldField.IsReadOnly == field.IsReadOnly && oldField.Value == field.Value && oldField.Truncated == field.Truncated
            && legacy.RootElement.GetProperty("details")[0].GetProperty("nextParameterOffset").GetInt32() == 20);
        stream.SetLength(0); stream.Position = 0;
        await Frames.WriteAsync(stream, result, default, ParameterResponseJson.For(true)); stream.Position = 0;
        var current = await Frames.ReadAsync<OperationResult>(stream, default);
        check("opted-in framed responses preserve all parameter metadata", current.Details![0].Parameters[0] == field
            && result.Details![0].Parameters[0] == field);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(field, ParameterResponseJson.For(true)));
        check("opted-in inapplicable metadata stays explicit null", json.RootElement.GetProperty("builtInParameter").ValueKind == JsonValueKind.Null);
        // Response projection must not relax strict request parsing.
        try
        {
            JsonSerializer.Deserialize<BridgeRequest>(oldRequest[..^1] + ",\"unknownFlag\":true}", Protocol.Json);
            check("unknown bridge request members still rejected", false);
        }
        catch (JsonException) { check("unknown bridge request members still rejected", true); }
        var oversized = field with { Value = new("String", Text: new string('x', Protocol.MaxBytes)) };
        stream.SetLength(0); stream.Position = 0;
        try
        {
            await Frames.WriteAsync(stream, result with { Details = [result.Details![0] with { Parameters = [oversized] }] }, default,
                ParameterResponseJson.For(true));
            check("identity responses retain the existing frame byte bound", false);
        }
        catch (InvalidDataException) { check("identity responses retain the existing frame byte bound", stream.Length == 0); }
    }

    // Exact pre-identity contract, decoded with the same strict serializer used by existing clients.
    private sealed record LegacyParameterInfo(string Id, string Name, string DataType, bool IsReadOnly,
        ParameterValue Value, bool Truncated = false);
}
