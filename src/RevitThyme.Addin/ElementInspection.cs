using System.Globalization;
using System.IO;
using System.Text.Json;
using Autodesk.Revit.DB;
using RevitThyme.Core;
using ParameterValue = RevitThyme.Core.ParameterValue;

namespace RevitThyme.Addin;

internal static class ElementInspection
{
    public static Element Resolve(Document doc, string id) => doc.GetElement(id)
        ?? throw new OperationException("element_missing", "Element unique ID is missing from this document.");
    public static Parameter ResolveParameter(Document doc, string elementId, string parameterId) => Resolve(doc, elementId).Parameters
        .Cast<Parameter>().SingleOrDefault(p => p.Id.Value.ToString(CultureInfo.InvariantCulture) == parameterId)
        ?? throw new OperationException("parameter_missing", "Parameter ID is missing from this element.");
    public static ElementInfo Summary(Element e) => new(e.UniqueId, e.Id.Value, Clip(e.Name, 200), Clip(e.Category?.Name ?? "", 100),
        e.Category?.Id.Value.ToString(CultureInfo.InvariantCulture), e.GetType().Name, e is ElementType, e is View v ? v.IsTemplate : null);
    public static ParameterInfo Read(Parameter p)
    {
        string? raw = p.StorageType == StorageType.String ? p.AsString() ?? "" : null;
        var value = p.StorageType switch
        {
            StorageType.String => new ParameterValue("String", Text: Clip(raw!, 512)),
            StorageType.Integer => new ParameterValue("Integer", Integer: p.AsInteger()),
            StorageType.Double => new ParameterValue("Double", Number: p.AsDouble()),
            StorageType.ElementId => new ParameterValue("ElementId", Integer: p.AsElementId().Value),
            _ => new ParameterValue("None")
        };
        return new(p.Id.Value.ToString(CultureInfo.InvariantCulture), Clip(p.Definition.Name, 100),
            p.Definition.GetDataType().TypeId, p.IsReadOnly || p.StorageType is StorageType.ElementId or StorageType.None,
            value, raw?.Length > 512, p.IsShared, p.IsShared ? p.GUID : null,
            p.Definition is InternalDefinition definition && definition.BuiltInParameter != BuiltInParameter.INVALID
                ? definition.BuiltInParameter.ToString() : null, p.HasValue);
    }
    public static ElementDetail Detail(Document doc, string id, int offset)
    {
        var e = Resolve(doc, id); var parameters = e.Parameters.Cast<Parameter>().OrderBy(p => p.Id.Value).Skip(offset).Take(21).ToArray();
        var summary = Summary(e); var page = new List<ParameterInfo>();
        foreach (var p in parameters.Take(20))
        {
            page.Add(Read(p));
            if (JsonSerializer.SerializeToUtf8Bytes(new ElementDetail(summary, page.ToArray(), offset + page.Count), Protocol.Json).Length > (Protocol.MaxOperationBytes - 4096) / 5)
            { page.RemoveAt(page.Count - 1); break; }
        }
        return new(summary, page.ToArray(), parameters.Length > page.Count ? offset + page.Count : null);
    }
    public static ElementPage Page(IEnumerable<Element> elements, ElementQuery query)
    {
        // Native collector order is stable while captured revision is unchanged; avoid materializing the entire model.
        var page = elements.Where(e => query.NameContains is null || e.Name.Contains(query.NameContains, StringComparison.OrdinalIgnoreCase))
            .Skip(query.Offset).Take(query.Limit + 1).ToArray();
        var items = page.Take(query.Limit).Select(Summary).ToList();
        while (JsonSerializer.SerializeToUtf8Bytes(items, Protocol.Json).Length > Protocol.MaxOperationBytes - 4096) items.RemoveAt(items.Count - 1);
        return new(items.ToArray(), page.Length > items.Count ? query.Offset + items.Count : null);
    }
    public static void Set(Document doc, ParameterEdit edit)
    {
        var p = ResolveParameter(doc, edit.ElementId, edit.ParameterId);
        bool accepted = edit.Value.Storage switch
        {
            "String" => p.Set(edit.Value.Text!), "Integer" => p.Set(checked((int)edit.Value.Integer!)),
            "Double" => p.Set(edit.Value.Number!.Value), _ => false
        };
        if (!accepted && Read(p).Value != edit.Value) throw new InvalidDataException("Revit refused the parameter value.");
    }
    private static string Clip(string s, int max) => s.Length <= max ? s : s[..max];
}
