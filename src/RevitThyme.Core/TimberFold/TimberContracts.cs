namespace RevitThyme.Core;

public enum TimberSeverity { Info, Warning, Error }
public enum TimberCategory { Wall, Roof, Door, Window, Link, Import, Floor, Site, Furniture, Mep, Other, Opening }
public enum TimberDisposition { Included, Excluded, Unsupported, Unaccounted }
public enum TimberFaceKind { Wall, Roof }
public enum TimberOpeningKind { Door, Window, Roof }
public enum TimberRotation { Fixed, QuarterTurn }
public enum TimberGrain { None, AlongStockWidth, AlongStockHeight }
public enum TimberKerfOwnership { LaserSoftware }

public sealed record TimberDiagnostic(string Code, TimberSeverity Severity, string Message,
    string? SourceId = null, string? GeometryId = null);
public sealed record TimberScope(string PhaseId, string[] DesignOptionIds,
    string[]? IncludeIds = null, string[]? ExcludeIds = null);
public sealed record TimberPoint2(double X, double Y);
public sealed record TimberPoint3(double X, double Y, double Z);

// Coordinates and rings are in Revit feet. Frame basis vectors are dimensionless.
// Rings include the outer boundary followed by holes; pane rings use the host face frame.
public sealed record TimberFace(string Id, string SourceId, TimberFaceKind Kind,
    TimberPoint3 OriginFeet, TimberPoint3 U, TimberPoint3 V, TimberPoint3 Normal,
    TimberPoint2[][] RingsFeet);
public sealed record TimberOpening(string Id, string SourceId, string HostSourceId, string HostFaceId,
    TimberOpeningKind Kind, TimberPoint2[][] VoidRingsFeet, TimberPoint2[][] PaneRingsFeet, bool GlazingResolved, TimberPoint2[][]? OpaqueRingsFeet = null);
public sealed record TimberSource(string Id, string UniqueId, TimberCategory Category,
    TimberDisposition Disposition, string Reason, string[] FaceIds, string[] OpeningIds,
    string GeometryFingerprint = "");
public sealed record TimberSnapshot(Target Target, string DocumentFingerprint, string PhaseId,
    string[] DesignOptionIds, TimberSource[] Sources, TimberFace[] Faces,
    TimberOpening[] Openings, TimberDiagnostic[] Diagnostics);

public sealed record TimberCategoryCount(TimberCategory Category, int Included, int Excluded,
    int Unsupported, int Unaccounted);
public sealed record TimberSourceSummary(string Id, TimberCategory Category, TimberDisposition Disposition, string Reason);
public sealed record TimberInspectionSummary(bool ReadyForPreview, bool PhysicalChecksOutstanding,
    string PhaseId, string[] DesignOptionIds, int SourceCount, int FaceCount, int OpeningCount,
    TimberCategoryCount[] Categories, int ErrorCount, int WarningCount, int InfoCount,
    TimberDiagnostic[] Diagnostics, bool DiagnosticsTruncated, TimberSourceSummary[] SourceSample,
    bool SourcesTruncated, bool DesignOptionsTruncated = false);
