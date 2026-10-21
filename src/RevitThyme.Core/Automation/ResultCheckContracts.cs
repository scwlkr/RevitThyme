using System.Text.Json;

namespace RevitThyme.Core;

/// <summary>Reviewed observations and permitted native changes, frozen before executing an action.</summary>
public sealed record ResultCheck(string Name, string Source, string Comparison = "equals",
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    [property: System.Text.Json.Serialization.JsonConverter(typeof(ResultCheckExpectedConverter))] JsonElement? Expected = null,
    double? Minimum = null, double? Maximum = null, string? ExpectedInput = null);
public sealed record ResultCheckPlan(ResultCheck[] Checks, string AllowedChangesSource, int MaxAdded = 0, long[]? AddedCategoryIds = null);
public sealed record ResultCheckVerdict(string Name, string Status, string Comparison, JsonElement? Expected = null,
    JsonElement? Observed = null, bool? Exists = null, string? Error = null);
public sealed record ResultVerification(string Status, Guid OperationId, Guid PreparationId, Guid VerificationId,
    string PlanDigest, string SourceSha256, string Message, ResultCheckVerdict[]? Checks = null,
    CodeChanges? ActualChanges = null, CodeChanges? AllowedChanges = null, CodeChanges? UnexpectedChanges = null,
    string[]? Violations = null);
public sealed record ResultCheckPreparation(Guid OperationId, Guid PreparationId, Guid VerificationId, string PlanDigest,
    string SourceSha256, OperationResult Receipt);

// Preserve explicit JSON null as an expected value, distinct from an omitted expected property.
public sealed class ResultCheckExpectedConverter : System.Text.Json.Serialization.JsonConverter<JsonElement?>
{
    public override bool HandleNull => true;
    public override JsonElement? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    { using var document = JsonDocument.ParseValue(ref reader); return document.RootElement.Clone(); }
    public override void Write(Utf8JsonWriter writer, JsonElement? value, JsonSerializerOptions options)
    { if (value is { } item) item.WriteTo(writer); else writer.WriteNullValue(); }
}
