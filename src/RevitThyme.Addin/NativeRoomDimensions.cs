using System.Security.Cryptography;
using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using RevitThyme.Core;

namespace RevitThyme.Addin;

internal static class NativeRoomDimensions
{
    private const double Tolerance = 1e-5;
    public static RoomDimensionPreview Plan(Document doc, string roomId, RoomDimensionOptions options)
    {
        if (doc.ActiveView is not ViewPlan view || view.IsTemplate || view.ViewType != ViewType.FloorPlan)
            throw new OperationException("unsupported_view", "Room Dimensions requires a floor plan.");
        if (view.GetCategoryHidden(new ElementId(BuiltInCategory.OST_Dimensions)))
            throw new OperationException("dimensions_hidden", "Show the Dimensions category in this view before creating dimensions.");
        if (doc.GetElement(roomId) is not Room room || room.Area <= Tolerance || room.Location is not LocationPoint || room.LevelId != view.GenLevel.Id)
            throw new OperationException("unsupported_room", "Choose a placed, enclosed room on the active floor plan's level.");
        if (view.get_Parameter(BuiltInParameter.VIEW_PHASE)?.AsElementId() != room.get_Parameter(BuiltInParameter.ROOM_PHASE)?.AsElementId())
            throw new OperationException("unsupported_room", "Room and floor plan must use the same phase.");
        using var boundaries = new SpatialElementBoundaryOptions { SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish };
        var loops = room.GetBoundarySegments(boundaries);
        if (loops is null || loops.Count != 1 || loops[0].Count != 4)
            throw new OperationException("unsupported_room", "Start with a rectangular room bounded by four straight native walls, without islands.");
        var segments = loops[0];
        var edges = segments.Select(s => s.GetCurve() as Line ?? throw new OperationException("unsupported_room", "Curved room boundaries are not supported.")).ToArray();
        XYZ u = (edges[0].GetEndPoint(1) - edges[0].GetEndPoint(0)).Normalize();
        if (Math.Abs(u.Z) > Tolerance) throw new OperationException("unsupported_room", "Room boundaries must be horizontal.");
        XYZ v = new(-u.Y, u.X, 0);
        var corners = edges.Select(e => e.GetEndPoint(0)).ToArray();
        if (view.CropBoxActive)
        {
            var crop = view.CropBox; var inverse = crop.Transform.Inverse;
            if (corners.Select(inverse.OfPoint).Any(p => p.X < crop.Min.X || p.X > crop.Max.X || p.Y < crop.Min.Y || p.Y > crop.Max.Y))
                throw new OperationException("room_outside_crop", "The room must lie fully inside the active view crop.");
            using var shape = view.GetCropRegionShapeManager();
            if (shape.Split || shape.ShapeSet)
                throw new OperationException("unsupported_crop", "Use an unsplit rectangular crop for Room Dimensions.");
        }
        for (int i = 0; i < 4; i++)
        {
            XYZ d = (edges[i].GetEndPoint(1) - corners[i]).Normalize();
            if (edges[i].GetEndPoint(1).DistanceTo(corners[(i + 1) % 4]) > Tolerance || Math.Abs(d.Z) > Tolerance
                || Math.Abs(d.DotProduct(i % 2 == 0 ? v : u)) > Tolerance)
                throw new OperationException("unsupported_room", "Room boundaries must form a closed rectangle.");
        }
        double minU = corners.Min(p => p.DotProduct(u)), maxU = corners.Max(p => p.DotProduct(u));
        double minV = corners.Min(p => p.DotProduct(v)), maxV = corners.Max(p => p.DotProduct(v));
        double width = maxU - minU, depth = maxV - minV;
        if (Math.Min(width, depth) <= 2 * options.InsetFeet + Tolerance)
            throw new OperationException("room_too_small", "Room is too small for the chosen dimension inset.");
        var refs = segments.Select((s, i) => Face(doc, s, edges[i])).ToArray();
        var tags = new FilteredElementCollector(doc, view.Id).OfCategory(BuiltInCategory.OST_RoomTags).OfType<RoomTag>().Where(t => t.Room?.Id == room.Id)
            .Select(t => t.get_BoundingBox(view) ?? throw new OperationException("tag_bounds", "Room tag bounds cannot be read.")).ToArray();
        // Pick an inset side clear of every room tag, reserving its actual projected bounding box plus clearance.
        double y = Choose(minV + options.InsetFeet, maxV - options.InsetFeet, v, tags, options.TagClearanceFeet);
        double x = Choose(minU + options.InsetFeet, maxU - options.InsetFeet, u, tags, options.TagClearanceFeet);
        double z = view.GenLevel.Elevation;
        Point3 Point(double a, double b) { XYZ p = u * a + v * b + XYZ.BasisZ * z; return new(p.X, p.Y, p.Z); }
        DimensionLine[] lines = [new(Point(minU, y), Point(maxU, y), refs[1], refs[3], width),
            new(Point(x, minV), Point(x, maxV), refs[0], refs[2], depth)];
        var viewDimensions = new FilteredElementCollector(doc).OfClass(typeof(Dimension)).Cast<Dimension>().Where(d => d.OwnerViewId == view.Id).ToArray();
        var matches = lines.Select(l => viewDimensions.Where(d => Matches(doc, d, l)).ToArray()).ToArray();
        if (matches.Any(m => m.Length > 1)) throw new OperationException("duplicate_dimensions", "Multiple dimensions already measure these room faces. Resolve them in Revit.");
        var existing = matches.SelectMany(m => m).Select(d => d.Id.Value).ToArray();
        if (existing.Length == 2 && !Verify(doc, existing, new(roomId, room.Name, width, depth, options, lines, existing, "")))
            throw new OperationException("dimension_conflict", "Existing dimensions are locked or do not match the room lengths.");
        var fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        { roomId, width, depth, options, lines, existing, tags = tags.Select(b => new { min = ToPoint(b.Min), max = ToPoint(b.Max) }).ToArray() }, Protocol.Json)));
        return new(roomId, room.Name, width, depth, options, lines, existing, fingerprint);
    }
    private static double Choose(double first, double second, XYZ axis, BoundingBoxXYZ[] tags, double clearance)
    {
        bool Clear(double position) => tags.All(b =>
        {
            var points = BoxCorners(b).Select(p => p.DotProduct(axis)).ToArray();
            return position < points.Min() - clearance || position > points.Max() + clearance;
        });
        return Clear(first) ? first : Clear(second) ? second : throw new OperationException("tag_collision", "Neither inset side leaves enough room for the room tag. Move the tag or reduce the inset/clearance.");
    }
    private static IEnumerable<XYZ> BoxCorners(BoundingBoxXYZ b)
    {
        foreach (double x in new[] { b.Min.X, b.Max.X }) foreach (double y in new[] { b.Min.Y, b.Max.Y }) foreach (double z in new[] { b.Min.Z, b.Max.Z })
            yield return b.Transform.OfPoint(new XYZ(x, y, z));
    }
    private static string Face(Document doc, BoundarySegment segment, Line edge)
    {
        if (doc.GetElement(segment.ElementId) is not Wall wall || wall.WallType.Kind != WallKind.Basic || Math.Abs(wall.Orientation.Z) > Tolerance)
            throw new OperationException("unsupported_reference", "Room boundaries must be basic native walls with vertical planar finish faces; linked walls and separators are not supported.");
        XYZ midpoint = edge.Evaluate(0.5, true), direction = edge.Direction;
        var references = new[] { ShellLayerType.Interior, ShellLayerType.Exterior }.SelectMany(side => HostObjectUtils.GetSideFaces(wall, side));
        foreach (var reference in references)
            if (wall.GetGeometryObjectFromReference(reference) is PlanarFace face && Math.Abs(face.FaceNormal.Z) < Tolerance
                && Math.Abs(face.FaceNormal.DotProduct(direction)) < Tolerance && Math.Abs((midpoint - face.Origin).DotProduct(face.FaceNormal)) < Tolerance)
                return reference.ConvertToStableRepresentation(doc);
        throw new OperationException("unsupported_reference", "A stable dimension reference could not be found on a room finish face.");
    }
    public static void Create(Document doc, RoomDimensionPreview preview, List<long> created)
    {
        foreach (var plan in preview.Lines)
        {
            var references = new ReferenceArray(); references.Append(Reference.ParseFromStableRepresentation(doc, plan.FirstReference));
            references.Append(Reference.ParseFromStableRepresentation(doc, plan.SecondReference));
            var dimension = doc.Create.NewDimension(doc.ActiveView, Line.CreateBound(ToXyz(plan.Start), ToXyz(plan.End)), references)
                ?? throw new OperationException("dimension_failed", "Revit could not create a face dimension.");
            created.Add(dimension.Id.Value);
        }
    }
    public static bool Verify(Document doc, long[] ids, RoomDimensionPreview preview) => ids.Length == 2 && ids.Distinct().Count() == 2
        && ids.Select((id, i) => doc.GetElement(new ElementId(id)) is Dimension d && d.OwnerViewId == doc.ActiveView.Id && !d.IsLocked
            && d.Value is { } value && Math.Abs(value - preview.Lines[i].LengthFeet) < Tolerance && Matches(doc, d, preview.Lines[i])).All(x => x);
    private static bool Matches(Document doc, Dimension d, DimensionLine l) => d.References.Size == 2
        && d.References.Cast<Reference>().Select(r => r.ConvertToStableRepresentation(doc)).Order().SequenceEqual(new[] { l.FirstReference, l.SecondReference }.Order());
    private static XYZ ToXyz(Point3 p) => new(p.X, p.Y, p.Z);
    private static Point3 ToPoint(XYZ p) => new(p.X, p.Y, p.Z);
}
