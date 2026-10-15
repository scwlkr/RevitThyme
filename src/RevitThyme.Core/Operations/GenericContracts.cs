namespace RevitThyme.Core;

public sealed record ElementQuery(string? CategoryId = null, string? NameContains = null,
    bool IncludeTypes = false, int Offset = 0, int Limit = 50);
public sealed record ElementInfo(string UniqueId, long Id, string Name, string Category,
    string? CategoryId, string Class, bool IsType, bool? IsTemplate = null);
public sealed record ElementPage(ElementInfo[] Items, int? NextOffset);
public sealed record ParameterValue(string Storage, string? Text = null, long? Integer = null, double? Number = null);
public sealed record ParameterInfo(string Id, string Name, string DataType, bool IsReadOnly,
    ParameterValue Value, bool Truncated = false, bool IsShared = false, Guid? SharedGuid = null,
    string? BuiltInParameter = null, bool HasValue = false);
public sealed record ElementDetail(ElementInfo Element, ParameterInfo[] Parameters, int? NextParameterOffset);
public sealed record ParameterEdit(string ElementId, string ParameterId, ParameterValue Expected, ParameterValue Value);

public interface IGenericModel : IModel
{
    ElementPage ListViews(ElementQuery query);
    ElementPage QueryElements(ElementQuery query);
    ElementDetail[] InspectElements(string[] ids, int parameterOffset);
    string[] GetSelection();
    void SetSelection(string[] ids);
    ParameterInfo ReadParameter(string elementId, string parameterId);
    IParameterChange BeginParameterChange();
}
public interface IParameterChange : IChange
{
    void SetParameter(ParameterEdit edit);
}
