using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RevitThyme.Core;

public sealed record SavedToolInput(string Name, string Type, string Description, bool Required = true);
public sealed record SavedToolTest(string Name, JsonElement Inputs, string Assumptions, string ExpectedResult, string AssertionSource);
public sealed record SavedToolDefinition(string Name, string Request, string Description, string Mode, string Source,
    SavedToolInput[] Inputs, string ExpectedResult, SavedToolTest[] Tests);
public sealed record SavedToolArtifact(string ToolId, string Digest, SavedToolDefinition Definition, string TestStatus = "not_run");

/// <summary>Validates saved tool contracts and binds input data without substituting C# tokens.</summary>
public static partial class SavedTools
{
    public const int MaxDefinitionBytes = 65536;
    public const int MaxInputBytes = 8192;
    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex InputName();
    public static void Validate(SavedToolDefinition tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        Text(tool.Name, 120, "name"); Text(tool.Request, 4000, "request"); Text(tool.Description, 4000, "description");
        Text(tool.ExpectedResult, 4000, "expected result"); Source(tool.Source);
        if (tool.Mode is not ("query" or "modify")) throw new ArgumentException("Tool mode must be query or modify.");
        if (tool.Inputs is null || tool.Inputs.Length > 32 || tool.Tests is null || tool.Tests.Length is < 1 or > 16)
            throw new ArgumentException("Use up to 32 inputs and 1–16 concrete tests.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var input in tool.Inputs)
        {
            if (input is null || input.Name is null || !InputName().IsMatch(input.Name) || !names.Add(input.Name))
                throw new ArgumentException("Input names must be unique identifiers, starting with a letter (maximum 64 characters).");
            if (input.Type is not ("string" or "number" or "integer" or "boolean" or "string[]")) throw new ArgumentException("Unsupported input type.");
            Text(input.Description, 1000, "input description");
        }
        names.Clear();
        foreach (var test in tool.Tests)
        {
            if (test is null) throw new ArgumentException("A test is missing.");
            Text(test.Name, 120, "test name"); Text(test.Assumptions, 4000, "test assumptions"); Text(test.ExpectedResult, 4000, "test expected result");
            if (!names.Add(test.Name)) throw new ArgumentException("Test names must be unique.");
            Source(test.AssertionSource);
            _ = Bind(tool, test.Inputs, test.AssertionSource);
            _ = Bind(tool, test.Inputs, tool.Source);
        }
        if (JsonSerializer.SerializeToUtf8Bytes(tool, Protocol.Json).Length > MaxDefinitionBytes) throw new ArgumentException("Tool definition exceeds 64 KiB.");
    }
    public static string Bind(SavedToolDefinition tool, JsonElement inputs, string? source = null)
    {
        if (inputs.ValueKind != JsonValueKind.Object || Encoding.UTF8.GetByteCount(inputs.GetRawText()) > MaxInputBytes)
            throw new ArgumentException("Inputs must be a JSON object up to 8 KiB.");
        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in inputs.EnumerateObject())
            if (!values.TryAdd(property.Name, property.Value)) throw new ArgumentException("Duplicate input: " + property.Name);
        if (values.Keys.Except(tool.Inputs.Select(i => i.Name), StringComparer.Ordinal).Any()) throw new ArgumentException("Unknown tool input.");
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            foreach (var input in tool.Inputs)
            {
                if (!values.TryGetValue(input.Name, out var value))
                { if (input.Required) throw new ArgumentException("Required input: " + input.Name); continue; }
                bool valid = input.Type switch
                {
                    "string" => value.ValueKind == JsonValueKind.String,
                    "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
                    "number" => value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double number) && double.IsFinite(number),
                    "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
                    "string[]" => value.ValueKind == JsonValueKind.Array && value.EnumerateArray().All(v => v.ValueKind == JsonValueKind.String),
                    _ => false
                };
                if (!valid) throw new ArgumentException("Invalid " + input.Type + " input: " + input.Name);
                writer.WritePropertyName(input.Name);
                // Normalize numeric lexical forms as well as property ordering for request replay.
                if (input.Type == "integer") writer.WriteNumberValue(value.GetInt64());
                else if (input.Type == "number") writer.WriteNumberValue(value.GetDouble());
                else value.WriteTo(writer);
            }
            writer.WriteEndObject();
        }
        string literal = JsonSerializer.Serialize(Encoding.UTF8.GetString(buffer.ToArray()));
        string expanded = "using var __revitThymeInputs = System.Text.Json.JsonDocument.Parse(" + literal + ");\n"
            + "var inputs = __revitThymeInputs.RootElement.Clone();\n#line 1 \"saved-tool.cs\"\n" + (source ?? tool.Source);
        Source(expanded); return expanded;
    }
    public static CodeRequest Invocation(SavedToolArtifact artifact, string digest, JsonElement inputs, bool trusted, int budgetMilliseconds, string? testName = null)
    {
        if (!string.Equals(artifact.Digest, digest, StringComparison.Ordinal)) throw new ArgumentException("Tool digest changed. Retrieve and review the definition again.");
        if (!trusted) throw new ArgumentException("Saved tools require trusted=true; full process permissions, no sandbox.");
        if (budgetMilliseconds is < 100 or > 30000) throw new ArgumentException("Budget must be 100–30000 ms.");
        string? source = testName is null ? null : artifact.Definition.Tests.SingleOrDefault(t => t.Name == testName)?.AssertionSource
            ?? throw new ArgumentException("Unknown saved tool test.");
        return new(Bind(artifact.Definition, inputs, source), true, budgetMilliseconds);
    }
    private static void Text(string? value, int max, string label)
    { if (string.IsNullOrWhiteSpace(value) || value.Length > max) throw new ArgumentException($"Use a nonempty {label} up to {max} characters."); }
    private static void Source(string? value)
    { if (string.IsNullOrWhiteSpace(value) || Encoding.UTF8.GetByteCount(value) > DynamicExecutor.MaxSourceBytes) throw new ArgumentException("Expanded C# source must be nonempty and no larger than 24 KiB."); }
}
