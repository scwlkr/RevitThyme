using System.Security.Cryptography;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;

namespace RevitThyme.Core;

public static class ResultChecks
{
    public static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static void Validate(ResultCheckPlan plan)
    {
        if (plan.Checks is null || plan.Checks.Length is < 1 or > 16) throw new ArgumentException("Use 1–16 result checks.");
        Source(plan.AllowedChangesSource);
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var check in plan.Checks)
        {
            if (check is null || string.IsNullOrWhiteSpace(check.Name) || check.Name.Length > 120 || !names.Add(check.Name))
                throw new ArgumentException("Use unique nonempty check names up to 120 characters.");
            Source(check.Source);
            if (check.Comparison is not ("equals" or "exists" or "absent" or "range")) throw new ArgumentException("Unsupported result comparison.");
            if (check.Comparison == "equals" && (check.Expected is null) == (check.ExpectedInput is null))
                throw new ArgumentException("Equality needs exactly one expected JSON value or expectedInput name.");
            if (check.ExpectedInput is { } name && (string.IsNullOrWhiteSpace(name) || name.Length > 64)) throw new ArgumentException("Invalid expectedInput.");
            if (check.Comparison == "range" && (check.Minimum is null && check.Maximum is null
                || check.Minimum is { } min && !double.IsFinite(min) || check.Maximum is { } max && !double.IsFinite(max)
                || check.Minimum > check.Maximum)) throw new ArgumentException("Range needs finite ordered limits.");
        }
        if (plan.MaxAdded is < 0 or > 512 || plan.AddedCategoryIds?.Length > 64
            || plan.AddedCategoryIds?.Any(id => id >= 0) == true || plan.MaxAdded > 0 && plan.AddedCategoryIds is not { Length: > 0 })
            throw new ArgumentException("Added elements require a count 0–512 and declared native negative category IDs (up to 64).");
        if (JsonSerializer.SerializeToUtf8Bytes(plan, Protocol.Json).Length > SavedTools.MaxDefinitionBytes) throw new ArgumentException("Result plan exceeds 64 KiB.");
    }
    public static JsonElement CanonicalInputs(JsonElement inputs)
    {
        if (inputs.ValueKind != JsonValueKind.Object || Encoding.UTF8.GetByteCount(inputs.GetRawText()) > SavedTools.MaxInputBytes)
            throw new ArgumentException("Inputs must be an object up to 8 KiB.");
        using var stream = new MemoryStream(); using (var writer = new Utf8JsonWriter(stream)) Write(inputs, writer);
        if (stream.Length > SavedTools.MaxInputBytes) throw new ArgumentException("Canonical serialized inputs exceed 8 KiB.");
        return JsonDocument.Parse(stream.ToArray()).RootElement.Clone();
    }
    private static void Write(JsonElement item, Utf8JsonWriter writer)
    {
        if (item.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject(); var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in item.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
            { if (!seen.Add(p.Name)) throw new ArgumentException("Duplicate JSON property: " + p.Name); writer.WritePropertyName(p.Name); Write(p.Value, writer); }
            writer.WriteEndObject();
        }
        else if (item.ValueKind == JsonValueKind.Array) { writer.WriteStartArray(); foreach (var v in item.EnumerateArray()) Write(v, writer); writer.WriteEndArray(); }
        else if (item.ValueKind == JsonValueKind.Number) writer.WriteRawValue(NormalizeNumber(item.GetRawText()));
        else item.WriteTo(writer);
    }
    // Exact JSON number normalization, including nested numbers and exponents beyond decimal/double range.
    private static string NormalizeNumber(string raw)
    {
        bool negative = raw[0] == '-'; int start = negative ? 1 : 0;
        int e = raw.IndexOfAny(['e', 'E']); string mantissa = raw[start..(e < 0 ? raw.Length : e)];
        BigInteger exponent = e < 0 ? BigInteger.Zero : BigInteger.Parse(raw.AsSpan(e + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        int dot = mantissa.IndexOf('.');
        if (dot >= 0) { exponent -= mantissa.Length - dot - 1; mantissa = mantissa.Remove(dot, 1); }
        string digits = mantissa.TrimStart('0'); if (digits.Length == 0) return "0";
        int length = digits.Length; digits = digits.TrimEnd('0'); exponent += length - digits.Length;
        if (exponent >= 0 && exponent <= 4096 && digits.Length + exponent <= 4096)
            return (negative ? "-" : "") + digits + new string('0', (int)exponent);
        return (negative ? "-" : "") + digits + (exponent.IsZero ? "" : "e" + exponent.ToString(CultureInfo.InvariantCulture));
    }
    internal static void Source(string source)
    {
        if (string.IsNullOrWhiteSpace(source) || Encoding.UTF8.GetByteCount(source) > DynamicExecutor.MaxSourceBytes)
            throw new ArgumentException("Expanded observation source must be nonempty and at most 24 KiB.");
    }
    public static CodeRequest Observation(string source, JsonElement data, int budget = 10000)
    {
        if (budget is < 100 or > 30000) throw new ArgumentException("Budget must be 100–30000 ms.");
        if (JsonSerializer.SerializeToUtf8Bytes(data, Protocol.Json).Length > DynamicExecutor.MaxObservationDataBytes)
            throw new ArgumentException("Observation data exceeds 32 KiB.");
        source = "// observation-data-sha256:" + Hash(JsonSerializer.Serialize(data, Protocol.Json)) + "\n" + source;
        Source(source); return new(source, true, budget, data.Clone());
    }
    internal static string ObservationSource(ResultCheckPlan plan)
    {
        var source = new StringBuilder("var inputs = data.GetProperty(\"inputs\");\nvar result = data.GetProperty(\"result\");\nvar changes = data.GetProperty(\"changes\");\n");
        for (int i = 0; i < plan.Checks.Length; i++)
            source.Append("object Check").Append(i).Append("() {\n#line 1 \"result-check-").Append(i).Append(".cs\"\n")
                .Append(plan.Checks[i].Source).Append("\n#line default\n}\n");
        source.Append("object Observe(Func<object> read) { try { return new { observation = read(), error = (string)null }; } catch(Exception e) { return new { observation = (object)null, error = e.Message.Substring(0, Math.Min(240,e.Message.Length)) }; } }\n")
            .Append("var observations = new object[] {");
        for (int i = 0; i < plan.Checks.Length; i++) source.Append(i == 0 ? "" : ",").Append("Observe(Check").Append(i).Append(')');
        source.Append("};\nvar addedCategories = changes.GetProperty(\"added\").EnumerateArray().Select(v => { long id=v.GetInt64(); var e=doc.GetElement(new ElementId(id)); return new { id, categoryId=e?.Category?.Id.Value }; }).ToArray();\nreturn new { observations, addedCategories };\n");
        return source.ToString();
    }
    internal static bool Equal(JsonElement a, JsonElement b)
    {
        if (a.ValueKind != b.ValueKind) return false;
        using var left = new MemoryStream(); using(var writer = new Utf8JsonWriter(left)) Write(a,writer);
        using var right = new MemoryStream(); using(var writer = new Utf8JsonWriter(right)) Write(b,writer);
        return left.ToArray().SequenceEqual(right.ToArray());
    }
}
