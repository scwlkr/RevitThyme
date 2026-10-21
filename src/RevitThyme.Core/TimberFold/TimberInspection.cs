namespace RevitThyme.Core;

public static class TimberInspection
{
    // Even JSON-escaped strings fit the shared 256 KiB envelope. Full evidence is retained in run files.
    public const int MaximumDiagnostics = 24;
    public const int MaximumSourceSamples = 12;
    public const int MaximumDesignOptions = 8;

    public static TimberInspectionSummary Inspect(TimberSnapshot snapshot, TimberFoldProfile profile, TimberScope scope)
    {
        var diagnostics = GetDiagnostics(snapshot, profile, scope);
        var categories = Enum.GetValues<TimberCategory>().Select(category =>
        {
            var sources = snapshot.Sources.Where(s => s.Category == category).ToArray();
            return new TimberCategoryCount(category, sources.Count(s => s.Disposition == TimberDisposition.Included),
                sources.Count(s => s.Disposition == TimberDisposition.Excluded), sources.Count(s => s.Disposition == TimberDisposition.Unsupported),
                sources.Count(s => s.Disposition == TimberDisposition.Unaccounted));
        }).Where(c => c.Included + c.Excluded + c.Unsupported + c.Unaccounted > 0).ToArray();
        int errors = diagnostics.Count(d => d.Severity == TimberSeverity.Error);
        return new(errors == 0, true, Clip(snapshot.PhaseId), snapshot.DesignOptionIds.Take(MaximumDesignOptions).Select(id => Clip(id)).ToArray(),
            snapshot.Sources.Length, snapshot.Faces.Length, snapshot.Openings.Length, categories, errors,
            diagnostics.Count(d => d.Severity == TimberSeverity.Warning), diagnostics.Count(d => d.Severity == TimberSeverity.Info),
            diagnostics.OrderByDescending(d => d.Severity).Take(MaximumDiagnostics).Select(d => d with
            {
                Code = Clip(d.Code, 32), Message = Clip(d.Message, 128), SourceId = d.SourceId is null ? null : Clip(d.SourceId),
                GeometryId = d.GeometryId is null ? null : Clip(d.GeometryId)
            }).ToArray(), diagnostics.Length > MaximumDiagnostics,
            snapshot.Sources.Take(MaximumSourceSamples).Select(s => new TimberSourceSummary(Clip(s.Id), s.Category, s.Disposition, Clip(s.Reason, 64))).ToArray(),
            snapshot.Sources.Length > MaximumSourceSamples, snapshot.DesignOptionIds.Length > MaximumDesignOptions);
    }

    public static TimberDiagnostic[] GetDiagnostics(TimberSnapshot snapshot, TimberFoldProfile profile, TimberScope scope)
    {
        var diagnostics = new List<TimberDiagnostic>(profile.Validate());
        diagnostics.AddRange(snapshot.Diagnostics.Select(d => Enum.IsDefined(d.Severity) ? d :
            d with { Severity = TimberSeverity.Error, Code = "invalid_diagnostic_severity" }));
        foreach (var source in snapshot.Sources)
        {
            if (source.Disposition == TimberDisposition.Unsupported)
                diagnostics.Add(new("unsupported_source", TimberSeverity.Error, source.Reason, source.Id));
            if (source.Disposition == TimberDisposition.Unaccounted)
                diagnostics.Add(new("unaccounted_source", TimberSeverity.Error, "Relevant source has no resolved classification.", source.Id));
        }
        TimberAccounting.Validate(snapshot, diagnostics);
        TimberScopeValidation.Validate(snapshot, scope, diagnostics);
        TimberGeometryValidation.Validate(snapshot, diagnostics);
        return diagnostics.ToArray();
    }

    private static string Clip(string value, int maximum = 48) => value.Length <= maximum ? value : value[..maximum];
}
