using Autodesk.Revit.DB;
namespace RevitThyme.RevitAdapter;
internal record GeometryResult(double[][][] Triangles, double[] Bounds, bool Partial, string[] Diagnostics);
internal static class Geometry
{
    public static GeometryResult Capture(Document doc)
    {
        var triangles = new List<double[][]>(); var reasons = new HashSet<string>();
        var candidates = new FilteredElementCollector(doc).WhereElementIsNotElementType()
            .Where(e => e.Category?.CategoryType == CategoryType.Model && e is not RevitLinkInstance).Take(1501).ToArray();
        if (candidates.Length > 1500) reasons.Add("Candidate limit: elements after the first 1,500 omitted.");
        using var options = new Options { IncludeNonVisibleObjects = true, DetailLevel = ViewDetailLevel.Fine };
        void Add(Mesh mesh)
        {
            for (int i = 0; i < mesh.NumTriangles; i++)
            {
                if (triangles.Count >= 50_000) { reasons.Add("Triangle limit: remaining geometry omitted."); return; }
                var tri = mesh.get_Triangle(i);
                var vertices = Enumerable.Range(0, 3).Select(j => { var p = tri.get_Vertex(j); return new[] { p.X, p.Y, p.Z }; }).ToArray();
                if (vertices.SelectMany(p => p).Any(n => !double.IsFinite(n) || Math.Abs(n) > 1_000_000))
                { reasons.Add("Unsupported coordinate outside finite ±1,000,000 ft bounds."); continue; }
                triangles.Add(vertices);
            }
        }
        void Walk(GeometryElement geometry, int depth)
        {
            if (depth > 16) { reasons.Add("Nested instance depth limit."); return; }
            foreach (var item in geometry)
            {
                if (triangles.Count >= 50_000) { reasons.Add("Triangle limit: remaining geometry omitted."); return; }
                switch (item)
                {
                    // GetInstanceGeometry applies the native instance transform into model coordinates once.
                    case GeometryInstance instance: using (var nested = instance.GetInstanceGeometry()) Walk(nested, depth + 1); break;
                    case Solid solid: foreach (Face face in solid.Faces) using (var mesh = face.Triangulate()) Add(mesh); break;
                    case Mesh mesh: Add(mesh); break;
                    default: reasons.Add("Non-triangle model geometry omitted (curves/points or unsupported representation)."); break;
                }
            }
        }
        foreach (var element in candidates.Take(1500))
        {
            if (triangles.Count >= 50_000) break;
            try { using var geometry = element.get_Geometry(options); if (geometry is not null) Walk(geometry, 0); }
            catch { reasons.Add("An element's geometry could not be captured."); }
        }
        var all = triangles.SelectMany(t => t).ToArray();
        var bounds = all.Length == 0 ? new double[] { -1, -1, -1, 1, 1, 1 }
            : Enumerable.Range(0, 3).Select(i => all.Min(p => p[i])).Concat(Enumerable.Range(0, 3).Select(i => all.Max(p => p[i]))).ToArray();
        for (int i = 0; i < 3; i++) if (bounds[i + 3] - bounds[i] < 0.001) bounds[i + 3] = bounds[i] + 0.001;
        return new(triangles.ToArray(), bounds, reasons.Count > 0,
            ["Local-document model geometry in project coordinates/internal feet; may include hidden, phase and design-option geometry.",
             "Links, annotation, crop, plan regions, projected underlay visibility and final plan visibility are excluded.", .. reasons.Order()]);
    }
}
