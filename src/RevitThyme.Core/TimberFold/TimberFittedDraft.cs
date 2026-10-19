using System.Collections.Immutable;

namespace RevitThyme.Core;

public enum TimberFitState { Blocked, GeometryFitted }
// Axis-box clearance is expressed in the receiver's affine U/V/inward-depth coordinates.
// It encloses the requested physical clearance ball; grid enclosure adds conservative trim.
// Cell excess sums are rounded upward and may overlap; they bound enclosure/axis-box growth,
// not an assertion that all that extra area was removed from the receiving panel.
public sealed record TimberJointRecord(string OwnerPanelId, string ReceiverPanelId,
    ImmutableArray<ImmutableArray<TimberPoint2>> RemovedRingsInches, double RemovedAreaSquareInches,
    string RemovedTwiceGridArea, double ClearanceUInches, double ClearanceVInches, double ClearanceDepthInches,
    double CellGridEnclosureExcessAreaSum, double CellAxisBoxExpansionAreaSum)
{
    public TimberBoundaryRefinement? Refinement { get; init; }
}
public sealed record TimberRoofExposureRecord(string PanelId, string SourceId, string FaceId, ImmutableArray<string> CoveringPanelIds,
    double SourceAreaSquareInches, double ExposedAreaSquareInches, ImmutableArray<ImmutableArray<TimberPoint2>> RemovedRingsInches,
    double RemovedAreaSquareInches, string RemovedTwiceGridArea, string GridAreaDiscrepancy, double CellGridEnclosureExcessAreaSum)
{
    public TimberBoundaryRefinement? Refinement { get; init; }
}
public sealed record TimberWallEnvelopeRecord(string PanelId, string SourceId, string FaceId, ImmutableArray<string> RoofPanelIds,
    ImmutableArray<ImmutableArray<TimberPoint2>> AddedRingsInches, double AddedAreaSquareInches, string AddedTwiceGridArea,
    double SearchWorldUpInches, string ContactRule)
{
    public TimberBoundaryRefinement? Refinement { get; init; }
}
public sealed record TimberFittedDraft(TimberFitState State, TimberPanelDraft Draft,
    ImmutableArray<TimberJointRecord> Joints, TimberAssemblyVerification Verification,
    ImmutableArray<TimberDiagnostic> Diagnostics)
{
    public ImmutableArray<TimberRoofExposureRecord> RoofExposure { get; init; } = [];
    public ImmutableArray<TimberWallEnvelopeRecord> WallEnvelope { get; init; } = [];
    public bool ManufacturingReady => false;
}
