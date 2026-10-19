using Autodesk.Revit.DB;
using Autodesk.Revit.DB.IFC;
using RevitThyme.Core;

namespace RevitThyme.Addin;

internal static class NativeTimberOpenings
{
    internal static void Extract(Document doc, Element element, bool explicitlyIncluded, Options options,
        double chordFeet, List<TimberFace> faces, List<TimberSource> sources,
        List<TimberOpening> openings, List<TimberDiagnostic> diagnostics)
    {
        var id = NativeTimberExtraction.Id(element); var category = NativeTimberExtraction.Category(element);
        Element? host = element switch { FamilyInstance instance => instance.Host, Opening opening => opening.Host, _ => null };
        var hostId = host is null ? "" : NativeTimberExtraction.Id(host);
        var hostSource = sources.SingleOrDefault(s => s.Id == hostId);
        if (hostSource?.Disposition == TimberDisposition.Excluded && !explicitlyIncluded)
        {
            sources.Add(NativeTimberExtraction.Source(element, category, TimberDisposition.Excluded,
                "Opening belongs to an excluded host.", [], []));
            return;
        }
        if (hostSource?.Disposition != TimberDisposition.Included)
        {
            sources.Add(NativeTimberExtraction.Source(element, category, TimberDisposition.Unsupported,
                "Opening has no completely supported, included native host.", [], []));
            diagnostics.Add(new("opening_host_unresolved", TimberSeverity.Error,
                "Hosted opening does not map to an included native shell host.", id));
            return;
        }
        try
        {
            var candidates = faces.Where(f => f.SourceId == hostId).Select(f =>
                (Face: f, Rings: ProjectCutout(doc, element, host!, f, chordFeet))).Where(c =>
                    OverlapsBounds(c.Rings[0], c.Face.RingsFeet[0])).ToArray();
            if (candidates.Length != 1)
                throw new InvalidOperationException($"Native cutout maps to {candidates.Length} host face regions; exactly one is required.");
            var (face, voidRings) = candidates[0];
            var isWindow = category == TimberCategory.Window;
            var panes = Array.Empty<TimberPoint2[]>(); var opaque = Array.Empty<TimberPoint2[]>();
            if (isWindow)
            {
                var projections = ProjectWindow(doc, element, face, options, chordFeet);
                panes = projections.Panes; opaque = projections.Opaque;
                if (panes.Length == 0) throw new InvalidOperationException(
                    "No native planar glazing faces with material transparency of at least 50 were found; glazing is ambiguous.");
            }
            var openingId = $"O-{id}";
            openings.Add(new(openingId, id, hostId, face.Id,
                isWindow ? TimberOpeningKind.Window : host is Wall ? TimberOpeningKind.Door : TimberOpeningKind.Roof,
                voidRings, panes, true, opaque));
            sources.Add(NativeTimberExtraction.Source(element, category, TimberDisposition.Included,
                isWindow ? "Native wall cutout and material-classified planar glazing/opaque projections." : "Native host cutout boundary.",
                [], [openingId]));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            sources.Add(NativeTimberExtraction.Source(element, category, TimberDisposition.Unsupported,
                "Native opening or glazing correspondence is unresolved.", [], []));
            diagnostics.Add(new("opening_geometry_unresolved", TimberSeverity.Error, ex.Message, id));
        }
    }

    private static TimberPoint2[][] ProjectCutout(Document doc, Element element, Element host,
        TimberFace frame, double chordFeet)
    {
        using var cut = Cutout(doc, element, host, frame);
        return NativeTimberGeometry.Rings([cut], frame,
            host is RoofBase ? chordFeet * Math.Abs(frame.Normal.Z) : chordFeet,
            projectionDirection: host is RoofBase ? XYZ.BasisZ : null);
    }

    private static CurveLoop Cutout(Document doc, Element element, Element host, TimberFace frame)
    {
        if (element is FamilyInstance instance && host is Wall wall)
        {
            var cut = ExporterIFCUtils.GetInstanceCutoutFromWall(doc, wall, instance, out var direction);
            if (cut is null || direction is null || direction.GetLength() < 1e-10)
                throw new InvalidOperationException("Revit did not resolve the actual instance wall cutout.");
            var dot = Math.Abs(direction.Normalize().DotProduct(wall.Orientation.Normalize()));
            if (dot < 1 - 1e-8) throw new InvalidOperationException("Oblique wall cuts cannot be represented as through-thickness laser contours.");
            return cut;
        }
        if (element is Opening opening)
        {
            var curves = new List<Curve>();
            if (opening.IsRectBoundary)
            {
                var rectangle = opening.BoundaryRect.Cast<XYZ>().ToArray();
                if (rectangle.Length != 2) throw new InvalidOperationException("Native rectangular opening has no unambiguous corner pair.");
                var a = rectangle[0]; var b = rectangle[1];
                XYZ[] points;
                if (host is Wall)
                {
                    var origin = NativeTimberGeometry.Vector(frame.OriginFeet);
                    var u = NativeTimberGeometry.Vector(frame.U); var v = NativeTimberGeometry.Vector(frame.V);
                    var normal = NativeTimberGeometry.Vector(frame.Normal);
                    var localA = a.Subtract(origin); var localB = b.Subtract(origin);
                    if (Math.Abs(localA.DotProduct(normal) - localB.DotProduct(normal)) > 1e-7)
                        throw new InvalidOperationException("Native wall opening diagonal does not lie in a plane parallel to its exterior host face.");
                    var ax = localA.DotProduct(u); var ay = localA.DotProduct(v);
                    var bx = localB.DotProduct(u); var by = localB.DotProduct(v);
                    if (Math.Abs(ax - bx) < 1e-8 || Math.Abs(ay - by) < 1e-8)
                        throw new InvalidOperationException("Native wall opening has a degenerate rectangle in its host face plane.");
                    XYZ World(double x, double y) => origin.Add(u.Multiply(x)).Add(v.Multiply(y));
                    // BoundaryRect is the native opening's diagonal, not an element bounding box.
                    // Wall-local U/V axes preserve rotated-wall width and vertical opening height.
                    points = [World(ax, ay), World(bx, ay), World(bx, by), World(ax, by)];
                }
                else if (host is RoofBase)
                {
                    if (Math.Abs(a.Z - b.Z) > 1e-8)
                        throw new InvalidOperationException("Nonhorizontal rectangular roof opening needs a native curve boundary.");
                    points = [a, new(b.X, a.Y, a.Z), b, new(a.X, b.Y, b.Z)];
                }
                else throw new InvalidOperationException("Native rectangular opening has an unsupported host category.");
                for (var i = 0; i < 4; i++) curves.Add(Line.CreateBound(points[i], points[(i + 1) % 4]));
            }
            else curves.AddRange(opening.BoundaryCurves.Cast<Curve>());
            return CurveLoop.Create(curves);
        }
        throw new InvalidOperationException("Only wall-hosted door/window instances and native wall/roof openings have supported cutout correspondence.");
    }

    private static (TimberPoint2[][] Panes, TimberPoint2[][] Opaque) ProjectWindow(Document doc,
        Element element, TimberFace host, Options options, double chordFeet)
    {
        using var geometry = element.get_Geometry(options) ?? throw new InvalidOperationException("Window has no native Fine geometry.");
        var panes = new Dictionary<string, TimberPoint2[][]>(StringComparer.Ordinal);
        var opaque = new Dictionary<string, TimberPoint2[][]>(StringComparer.Ordinal);
        var normal = NativeTimberGeometry.Vector(host.Normal);
        foreach (var solid in NativeTimberGeometry.Solids(geometry))
        foreach (Face face in solid.Faces)
        {
            if (face.Area <= 1e-10) continue;
            var material = doc.GetElement(face.MaterialElementId) as Material;
            var glazing = material is { Transparency: >= 50 };
            if (face is not PlanarFace plane)
            {
                // Circular extrusions have a planar front/back silhouette and edge-on cylindrical sides.
                if (face is CylindricalFace cylinder && Math.Abs(cylinder.Axis.Normalize().DotProduct(normal)) > 1 - 1e-8)
                    continue;
                // Silhouette of curved opaque profiles is not captured by planar-face projection.
                throw new InvalidOperationException("Window contains nonplanar solid faces; glazing/frame silhouette is not fully resolved.");
            }
            var alignment = Math.Abs(plane.FaceNormal.DotProduct(normal));
            if (alignment < 1e-8) continue; // Edge-on faces have zero projected area.
            if (glazing && alignment < 1 - 1e-8)
                throw new InvalidOperationException("Glazing is oblique to its host; a planar pane projection would be ambiguous.");
            var rings = NativeTimberGeometry.Rings(plane.GetEdgesAsCurveLoops(), host, chordFeet);
            // Opposite sides of the same solid project identically. Deduplicate before NonZero union.
            var key = NativeTimberGeometry.Hash(rings.Select(r => r.Select(p => new[] { Math.Round(p.X, 10), Math.Round(p.Y, 10) })));
            (glazing ? panes : opaque).TryAdd(key, rings);
        }
        // Contours carry signed winding: each face has CCW exterior and CW holes.
        // Core unions each material class using NonZero winding, then clips glass minus opaque to the cutout.
        return (panes.OrderBy(p => p.Key, StringComparer.Ordinal).SelectMany(p => p.Value).ToArray(),
            opaque.OrderBy(p => p.Key, StringComparer.Ordinal).SelectMany(p => p.Value).ToArray());
    }

    private static bool OverlapsBounds(TimberPoint2[] a, TimberPoint2[] b)
    {
        const double tolerance = 1e-8;
        return a.Min(p => p.X) < b.Max(p => p.X) - tolerance && a.Max(p => p.X) > b.Min(p => p.X) + tolerance &&
            a.Min(p => p.Y) < b.Max(p => p.Y) - tolerance && a.Max(p => p.Y) > b.Min(p => p.Y) + tolerance;
    }
}
