using RevitThyme.Core;
namespace RevitThyme.Unit;
internal static class TimberFoldInspection
{
    public static void Run(Action<string, bool> check)
    {
        var profile = new TimberFoldProfile();
        check("nominal TimberFold profile is digitally valid and explicitly unmeasured", profile.Validate().Length == 0 && !profile.ThicknessMeasured && !profile.StockMeasured);
        check("invalid stock and nonfinite tolerances block digital inspection", (profile with { StockWidthInches = 0, ChordToleranceInches = double.NaN }).Validate().Count(d => d.Severity == TimberSeverity.Error) == 2);
        var snapshot = Wall();
        Reconciliation(check, snapshot);
        Scope(check, snapshot);
        Openings(check, snapshot);
        NativeOpenings(check);
        Bounded(check, snapshot);
        var inspected = Inspect(snapshot);
        check("supported captured wall is ready for preview but physical checks remain outstanding", inspected.ReadyForPreview && inspected.PhysicalChecksOutstanding && inspected.SourceCount == 1 && inspected.FaceCount == 1 && inspected.Categories.Single(c => c.Category == TimberCategory.Wall).Included == 1);
        var unsupported = snapshot with { Sources = [snapshot.Sources[0] with { Disposition = TimberDisposition.Unsupported, Reason = "Slanted wall" }] };
        check("unsupported selected source blocks readiness with its source ID", !Inspect(unsupported).ReadyForPreview && Inspect(unsupported).Diagnostics.Any(d => d.SourceId == "1" && d.Code == "unsupported_source"));
    }
    private static void Bounded(Action<string, bool> check, TimberSnapshot snapshot)
    {
        var many = snapshot with
        {
            Sources = [.. snapshot.Sources, .. Enumerable.Range(2, 500).Select(i => new TimberSource(i.ToString(), "unique-" + i, TimberCategory.Furniture, TimberDisposition.Excluded, new string('<', 10000), [], []))],
            Diagnostics = Enumerable.Range(0, 500).Select(i => new TimberDiagnostic("diagnostic", TimberSeverity.Warning, new string('<', 10000), "source", "face")).ToArray()
        };
        var summary = Inspect(many);
        check("full inspection evidence retains every diagnostic without IPC truncation", TimberInspection.GetDiagnostics(many, new(), new("phase", [])).Length == 500 && TimberInspection.GetDiagnostics(many, new(), new("phase", []))[0].Message.Length == 10000);
        var options = Enumerable.Range(1, 20).Select(i => "option-" + i).ToArray();
        var optionSummary = TimberInspection.Inspect(snapshot with { DesignOptionIds = options }, new(), new("phase", options));
        check("many compatible options retain readiness and mark summary sampling", optionSummary.ReadyForPreview && optionSummary.DesignOptionsTruncated);
        check("IPC summary bounds source reasons and diagnostic strings while retaining totals", summary.SourceCount == 501 && summary.WarningCount == 500 && summary.DiagnosticsTruncated && summary.SourcesTruncated && System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(summary).Length < 60000);
    }
    private static void NativeOpenings(Action<string, bool> check)
    {
        TimberPoint2[][] aperture = [[new(2, 0), new(5, 0), new(5, 7), new(2, 7)]];
        var wall = Wall() with
        {
            Sources = [new("1", "unique-1", TimberCategory.Wall, TimberDisposition.Included, "Exterior basic wall", ["1:face"], ["2:void"], "wall-sha"),
                new("2", "opening-wall", TimberCategory.Opening, TimberDisposition.Included, "Native wall opening", [], ["2:void"], "wall-opening-sha")],
            Openings = [new("2:void", "2", "1", "1:face", TimberOpeningKind.Door, aperture, [], true)]
        };
        check("native wall opening is accounted without becoming an unsupported source", Inspect(wall).ReadyForPreview && Inspect(wall).Categories.Single(c => c.Category == TimberCategory.Opening).Included == 1);
        var roof = wall with
        {
            Sources = [new("3", "unique-roof", TimberCategory.Roof, TimberDisposition.Included, "Flat roof", ["3:face"], ["4:void"], "roof-sha"),
                new("4", "opening-roof", TimberCategory.Opening, TimberDisposition.Included, "Native roof opening", [], ["4:void"], "roof-opening-sha")],
            Faces = [new("3:face", "3", TimberFaceKind.Roof, new(0, 0, 10), new(1, 0, 0), new(0, 1, 0), new(0, 0, 1), [[new(0, 0), new(20, 0), new(20, 16), new(0, 16)]])],
            Openings = [new("4:void", "4", "3", "3:face", TimberOpeningKind.Roof, [[new(2, 2), new(5, 2), new(5, 6), new(2, 6)]], [], true)]
        };
        check("native roof opening is accounted without requiring glazing or source faces", Inspect(roof).ReadyForPreview && Inspect(roof).OpeningCount == 1);
        var missing = wall with { Sources = [wall.Sources[0], wall.Sources[1] with { OpeningIds = [] }] };
        check("native opening with no owned void reference blocks readiness", !Inspect(missing).ReadyForPreview && Inspect(missing).Diagnostics.Any(d => d.Code == "missing_opening" && d.SourceId == "2"));
    }
    private static void Openings(Action<string, bool> check, TimberSnapshot wall)
    {
        TimberPoint2[][] ring = [[new(2, 2), new(4, 2), new(4, 6), new(2, 6)]];
        var window = wall with
        {
            Sources = [.. wall.Sources, new("2", "unique-2", TimberCategory.Window, TimberDisposition.Included, "Hosted window", [], ["2:opening"], "window-sha")],
            Openings = [new("2:opening", "2", "1", "1:face", TimberOpeningKind.Window, ring, ring, true)]
        };
        check("resolved glazing pane remains inspectable", Inspect(window).ReadyForPreview);
        var hostReferencesOpening = window with { Sources = [window.Sources[0] with { OpeningIds = ["2:opening"] }, window.Sources[1]] };
        check("native host and hosted source can both account for the same opening", Inspect(hostReferencesOpening).ReadyForPreview);
        var unresolved = window with { Openings = [window.Openings[0] with { GlazingResolved = false }] };
        check("ambiguous glazing blocks source window", !Inspect(unresolved).ReadyForPreview && Inspect(unresolved).Diagnostics.Any(d => d.Code == "unresolved_glazing" && d.SourceId == "2"));
        var noPanes = window with { Openings = [window.Openings[0] with { PaneRingsFeet = [] }] };
        check("empty resolved glazing cannot silently pass", !Inspect(noPanes).ReadyForPreview);
    }
    private static void Scope(Action<string, bool> check, TimberSnapshot snapshot)
    {
        check("phase disagreement blocks captured source scope", !TimberInspection.Inspect(snapshot, new(), new("different-phase", [])).ReadyForPreview);
        check("design option disagreement blocks captured source scope", !TimberInspection.Inspect(snapshot, new(), new("phase", ["option"])).ReadyForPreview);
        check("requested source absent from inventory blocks completeness", !TimberInspection.Inspect(snapshot, new(), new("phase", [], ["999"])).ReadyForPreview);
        check("explicit exclusion cannot remain included", !TimberInspection.Inspect(snapshot, new(), new("phase", [], ExcludeIds: ["1"])).ReadyForPreview);
    }
    private static void Reconciliation(Action<string, bool> check, TimberSnapshot snapshot)
    {
        var missingFace = snapshot with { Faces = [] };
        check("missing extracted face blocks readiness instead of silently omitting a wall", !Inspect(missingFace).ReadyForPreview && Inspect(missingFace).Diagnostics.Any(d => d.Code == "missing_face"));
        var duplicate = snapshot with { Sources = [.. snapshot.Sources, new("2", "unique-1", TimberCategory.Furniture, TimberDisposition.Excluded, "Outside shell scope", [], [])] };
        check("duplicate native unique IDs block source identity accounting", !Inspect(duplicate).ReadyForPreview && Inspect(duplicate).Diagnostics.Any(d => d.Code == "duplicate_source_identity"));
        var orphan = snapshot with { Sources = [] };
        check("geometry without an inventoried source blocks readiness", !Inspect(orphan).ReadyForPreview);
    }
    private static TimberInspectionSummary Inspect(TimberSnapshot snapshot) => TimberInspection.Inspect(snapshot, new(), new("phase", []));
    private static TimberSnapshot Wall() => new(new(1, "start", "session", "doc", "view", 0), "document-sha", "phase", [],
        [new("1", "unique-1", TimberCategory.Wall, TimberDisposition.Included, "Exterior basic wall", ["1:face"], [], "source-sha")],
        [new("1:face", "1", TimberFaceKind.Wall, new(0, 0, 0), new(1, 0, 0), new(0, 0, 1), new(0, -1, 0), [[new(0, 0), new(20, 0), new(20, 10), new(0, 10)]])], [], []);
}
