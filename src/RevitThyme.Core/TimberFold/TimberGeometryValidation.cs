namespace RevitThyme.Core;

internal static class TimberGeometryValidation
{
    public static void Validate(TimberSnapshot snapshot, List<TimberDiagnostic> diagnostics)
    {
        foreach (var face in snapshot.Faces)
        {
            if (!ValidFrame(face)) Error("invalid_frame", "Face needs a finite orthonormal right-handed local frame.", face.SourceId, face.Id);
            if (face.Kind == TimberFaceKind.Wall && Math.Abs(face.Normal.Z) > 1e-6)
                Error("unsupported_wall", "Only vertical planar wall faces are supported.", face.SourceId, face.Id);
            if (face.Kind == TimberFaceKind.Roof && face.Normal.Z <= 0)
                Error("unsupported_roof", "A captured roof top face must point upward.", face.SourceId, face.Id);
            Rings(face.RingsFeet, true, face.SourceId, face.Id);
        }
        foreach (var opening in snapshot.Openings)
        {
            Rings(opening.VoidRingsFeet, true, opening.SourceId, opening.Id);
            if (!Enum.IsDefined(opening.Kind)) Error("invalid_opening", "Opening kind is unsupported.", opening.SourceId, opening.Id);
            if (opening.Kind == TimberOpeningKind.Window)
            {
                if (!opening.GlazingResolved || opening.PaneRingsFeet.Length == 0)
                    Error("unresolved_glazing", "Window needs unambiguous projected glazing panes in its host frame.", opening.SourceId, opening.Id);
                Rings(opening.PaneRingsFeet, true, opening.SourceId, opening.Id);
            }
            else Rings(opening.PaneRingsFeet, false, opening.SourceId, opening.Id);
            Rings(opening.OpaqueRingsFeet ?? [], false, opening.SourceId, opening.Id);
        }
        void Rings(TimberPoint2[][] rings, bool required, string source, string geometry)
        {
            if ((required && rings.Length == 0) || rings.Any(r => r.Length < 3 ||
                r.Any(p => !double.IsFinite(p.X) || !double.IsFinite(p.Y)) || r.Distinct().Count() < 3))
                Error("invalid_boundary", "Captured boundary requires at least three distinct finite points per ring.", source, geometry);
        }
        void Error(string code, string message, string source, string geometry) =>
            diagnostics.Add(new(code, TimberSeverity.Error, message, source, geometry));
    }

    private static bool ValidFrame(TimberFace face)
    {
        var origin = face.OriginFeet; var u = face.U; var v = face.V; var n = face.Normal;
        if (!Finite(origin) || !Finite(u) || !Finite(v) || !Finite(n)) return false;
        double handed = (u.Y * v.Z - u.Z * v.Y) * n.X + (u.Z * v.X - u.X * v.Z) * n.Y + (u.X * v.Y - u.Y * v.X) * n.Z;
        return Math.Abs(Dot(u, u) - 1) <= 1e-6 && Math.Abs(Dot(v, v) - 1) <= 1e-6 && Math.Abs(Dot(n, n) - 1) <= 1e-6 &&
            Math.Abs(Dot(u, v)) <= 1e-6 && Math.Abs(Dot(u, n)) <= 1e-6 && Math.Abs(Dot(v, n)) <= 1e-6 && Math.Abs(handed - 1) <= 1e-6;
    }
    private static bool Finite(TimberPoint3 p) => double.IsFinite(p.X) && double.IsFinite(p.Y) && double.IsFinite(p.Z);
    private static double Dot(TimberPoint3 a, TimberPoint3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
}
