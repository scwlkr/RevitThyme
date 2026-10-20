using System.Text.Json;
using System.Text.Json.Serialization;

namespace RevitThyme.Core;

public enum OperationKind { Inspect, RenameView, ListViews, QueryElements, InspectElements, GetSelection, SetSelection, SetParameters, GetOperation,
    GetViewRange, SetViewRange, PreviewRoomDimensions, CreateRoomDimensions, RunQuery, RunModify, InspectTimberFold,
    PreviewTimberFold, GetTimberFoldRun, CancelTimberFoldRun, GenerateTimberFold, VerifyTimberFoldRun, ReconcileTimberFold, ExportViewImage,
    OpenDocument, ActivateDocument, SaveDocument, CloseDocument, ActivateView }
public enum Outcome { Inspected, Applied, Unchanged, Rejected, RolledBack, Unconfirmed, Completed }
public sealed record Target(int ProcessId, string ProcessStart, string SessionId,
    string DocumentId, string ViewId, long Revision);
public sealed record Snapshot(Target Target, string RevitVersion, string DocumentTitle,
    string DocumentPath, bool IsWorkshared, bool IsModified, string ViewName,
    string ViewType, long ViewElementId, bool CanModify, string? WriteBlock)
{
    public bool HasDocument => Target.DocumentId.Length > 0;
}
public sealed record OperationRequest(Guid Id, OperationKind Operation,
    Target? Target = null, string? ExpectedName = null, string? Name = null,
    ElementQuery? Query = null, string[]? ElementIds = null, int ParameterOffset = 0,
    ParameterEdit[]? Edits = null, Guid? OperationId = null,
    ViewRangeSettings? ExpectedRange = null, ViewRangeSettings? Range = null,
    RoomDimensionOptions? RoomOptions = null, RoomDimensionPreview? RoomPreview = null, CodeRequest? Code = null,
    TimberFoldRequest? TimberFold = null, ViewImageRequest? ImageExport = null, DocumentLifecycleRequest? DocumentLifecycle = null)
{
    public bool IsWrite => Operation is OperationKind.RenameView or OperationKind.SetParameters or OperationKind.SetSelection
        or OperationKind.SetViewRange or OperationKind.CreateRoomDimensions or OperationKind.RunModify or OperationKind.RunQuery
        or OperationKind.InspectTimberFold or OperationKind.PreviewTimberFold or OperationKind.CancelTimberFoldRun or OperationKind.GenerateTimberFold
        or OperationKind.ReconcileTimberFold or OperationKind.ExportViewImage or OperationKind.OpenDocument or OperationKind.ActivateDocument or OperationKind.SaveDocument
        or OperationKind.CloseDocument or OperationKind.ActivateView;
    public bool EquivalentTo(OperationRequest other) => JsonSerializer.Serialize(this, Protocol.Json) == JsonSerializer.Serialize(other, Protocol.Json);
}
public sealed record OperationResult(Guid Id, Outcome Outcome, string Code, string Message,
    Snapshot? Snapshot = null, string? PreviousName = null, long[]? ChangedIds = null,
    ElementPage? Elements = null, ElementDetail[]? Details = null, string[]? Selection = null,
    ParameterEdit[]? ParameterChanges = null, ViewRangeInfo? ViewRange = null,
    RoomDimensionPreview? RoomDimensions = null, CodeResult? Execution = null, TimberFoldReply? TimberFold = null,
    TimberRunSummary? TimberRun = null, ViewImageResult? ImageExport = null, DocumentLifecycleResult? DocumentLifecycle = null)
{
    public bool Succeeded => Outcome is Outcome.Inspected or Outcome.Applied or Outcome.Unchanged or Outcome.Completed;
    public static OperationResult Reject(OperationRequest request, string code, string message) =>
        new(request.Id, Outcome.Rejected, code, message);
}
public interface IOperationClient
{
    Task<OperationResult> ExecuteAsync(OperationRequest request, CancellationToken cancellationToken = default);
}
public static class Protocol
{
    public const int Version = 11;
    public const int MaxBytes = 65536;
    public static readonly JsonSerializerOptions Json = CreateOptions();
    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        return options;
    }
}
