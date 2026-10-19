using System.Globalization;
using System.Security;
using System.Text;

namespace RevitThyme.Core;

internal static class TimberGuideSvg
{
    internal static byte[] Write(TimberGuideInventory inventory, TimberFoldProfile profile, CancellationToken ct)
    {
        var content = new StringBuilder();
        double y = 36;
        Text(24, y, "TimberFold — stock and assembly reference", 25); y += 32;
        Text(24, y, "Physical checks outstanding — not manufacturing ready", 17, "#9a3412"); y += 27;
        Text(24, y, "Nominal contours; apply kerf compensation in laser software. No machine settings or kerf offsets supplied."); y += 23;
        Text(24, y, FormattableString.Invariant($"Scale 1:{profile.ScaleDenominator:G8} | material thickness {profile.ThicknessInches:G8} in | stock {profile.StockWidthInches:G8} × {profile.StockHeightInches:G8} in")); y += 23;
        Text(24, y, "Cut calibration pieces and measure thickness, kerf, retained bars and joint fit before cutting a complete kit."); y += 40;
        foreach (var vector in inventory.Vectors)
        {
            ct.ThrowIfCancellationRequested();
            Text(24, y, vector.Drawing.Title, 21); y += 24;
            Text(24, y, vector.Drawing.Role == "Stock"
                ? "Outer border = stock; inner border = usable area. IDs and borders belong to REFERENCE, never CUT."
                : "Left: assembled. Right: exploded along actual outward normals. All outlines visible; no hidden-line removal."); y += 27;
            var points = vector.GeometryContours.SelectMany(r => r).ToArray();
            double minX = points.Min(p => p.X), maxX = points.Max(p => p.X), minY = points.Min(p => p.Y), maxY = points.Max(p => p.Y);
            double scale = Math.Min(740 / Math.Max(0.001, maxX - minX), 440 / Math.Max(0.001, maxY - minY));
            double height = Math.Max(100, (maxY - minY) * scale);
            double top = y;
            foreach (var ring in vector.GeometryContours)
            {
                string coordinates = string.Join(" ", ring.Select(p => F(24 + (p.X - minX) * scale) + "," + F(top + (maxY - p.Y) * scale)));
                content.Append("<polygon points=\"").Append(coordinates).Append("\" fill=\"none\" stroke=\"#334155\" stroke-width=\"1\"/>");
            }
            // Screen-space callouts remain legible for tiny parts and overlapping
            // axon projections; the actual source mapping follows directly below.
            double next = top + 15;
            foreach (var label in vector.IdentificationAnchors
                .OrderByDescending(l => l.PhysicalPositionInches.Y).ThenBy(l => l.Text, StringComparer.Ordinal))
            {
                double px = 24 + (label.PhysicalPositionInches.X - minX) * scale;
                double py = top + (maxY - label.PhysicalPositionInches.Y) * scale;
                double ly = Math.Max(next, py); next = ly + 22;
                content.Append("<path d=\"M ").Append(F(px)).Append(' ').Append(F(py)).Append(" L 795 ").Append(F(ly - 4))
                    .Append("\" fill=\"none\" stroke=\"#94a3b8\" stroke-width=\"1\"/>");
                Text(804, ly, label.Text, 16, "#0f172a");
            }
            y = Math.Max(top + height, next) + 28;
            if (vector.Drawing.Role == "Assembly")
            {
                Text(24, y, "Orientation: +X lower-right; +Y lower-left; +Z up. Material depth points inward (-outward normal)."); y += 23;
                Text(24, y, "Crossings in this projection do not create joints. Exploded offsets are for identification only."); y += 23;
            }
            foreach (var part in inventory.Parts.Where(p => vector.Drawing.PanelIds.Contains(p.Panel.Id)))
            {
                Text(24, y, part.Alias + " — " + part.Panel.Kind + FormattableString.Invariant($" | stock {part.Placement.Sheet} | area {part.Panel.AreaSquareInches:G8} in²"), 15); y += 21;
                foreach (string line in Wrap("Panel: " + part.Panel.Id, 110).Concat(Wrap("Source: " + part.Panel.SourceId + " | Face: " + part.Panel.FaceId, 110)))
                { Text(44, y, line, 13); y += 19; }
                y += 8;
            }
            y += 22;
        }
        Text(24, y, "Recorded joint steps", 21); y += 30;
        Text(24, y, "List order identifies fitted ownership; the physical assembly sequence remains untested."); y += 26;
        int step = 1;
        foreach (var joint in inventory.Joints.OrderBy(j => j.OwnerPanelId, StringComparer.Ordinal).ThenBy(j => j.ReceiverPanelId, StringComparer.Ordinal))
        {
            var owner = inventory.Parts.Single(p => p.Panel.Id == joint.OwnerPanelId);
            var receiver = inventory.Parts.Single(p => p.Panel.Id == joint.ReceiverPanelId);
            Text(24, y, $"{step++}. {owner.Alias} owns contact; {receiver.Alias} receives the fitted trim.", 16); y += 23;
            foreach (string line in Wrap($"Owner source: {owner.Panel.SourceId} | Receiver source: {receiver.Panel.SourceId}", 110))
            { Text(44, y, line, 13); y += 19; }
            Text(44, y, FormattableString.Invariant($"Recorded removed area {joint.RemovedAreaSquareInches:G8} in²; requested joint clearance {profile.JointClearanceInches:G8} in."), 13); y += 30;
        }
        if (inventory.Joints.IsEmpty) { Text(24, y, "No fitted joints recorded; no assembly connection is implied."); y += 26; }
        Text(24, y, "Dry-fit calibration and representative joints before full assembly. Physical checks remain outstanding.", 15, "#9a3412"); y += 36;
        string svg = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"1000\" height=\"" + F(y) + "\" viewBox=\"0 0 1000 " + F(y) + "\"><title>TimberFold reference guide</title>" +
            "<desc>Stock identification, source-to-part mapping, assembled and exploded projections, and recorded joint ownership. Physical fabrication untested.</desc>" +
            "<rect width=\"100%\" height=\"100%\" fill=\"white\"/>" + content + "</svg>";
        return Encoding.UTF8.GetBytes(svg);

        void Text(double x, double baseline, string text, int size = 14, string color = "#334155") =>
            content.Append("<text x=\"").Append(F(x)).Append("\" y=\"").Append(F(baseline)).Append("\" font-family=\"Arial, sans-serif\" font-size=\"")
                .Append(size.ToString(CultureInfo.InvariantCulture)).Append("\" fill=\"").Append(color).Append("\">").Append(SecurityElement.Escape(text)).Append("</text>");
    }
    private static string F(double value) => value.ToString("G17", CultureInfo.InvariantCulture);
    private static IEnumerable<string> Wrap(string text, int width)
    { for (int i = 0; i < text.Length; i += width) yield return text.Substring(i, Math.Min(width, text.Length - i)); }
}
