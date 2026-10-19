using System.Collections.Immutable;

namespace RevitThyme.Core;

public enum TimberDraftState { Blocked, ContoursValidated }
public sealed record TimberPanelFrame(TimberPoint3 OriginInches, TimberPoint3 U, TimberPoint3 V, TimberPoint3 OutwardNormal);
public sealed record TimberTriangle(TimberPoint2 A, TimberPoint2 B, TimberPoint2 C);
// Rings are open vertex sequences with implicit closing edges: outside CCW, holes CW.
public sealed record TimberPanel(string Id, string SourceId, string FaceId, TimberFaceKind Kind,
    TimberPanelFrame Frame, double ThicknessInches, ImmutableArray<ImmutableArray<TimberPoint2>> RingsInches,
    ImmutableArray<TimberTriangle> Triangles, double AreaSquareInches);
public sealed record TimberFacePartMap(string SourceId, string FaceId, ImmutableArray<string> PanelIds);
public sealed record TimberOpeningPartMap(string SourceId, string OpeningId, string HostFaceId, ImmutableArray<string> PanelIds);
public sealed record TimberPanelDraft(TimberDraftState State, TimberPoint3 AssemblyOriginFeet,
    ImmutableArray<TimberPanel> Panels, ImmutableArray<TimberFacePartMap> FaceMap, ImmutableArray<TimberOpeningPartMap> OpeningMap,
    ImmutableArray<TimberDiagnostic> Diagnostics)
{
    // This stage has not established joints, collisions, nesting or deliverable readback.
    public bool ManufacturingReady => false;
}
