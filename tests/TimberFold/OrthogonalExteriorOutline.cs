using Autodesk.Revit.DB;

namespace RevitThyme.TimberFold.Fixtures;

internal static class OrthogonalExteriorOutline
{
    // Input is a counterclockwise exterior-face boundary. At each orthogonal
    // corner, the sum of adjacent inward normals intersects their offset lines.
    public static XYZ[] Inset((double X, double Y)[] outline, double distance, double elevation)
    {
        double twiceArea = Enumerable.Range(0, outline.Length).Sum(i =>
            outline[i].X * outline[(i + 1) % outline.Length].Y - outline[(i + 1) % outline.Length].X * outline[i].Y);
        if (twiceArea <= 0 || distance <= 0) throw new InvalidDataException("Expected a counterclockwise exterior outline and positive wall inset.");
        return Enumerable.Range(0, outline.Length).Select(i =>
        {
            var previous = outline[(i + outline.Length - 1) % outline.Length]; var vertex = outline[i];
            var next = outline[(i + 1) % outline.Length];
            var incoming = new XYZ(vertex.X - previous.X, vertex.Y - previous.Y, 0).Normalize();
            var outgoing = new XYZ(next.X - vertex.X, next.Y - vertex.Y, 0).Normalize();
            if (Math.Abs(incoming.DotProduct(outgoing)) > 1e-8 ||
                Math.Min(Math.Abs(incoming.X), Math.Abs(incoming.Y)) > 1e-8 || Math.Min(Math.Abs(outgoing.X), Math.Abs(outgoing.Y)) > 1e-8)
                throw new InvalidDataException("Original wall recipes require orthogonal axis-aligned corners.");
            return new XYZ(vertex.X, vertex.Y, elevation) + XYZ.BasisZ.CrossProduct(incoming + outgoing) * distance;
        }).ToArray();
    }
}
