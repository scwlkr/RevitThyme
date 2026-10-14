namespace RevitThyme.Core;

// Level is Current/Above/Below/Unlimited or an actual Level UniqueId. All lengths are feet.
public sealed record RangePlane(string Level, double OffsetFeet);
public sealed record ViewRangeSettings(RangePlane Top, RangePlane Cut, RangePlane Bottom, RangePlane Depth);
public sealed record RangeLevel(string Id, string Name, double ElevationFeet);
public sealed record ViewRangeInfo(ViewRangeSettings Settings, RangeLevel[] Levels, double AssociatedLevelFeet,
    bool CanEdit, string? EditBlock);
public sealed record RoomDimensionOptions(double InsetFeet = 1.5, double TagClearanceFeet = 1);
public sealed record Point3(double X, double Y, double Z);
public sealed record DimensionLine(Point3 Start, Point3 End, string FirstReference, string SecondReference, double LengthFeet);
public sealed record RoomDimensionPreview(string RoomId, string RoomName, double WidthFeet, double DepthFeet,
    RoomDimensionOptions Options, DimensionLine[] Lines, long[] ExistingIds, string Fingerprint);

public interface IUtilityModel : IModel
{
    ViewRangeInfo ReadViewRange();
    RoomDimensionPreview PlanRoomDimensions(string roomId, RoomDimensionOptions options);
    IUtilityChange BeginUtilityChange(string label);
    bool VerifyDimensions(long[] ids, RoomDimensionPreview preview);
    bool ElementsAbsent(long[] ids);
}
public interface IUtilityChange : IChange
{
    void SetViewRange(ViewRangeSettings settings);
    void CreateRoomDimensions(RoomDimensionPreview preview, List<long> createdIds);
}
