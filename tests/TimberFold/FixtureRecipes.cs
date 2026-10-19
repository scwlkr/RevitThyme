using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace RevitThyme.TimberFold.Fixtures;

public static partial class OriginalFixtureBuilder
{
    private static void BuildGable(Context c, Level eave)
    {
        var walls = c.Shell([(0, 0), (24, 0), (24, 16), (0, 16)], c.Ground, 14, "ground");
        var roof = c.Roof([(0, 0), (24, 0), (24, 16), (0, 16)], eave, [0, 2], "main-gable");
        c.Fit(walls, roof);
        c.Window(walls[0], c.Ground, new(6, 0, 3), "front-left");
        c.Window(walls[0], c.Ground, new(18, 0, 3), "front-right");
        c.Door(walls[0], new(10.5, 0, 0), new(13.5, 0, 7));
    }

    private static void BuildIrregular(Context c, Level eave)
    {
        var walls = c.Shell([(0, 0), (24, 0), (24, 28), (12, 28), (12, 16), (0, 16)], c.Ground, 15, "L-shell");
        var main = c.Roof([(0, 0), (24, 0), (24, 16), (0, 16)], eave, [0, 2], "main-east-west-gable");
        var wing = c.Roof([(12, 8), (24, 8), (24, 28), (12, 28)], eave, [1, 3], "intersecting-north-south-gable");
        // Roof overlap is deliberate: exercise explicit clipping/intersection accounting rather than conceal it with a bespoke mesh.
        c.Fit([walls[0], walls[4], walls[5]], main);
        c.Fit([walls[2], walls[3]], wing);
        // One wall spans both roofs. Revit allows only one top-attachment target,
        // so keep an explicit editable stepped/gabled profile at this boundary.
        // The operation must reconcile its top against both actual undersides.
        var spanningLine = ((LocationCurve)walls[1].Location).Curve;
        var south = spanningLine.GetEndPoint(0); var north = spanningLine.GetEndPoint(1);
        c.Document.Delete(walls[1].Id);
        walls[1] = Wall.Create(c.Document, Polygon([new(south.X, south.Y, 0), new(north.X, north.Y, 0), new(north.X, north.Y, 10),
            new(south.X, 16, 10), new(south.X, 8, 14), new(south.X, south.Y, 10 + south.Y * 0.5)]).Cast<Curve>().ToArray(),
            c.WallType.Id, c.Ground.Id, false, XYZ.BasisX);
        c.Document.Regenerate();
        if (walls[1].Orientation.DotProduct(XYZ.BasisX) < 0) walls[1].Flip();
        c.Document.Regenerate();
        Tag(walls[1], "L-shell-wall-1-profile-spanning-two-roofs");
        c.Window(walls[0], c.Ground, new(6, 0, 3), "front-left");
        c.Window(walls[0], c.Ground, new(18, 0, 3), "front-right");
        c.Window(walls[2], c.Ground, new(18, 28, 3), "wing-end");
        c.Door(walls[0], new(10.5, 0, 0), new(13.5, 0, 7));
    }

    private static void BuildMultistorey(Context c, Level first, Level upper)
    {
        var outline = new (double, double)[] { (0, 0), (24, 0), (24, 16), (0, 16) };
        var lower = c.Shell(outline, c.Ground, 10, "ground");
        var higher = c.Shell(outline, first, 12, "first");
        var roof = c.Roof(outline, upper, [0, 2], "upper-gable");
        c.Fit(higher, roof);
        c.Window(lower[0], c.Ground, new(6, 0, 3), "ground-aligned");
        c.Window(lower[0], c.Ground, new(18, 0, 3), "ground-offset");
        c.Window(higher[0], first, new(6, 0, 13), "first-aligned");
        c.Window(higher[0], first, new(17, 0, 13), "first-offset");
        c.Door(lower[0], new(10.5, 0, 0), new(13.5, 0, 7));
    }

    private static void BuildFlat(Context c, Level terrace, Level upper)
    {
        var outer = new (double, double)[] { (0, 0), (28, 0), (28, 20), (0, 20) };
        var setback = new (double, double)[] { (8, 5), (20, 5), (20, 15), (8, 15) };
        var lower = c.Shell(outer, c.Ground, 10, "ground");
        var higher = c.Shell(setback, terrace, 8, "setback");
        var lowerRoof = c.Roof(outer, terrace, [], "terrace-flat");
        c.RoofOpening(lowerRoof, setback, terrace.Elevation, "setback-void");
        var upperRoof = c.Roof(setback, upper, [], "upper-flat");
        c.RoofOpening(upperRoof, [(12, 8), (16, 8), (16, 12), (12, 12)], upper.Elevation, "skylight-void");
        c.Fit(lower, lowerRoof); c.Fit(higher, upperRoof);
        c.Shell(outer, terrace, 2, "terrace-parapet");
        c.Shell(setback, upper, 2, "upper-parapet");
        c.Window(lower[0], c.Ground, new(5, 0, 3), "ground-left");
        c.Window(lower[0], c.Ground, new(23, 0, 3), "ground-right");
        c.Window(higher[0], terrace, new(11, 5, 12), "upper-left");
        c.Window(higher[0], terrace, new(17, 5, 12), "upper-right");
        c.Door(lower[0], new(12.5, 0, 0), new(15.5, 0, 7));
    }

    private static void BuildSmall(Context c, string fixture, Level eave)
    {
        if (fixture == "arc-roof")
        {
            var boundary = new CurveArray();
            boundary.Append(Arc.Create(new XYZ(0, 0, 10), 8, 0, Math.PI, XYZ.BasisX, XYZ.BasisY));
            boundary.Append(Arc.Create(new XYZ(0, 0, 10), 8, Math.PI, Math.PI * 2, XYZ.BasisX, XYZ.BasisY));
            var edges = new ModelCurveArray();
            var roof = c.Document.Create.NewFootPrintRoof(boundary, eave, c.RoofType, out edges);
            foreach (ModelCurve edge in edges) roof.set_DefinesSlope(edge, false);
            Tag(roof, "arc-roof-radius-8ft");
            return;
        }
        int count = fixture == "performance-100" ? 100 : fixture == "phase-scope" ? 2 : 1;
        for (int i = 0; i < count; i++)
        {
            c.Cancellation.ThrowIfCancellationRequested();
            Curve line = fixture == "curved-wall"
                ? Arc.Create(new XYZ(0, 0, 0), 10, 0, Math.PI / 2, XYZ.BasisX, XYZ.BasisY)
                : Line.CreateBound(new XYZ(0, i * 4, 0), new XYZ(fixture == "oversized-wall" ? 120 : 12, i * 4, 0));
            var wall = Wall.Create(c.Document, line, c.WallType.Id, c.Ground.Id, 10, 0, true, false);
            Tag(wall, fixture + "-" + i);
            if (fixture == "slanted-wall")
            {
                wall.get_Parameter(BuiltInParameter.WALL_CROSS_SECTION).Set((int)WallCrossSection.SingleSlanted);
                wall.get_Parameter(BuiltInParameter.WALL_SINGLE_SLANT_ANGLE_FROM_VERTICAL).Set(Math.PI / 12);
            }
            if (fixture is "ambiguous-glazing" or "thin-bar") c.Window(wall, c.Ground, new XYZ(6, 0, 3), fixture);
            if (fixture == "phase-scope")
            {
                var phases = c.Document.Phases.Cast<Phase>().ToArray();
                if (phases.Length < 2) throw new InvalidDataException("Phase fixture requires two built-in project phases.");
                wall.CreatedPhaseId = i == 0 ? phases[0].Id : phases[^1].Id;
                if (i == 0) wall.DemolishedPhaseId = phases[^1].Id;
            }
        }
    }

    private static string Description(string fixture) => fixture switch
    {
        "gable" => "24 x 16 ft shell; eave 10 ft; 6:12 gable ridge 14 ft; 4 walls fitted to native roof; two 4 x 4 ft double-pane windows, 3 x 7 ft door void.",
        "irregular" => "L outline (0,0),(24,0),(24,28),(12,28),(12,16),(0,16) ft; main 24 x 16 ft east-west gable and 12 x 20 ft north-south wing gable overlap on 12 x 8 ft. Eave 10 ft, slopes 6:12; roof source faces must be reconciled at intersection. Three two-pane windows, door void.",
        "multistorey" => "24 x 16 ft; storeys at 0 and 10 ft; upper eave 18 ft, ridge 22 ft. Eight stacked walls with 10/8/12 ft profiles; four windows aligned at x=6 and offset x=18/17 ft, 3 x 7 ft ground door void.",
        "flat" => "28 x 20 ft lower shell, terrace roof at 10 ft with 12 x 10 ft setback void; upper shell (8,5)-(20,15) ft, flat roof at 18 ft with 4 x 4 ft skylight void. Two-ft parapets on both levels. Four two-pane windows and door void.",
        "curved-wall" => "One 10 ft radius quarter-circle basic wall, 10 ft height. Must block curved wall support.",
        "slanted-wall" => "One 12 x 10 ft basic wall, 15 degrees from vertical. Must block slanted wall support.",
        "oversized-wall" => "One 120 x 10 ft wall; 30 x 2.5 physical inches at 1:48 exceeds 23.5 x 17.5 in printable area. Must block without omission or rescaling.",
        "ambiguous-glazing" => "One hosted two-panel window whose pane material is opaque and not Glass. Must block ambiguous/missing glazing.",
        "thin-bar" => "One two-pane window with 1.5 in model centre bar, 0.03125 physical in at 1:48, below the accepted 0.0625 in bar gate.",
        "arc-roof" => "One circular planar flat roof, radius 8 ft, two half-circle source arcs. Native oracle: area 64*pi square ft before thickness effects; exported chord gate <=0.001 physical in.",
        "phase-scope" => "Two 12 x 10 ft walls: one created in earliest phase and demolished in latest; one created in latest phase. Requires explicit chosen-phase inventory.",
        "performance-100" => "100 isolated 12 x 10 ft walls at 4 ft spacing. 100 analytic shell panels; timings exclude Revit startup.",
        _ => throw new ArgumentOutOfRangeException(nameof(fixture))
    };

    private sealed class Context(Document document, Level ground, WallType wallType, RoofType roofType, FamilySymbol? window, CancellationToken cancellation)
    {
        public Document Document { get; } = document;
        public Level Ground { get; } = ground;
        public WallType WallType { get; } = wallType;
        public RoofType RoofType { get; } = roofType;
        public CancellationToken Cancellation { get; } = cancellation;
        public Wall[] Shell((double X, double Y)[] outline, Level level, double height, string role)
        {
            var centreline = OrthogonalExteriorOutline.Inset(outline, WallType.Width / 2, level.Elevation);
            var walls = Enumerable.Range(0, outline.Length).Select(i =>
            {
                Cancellation.ThrowIfCancellationRequested();
                var wall = Wall.Create(Document, Line.CreateBound(centreline[i], centreline[(i + 1) % outline.Length]),
                    WallType.Id, level.Id, height, 0, true, false);
                Tag(wall, role + "-wall-" + i); return wall;
            }).ToArray();
            // Native Orientation reads the wall face; complete geometry creation first.
            Document.Regenerate();
            for (int i = 0; i < walls.Length; i++)
            {
                var a = outline[i]; var b = outline[(i + 1) % outline.Length];
                var outward = new XYZ(b.Y - a.Y, a.X - b.X, 0).Normalize();
                if (walls[i].Orientation.DotProduct(outward) < 0) walls[i].Flip();
            }
            Document.Regenerate();
            return walls;
        }
        public FootPrintRoof Roof((double X, double Y)[] outline, Level level, int[] slopingEdges, string role)
        {
            // Revit requires an initialized native mapping array even for this out parameter.
            var edges = new ModelCurveArray();
            var roof = Document.Create.NewFootPrintRoof(Polygon(outline.Select(p => new XYZ(p.X, p.Y, level.Elevation)).ToArray()), level, RoofType, out edges);
            var mappedSegments = new HashSet<int>();
            foreach (ModelCurve edge in edges)
            {
                int segment = FootprintSegment(edge.GeometryCurve, outline);
                if (!mappedSegments.Add(segment)) throw new InvalidDataException("Native roof mapping duplicated a footprint segment.");
                bool slope = slopingEdges.Contains(segment); roof.set_DefinesSlope(edge, slope);
                // Installed RevitAPI.xml, FootPrintRoof.SlopeAngle: this value is
                // rise/run; 0.5 means 6:12 and produces a 26.57-degree slope.
                if (slope) roof.set_SlopeAngle(edge, 0.5);
            }
            if (mappedSegments.Count != outline.Length) throw new InvalidDataException("Native roof mapping lost a footprint segment.");
            Tag(roof, role); return roof;
        }
        private static int FootprintSegment(Curve curve, (double X, double Y)[] outline)
        {
            if (curve is not Line || !curve.IsBound) throw new InvalidDataException("Expected a native straight footprint model curve.");
            bool Same(XYZ point, (double X, double Y) vertex) => Math.Abs(point.X - vertex.X) < 1e-7 && Math.Abs(point.Y - vertex.Y) < 1e-7;
            var start = curve.GetEndPoint(0); var end = curve.GetEndPoint(1);
            var matches = Enumerable.Range(0, outline.Length).Where(i =>
            {
                var a = outline[i]; var b = outline[(i + 1) % outline.Length];
                return Same(start, a) && Same(end, b) || Same(start, b) && Same(end, a);
            }).ToArray();
            if (matches.Length != 1) throw new InvalidDataException("Native roof model curve does not correspond uniquely to an original footprint segment.");
            return matches[0];
        }
        public void Fit(Wall[] walls, RoofBase roof)
        {
            Document.Regenerate();
            foreach (var wall in walls) wall.AddAttachment(roof.Id, AttachmentLocation.Top);
            Document.Regenerate();
        }
        public void Window(Wall host, Level level, XYZ origin, string role)
        {
            Cancellation.ThrowIfCancellationRequested();
            Document.Regenerate();
            var instance = Document.Create.NewFamilyInstance(OnHost(host, origin), window ?? throw new InvalidOperationException("No original family loaded."), host, level, StructuralType.NonStructural);
            Tag(instance, "two-pane-window-" + role);
        }
        private static XYZ OnHost(Wall host, XYZ point)
        {
            var curve = ((LocationCurve)host.Location).Curve;
            var projected = curve.Project(new XYZ(point.X, point.Y, curve.GetEndPoint(0).Z))
                ?? throw new InvalidDataException("An opening point did not project onto its native host location curve.");
            return new(projected.XYZPoint.X, projected.XYZPoint.Y, point.Z);
        }
        public void Door(Wall host, XYZ min, XYZ max) => Tag(Document.Create.NewOpening(host, OnHost(host, min), OnHost(host, max)), "door-void");
        public void RoofOpening(RoofBase host, (double X, double Y)[] outline, double elevation, string role) =>
            Tag(Document.Create.NewOpening(host, Polygon(outline.Select(p => new XYZ(p.X, p.Y, elevation)).ToArray()), true), role);
    }
}
