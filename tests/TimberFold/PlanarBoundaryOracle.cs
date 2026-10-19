using Autodesk.Revit.DB;

namespace RevitThyme.TimberFold.Fixtures;

/// <summary>Independent exact line/circular-arc area integral; no product geometry code.</summary>
internal static class PlanarBoundaryOracle
{
    public static double? Area(Face face)
    {
        if (face is not PlanarFace plane) return null;
        double signedArea = 0;
        foreach (EdgeArray edges in face.EdgeLoops)
        {
            var curves = edges.Cast<Edge>().Select(edge => edge.AsCurveFollowingFace(face)).ToArray();
            for (int i = 0; i < curves.Length; i++)
            {
                var curve = curves[i];
                if (End(curve, 1).DistanceTo(End(curves[(i + 1) % curves.Length], 0)) > 1e-7)
                    throw new InvalidDataException("Native face boundary is not a connected oriented loop.");
                if (curve is Line)
                    signedArea += (End(curve, 0) - plane.Origin).CrossProduct(End(curve, 1) - plane.Origin).DotProduct(plane.FaceNormal) / 2;
                else if (curve is Arc arc)
                {
                    // Green's theorem: 1/2 integral (r cross dr) dot normal.
                    // For a circular arc this is its centre/chord term plus r²*sweep.
                    double sweep = arc.IsBound ? arc.GetEndParameter(1) - arc.GetEndParameter(0) : 2 * Math.PI;
                    var vectorArea = (arc.Center - plane.Origin).CrossProduct(End(arc, 1) - End(arc, 0))
                        + arc.XDirection.CrossProduct(arc.YDirection) * (arc.Radius * arc.Radius * sweep);
                    signedArea += vectorArea.DotProduct(plane.FaceNormal) / 2;
                }
                else throw new InvalidDataException("Original planar fixture area oracle accepts only native lines and circular arcs.");
            }
        }
        return Math.Abs(signedArea);
    }

    private static XYZ End(Curve curve, int index) => curve is Arc { IsBound: false } arc
        ? arc.Center + arc.XDirection * arc.Radius : curve.GetEndPoint(index);
}
