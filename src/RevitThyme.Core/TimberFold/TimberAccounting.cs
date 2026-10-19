namespace RevitThyme.Core;

internal static class TimberAccounting
{
    public static void Validate(TimberSnapshot snapshot, List<TimberDiagnostic> diagnostics)
    {
        var sources = Index(snapshot.Sources, s => s.Id, "source");
        var faces = Index(snapshot.Faces, f => f.Id, "face");
        var openings = Index(snapshot.Openings, o => o.Id, "opening");
        foreach (var group in snapshot.Sources.GroupBy(s => s.UniqueId, StringComparer.Ordinal).Where(g => g.Count() > 1))
            Error("duplicate_source_identity", "Native source unique identity occurs more than once in the inventory.", group.First().Id);
        foreach (var source in snapshot.Sources)
        {
            if (string.IsNullOrWhiteSpace(source.UniqueId) || string.IsNullOrWhiteSpace(source.Reason) ||
                !Enum.IsDefined(source.Category) || !Enum.IsDefined(source.Disposition))
                Error("invalid_source", "Source identity, classification and reason must be recorded.", source.Id);
            if (source.Disposition != TimberDisposition.Included) continue;
            if (string.IsNullOrWhiteSpace(source.GeometryFingerprint))
                Error("missing_fingerprint", "Included source needs a captured geometry fingerprint.", source.Id);
            if (source.Category is TimberCategory.Wall or TimberCategory.Roof && source.FaceIds.Length == 0)
                Error("missing_face", "Included shell source has no accounted face.", source.Id);
            if (source.Category is TimberCategory.Door or TimberCategory.Window or TimberCategory.Opening && source.OpeningIds.Length == 0)
                Error("missing_opening", "Included hosted source has no accounted opening.", source.Id);
            if (source.Category is not (TimberCategory.Wall or TimberCategory.Roof or TimberCategory.Door or TimberCategory.Window or TimberCategory.Opening))
                Error("unsupported_source", "This source category cannot be included in the exterior shell kit.", source.Id);
            foreach (string id in source.FaceIds)
                if (!faces.TryGetValue(id, out var face) || face.SourceId != source.Id)
                    Error("missing_face", "Source face reference does not resolve to its captured face.", source.Id, id);
            foreach (string id in source.OpeningIds)
                if (!openings.TryGetValue(id, out var opening) || (opening.SourceId != source.Id && opening.HostSourceId != source.Id))
                    Error("missing_opening", "Source opening reference does not resolve to its captured opening.", source.Id, id);
            if (source.FaceIds.Distinct(StringComparer.Ordinal).Count() != source.FaceIds.Length ||
                source.OpeningIds.Distinct(StringComparer.Ordinal).Count() != source.OpeningIds.Length)
                Error("duplicate_reference", "Each source face/opening reference must occur once.", source.Id);
        }
        foreach (var face in snapshot.Faces)
        {
            if (!sources.TryGetValue(face.SourceId, out var source) || source.Disposition != TimberDisposition.Included ||
                !source.FaceIds.Contains(face.Id, StringComparer.Ordinal))
                Error("unaccounted_face", "Captured face has no matching included source reference.", face.SourceId, face.Id);
            else if ((source.Category == TimberCategory.Wall && face.Kind != TimberFaceKind.Wall) ||
                     (source.Category == TimberCategory.Roof && face.Kind != TimberFaceKind.Roof) ||
                     source.Category is not (TimberCategory.Wall or TimberCategory.Roof))
                Error("invalid_face_kind", "Face kind must match its wall or roof source.", face.SourceId, face.Id);
        }
        foreach (var opening in snapshot.Openings)
        {
            if (!sources.TryGetValue(opening.SourceId, out var source) || source.Disposition != TimberDisposition.Included ||
                !source.OpeningIds.Contains(opening.Id, StringComparer.Ordinal))
                Error("unaccounted_opening", "Captured opening has no matching included source reference.", opening.SourceId, opening.Id);
            if (!faces.TryGetValue(opening.HostFaceId, out var host) || host.SourceId != opening.HostSourceId)
                Error("missing_host", "Opening must identify the captured host source and face.", opening.SourceId, opening.Id);
        }
        if (!snapshot.Sources.Any(s => s.Disposition == TimberDisposition.Included && s.Category is TimberCategory.Wall or TimberCategory.Roof))
            Error("empty_shell", "No supported wall or roof sources were included.");

        Dictionary<string, T> Index<T>(IEnumerable<T> entries, Func<T, string> key, string kind)
        {
            var result = new Dictionary<string, T>(StringComparer.Ordinal);
            foreach (var entry in entries)
            {
                string id = key(entry);
                if (string.IsNullOrWhiteSpace(id) || !result.TryAdd(id, entry))
                    Error("duplicate_identity", $"Each {kind} needs a nonempty unique identity.", geometry: id);
            }
            return result;
        }
        void Error(string code, string message, string? source = null, string? geometry = null) =>
            diagnostics.Add(new(code, TimberSeverity.Error, message, source, geometry));
    }
}
