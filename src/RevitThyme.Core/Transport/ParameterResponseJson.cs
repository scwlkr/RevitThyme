using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace RevitThyme.Core;

// Older v4 clients reject unknown response members. Negotiate only the response projection;
// retained outcomes and exact edit/replay arguments always keep the canonical typed contract.
public static class ParameterResponseJson
{
    private static readonly JsonSerializerOptions Legacy = CreateLegacy();
    public static JsonSerializerOptions For(bool includeParameterIdentity) => includeParameterIdentity ? Protocol.Json : Legacy;

    private static JsonSerializerOptions CreateLegacy()
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(info =>
        {
            if (info.Type != typeof(ParameterInfo)) return;
            foreach (var property in info.Properties.Where(p => p.Name is "isShared" or "sharedGuid" or "builtInParameter" or "hasValue"))
                property.ShouldSerialize = (_, _) => false;
        });
        return new(Protocol.Json) { TypeInfoResolver = resolver };
    }
}
