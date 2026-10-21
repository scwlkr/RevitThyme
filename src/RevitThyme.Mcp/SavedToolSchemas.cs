using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace RevitThyme.Mcp;

internal static class SavedToolSchemas
{
    // JsonElement exports as the schema `true`. These particular values are
    // objects at runtime; advertise that same constraint instead of any JSON.
    public static McpRequestHandler<ListToolsRequestParams, ListToolsResult> Filter(
        McpRequestHandler<ListToolsRequestParams, ListToolsResult> next) => async (context, token) =>
    {
        var result = await next(context, token);
        foreach (var tool in result.Tools.Where(t => t.Name is "save_tool" or "run_tool" or "prepare_result_checks" or "verify_result" or "verify_tool"))
        {
            var schema = JsonNode.Parse(tool.InputSchema.GetRawText())!;
            var properties = schema["properties"]!;
            var inputsOwner = tool.Name != "save_tool" ? properties
                : properties["definition"]!["properties"]!["tests"]!["items"]!["properties"]!;
            inputsOwner["inputs"] = new JsonObject
            {
                ["type"] = "object", ["additionalProperties"] = true,
                ["description"] = "JSON input object matching the tool's typed input definitions. Supply an object, not a string containing JSON."
            };
            var plan = tool.Name == "save_tool" ? properties["definition"]?["properties"]?["resultChecks"] : properties["plan"];
            if (plan?["properties"]?["checks"]?["items"]?["properties"] is JsonObject checks)
                checks["expected"] = new JsonObject
                {
                    ["description"] = "Expected JSON value, including explicit null. Supply exactly one of expected or expectedInput for equals.",
                    ["anyOf"] = new JsonArray(
                        new JsonObject { ["type"] = "string" }, new JsonObject { ["type"] = "number" },
                        new JsonObject { ["type"] = "boolean" }, new JsonObject { ["type"] = "null" },
                        new JsonObject { ["type"] = "object", ["additionalProperties"] = true },
                        new JsonObject { ["type"] = "array", ["items"] = new JsonObject() })
                };
            tool.InputSchema = JsonSerializer.SerializeToElement(schema);
        }
        return result;
    };
}
