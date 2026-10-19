using System.Collections.Immutable;

namespace RevitThyme.Core;

public enum TimberRunState { Accepted, Capturing, Fitting, Layout, Cutting, PreviewReady, Failed, Cancelled, Unconfirmed }
public sealed record TimberRunStart(Guid Id, Target Target, TimberFoldProfile Profile, TimberScope? Scope = null);
public sealed record TimberRunSummary(Guid RunId, Target Target, TimberRunState State, string Stage,
    string Directory, string? SourceFingerprint, int PanelCount, int SheetCount,
    double ElapsedMilliseconds, ImmutableArray<TimberDiagnostic> Diagnostics, bool DiagnosticsTruncated = false)
{
    public bool Terminal => State is TimberRunState.PreviewReady or TimberRunState.Failed or TimberRunState.Cancelled or TimberRunState.Unconfirmed;
    public bool ManufacturingReady => false;
    public bool PhysicalChecksOutstanding => true;
    public bool NativeImportOutstanding => true;
}
