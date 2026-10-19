namespace RevitThyme.Core;

internal static class TimberScopeValidation
{
    public static void Validate(TimberSnapshot snapshot, TimberScope scope, List<TimberDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(scope.PhaseId) || scope.PhaseId != snapshot.PhaseId)
            Error("phase_mismatch", "Choose a phase and capture geometry for that exact phase.");
        if (scope.DesignOptionIds.Any(string.IsNullOrWhiteSpace) ||
            scope.DesignOptionIds.Distinct(StringComparer.Ordinal).Count() != scope.DesignOptionIds.Length ||
            !scope.DesignOptionIds.Order(StringComparer.Ordinal).SequenceEqual(snapshot.DesignOptionIds.Order(StringComparer.Ordinal)))
            Error("option_mismatch", "Captured design options must match the explicitly chosen compatible options.");
        string[] included = scope.IncludeIds ?? [], excluded = scope.ExcludeIds ?? [];
        if (included.Intersect(excluded, StringComparer.Ordinal).Any())
            Error("scope_conflict", "A source cannot be both explicitly included and excluded.");
        foreach (string id in included.Concat(excluded).Distinct(StringComparer.Ordinal))
        {
            var source = snapshot.Sources.FirstOrDefault(s => s.Id == id);
            if (source is null)
                Error("scope_source_missing", "Requested source is absent from the captured inventory.", id);
            else if (excluded.Contains(id, StringComparer.Ordinal) && source.Disposition != TimberDisposition.Excluded)
                Error("scope_exclusion_missing", "Explicitly excluded source was not classified as excluded.", id);
        }
        if (string.IsNullOrWhiteSpace(snapshot.DocumentFingerprint))
            Error("missing_document_fingerprint", "Capture a source document fingerprint before inspection.");
        void Error(string code, string message, string? source = null) =>
            diagnostics.Add(new(code, TimberSeverity.Error, message, source));
    }
}
