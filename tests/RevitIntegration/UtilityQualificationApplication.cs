using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using RevitThyme.Addin;
using RevitThyme.Core;

namespace RevitThyme.RevitIntegration;

public sealed class UtilityQualificationApplication : IExternalApplication, IExternalEventHandler
{
    private string? folder;
    private string fixture = "", hash = "", roomId = "";
    private int stage, count;
    private bool stopped;
    private ExternalEvent? pulseEvent;
    private System.Threading.Timer? pulse;
    private Task<OperationResult>? pending;
    private ViewRangeSettings? original;
    private ViewRangeSettings? originalCeiling;
    private long[] dimensionIds = [];
    private RoomDimensionPreview? roomPlan;
    private readonly List<object> checks = [];
    private DateTime started;
    public Result OnStartup(UIControlledApplication app)
    {
        folder = Environment.GetEnvironmentVariable("REVITTHYME_UTILITIES_TEST_RUN");
        if (folder is null) return Result.Succeeded;
        fixture = Path.Combine(folder, "Foundation-Test.rvt");
        if (!Path.IsPathFullyQualified(folder) || !Guid.TryParse(Path.GetFileName(folder), out _) || File.Exists(fixture)
            || fixture != Environment.GetEnvironmentVariable("REVITTHYME_FOUNDATION_FIXTURE")) return Result.Failed;
        started = DateTime.UtcNow; pulseEvent = ExternalEvent.Create(this); app.Idling += Idle;
        pulse = new(_ => { if (!stopped) { try { pulseEvent.Raise(); } catch (Exception) { } } }, null, 1000, 500); return Result.Succeeded;
    }
    private void Check(string name, bool passed) { checks.Add(new { name, passed }); if (!passed) throw new InvalidOperationException(name); }
    private void Idle(object? sender, IdlingEventArgs e) => Advance((UIApplication)sender!);
    public void Execute(UIApplication app) => Advance(app);
    private OperationResult PendingResult() => pending!.GetAwaiter().GetResult();
    private static double Cut(Document doc) { using var r = ((ViewPlan)doc.ActiveView).GetViewRange(); return r.GetOffset(PlanViewPlane.CutPlane); }
    private static int Dims(Document doc) => new FilteredElementCollector(doc).OfClass(typeof(Dimension)).GetElementCount();
    private void Undo(UIApplication app, bool redo = false) => app.PostCommand(RevitCommandId.LookupPostableCommandId(redo ? PostableCommand.Redo : PostableCommand.Undo));
    private void Button(UIApplication app, string name, string className)
    {
        var panel = app.GetRibbonPanels("RevitThyme").Single(p => p.GetItems().Any(b => b.Name == name));
        var button = panel.GetItems().OfType<PushButton>().Single(b => b.Name == name);
        Check("ribbon command registered: " + name, button.ClassName == className && button.Enabled);
        var id = RevitCommandId.LookupCommandId("CustomCtrl_%CustomCtrl_%RevitThyme%" + panel.Name + "%" + name);
        Check("ribbon command can post: " + name, id is not null && app.CanPostCommand(id)); app.PostCommand(id!);
    }
    private void Advance(UIApplication app)
    {
        if (stopped) return;
        try
        {
            if ((DateTime.UtcNow - started).TotalMinutes > 20) throw new TimeoutException("Utilities native qualification timed out.");
            var host = FoundationApplication.Instance ?? throw new InvalidOperationException("Host missing.");
            var doc = app.ActiveUIDocument?.Document;
            if (pending is not null && !pending.IsCompleted) return;
            switch (stage)
            {
                case 0:
                    Check("empty owned process", app.Application.Documents.Size == 0); UtilityFixture.Create(app, fixture); stage++; break;
                case 1:
                    app.ActiveUIDocument!.RequestViewChange(View(doc!, "Utilities Plan")); stage++; break;
                case 2:
                    if (doc!.ActiveView.Name != "Utilities Plan") return;
                    hash = Hash(); count = new FilteredElementCollector(doc).GetElementCount();
                    roomId = Rooms(doc).Single(r => r.Name.StartsWith("Rectangle", StringComparison.Ordinal)).UniqueId;
                    Check("native populated fixture has tagged room", Rooms(doc).Length == 3 && new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_RoomTags).WhereElementIsNotElementType().GetElementCount() == 1);
                    Button(app, "RevitThyme.Native.ViewRange", typeof(ViewRangeCommand).FullName!); stage++; break;
                case 3:
                    if (host.ViewRangeWindow is not { IsBusy: false, CurrentRange: { } range }) return;
                    original = range.Settings; Check("button WPF range reads four native planes", original.Cut.OffsetFeet == 4 && original.Top.OffsetFeet == 8);
                    Check("native level labels bounded for IPC", range.Levels.All(l => l.Name.Length <= 64) && range.Levels.Any(l => l.Name.EndsWith("…", StringComparison.Ordinal)));
                    Capture(host.ViewRangeWindow, "view-range.png");
                    pending = host.ViewRangeWindow.ApplyAsync(original with { Cut = original.Cut with { OffsetFeet = 5 + 1.0 / 96 } }); stage++; break;
                case 4:
                    Check("button WPF applies exact eighth-inch native range", PendingResult().Outcome == Outcome.Applied && Math.Abs(Cut(doc!) - 5 - 1.0 / 96) < 1e-9);
                    Undo(app); stage++; break;
                case 5:
                    if (Cut(doc!) != 4) return; Check("one native Undo restores range", true); Undo(app, true); stage++; break;
                case 6:
                    if (Math.Abs(Cut(doc!) - 5 - 1.0 / 96) > 1e-9) return;
                    Check("one native Redo reapplies range", true); pending = host.ViewRangeWindow!.RefreshAsync(); stage++; break;
                case 7:
                    pending = host.ViewRangeWindow!.ApplyAsync(original); stage++; break;
                case 8:
                    Check("button restores native range", PendingResult().Outcome == Outcome.Applied && Cut(doc!) == 4); host.ViewRangeWindow!.Close();
                    Button(app, "RevitThyme.Native.RoomDimensions", typeof(RoomDimensionsCommand).FullName!); stage++; break;
                case 9:
                    if (host.RoomDimensionsWindow is not { IsBusy: false, CurrentPreview: { } preview }) return;
                    Check("button WPF previews without dimensions", Dims(doc!) == 0 && preview.RoomId == roomId && preview.Lines.Length == 2);
                    roomPlan = preview;
                    Check("native finish dimensions match room area", Math.Abs(preview.WidthFeet * preview.DepthFeet - ((Room)doc!.GetElement(roomId)).Area) < 1e-4);
                    Capture(host.RoomDimensionsWindow, "room-dimensions.png"); pending = host.RoomDimensionsWindow.CreateAsync(); stage++; break;
                case 10:
                    dimensionIds = PendingResult().ChangedIds!;
                    Check("button WPF creates two native dimensions", PendingResult().Outcome == Outcome.Applied && Dims(doc!) == 2 && NativeValues(doc!, dimensionIds));
                    Association(doc!, roomPlan!, dimensionIds);
                    RoomGuards(app, host);
                    host.RoomDimensionsWindow!.Close(); app.ActiveUIDocument!.RefreshActiveView(); Undo(app); stage++; break;
                case 11:
                    if (Dims(doc!) != 0) return; Check("one Undo removes both dimensions", true); Undo(app, true); stage++; break;
                case 12:
                    if (Dims(doc!) != 2) return; Check("one Redo restores both native dimensions", NativeValues(doc!, dimensionIds)); Undo(app); stage++; break;
                case 13:
                    if (Dims(doc!) != 0) return;
                    var model = new RevitModel(app, host); var executor = new OperationExecutor();
                    var invalid = original! with { Cut = original!.Cut with { OffsetFeet = 50 } };
                    var bad = executor.Execute(model, new(Guid.NewGuid(), OperationKind.SetViewRange, model.Capture().Target, ExpectedRange: original, Range: invalid));
                    Check("invalid plane order rolls back native range", bad.Outcome == Outcome.RolledBack && Cut(doc!) == 4);
                    var lroom = Rooms(doc!).Single(r => r.Name.StartsWith("L Shape", StringComparison.Ordinal));
                    Check("irregular room explicitly rejected", executor.Execute(model, new(Guid.NewGuid(), OperationKind.PreviewRoomDimensions, model.Capture().Target, ElementIds: [lroom.UniqueId])).Code == "unsupported_room");
                    var rotated = Rooms(doc!).Single(r => r.Name.StartsWith("Rotated", StringComparison.Ordinal));
                    var p = model.PlanRoomDimensions(rotated.UniqueId, new());
                    var made = executor.Execute(model, new(Guid.NewGuid(), OperationKind.CreateRoomDimensions, model.Capture().Target, ElementIds: [rotated.UniqueId], RoomPreview: p));
                    Check("rotated rectangular room has associative dimensions", made.Outcome == Outcome.Applied && NativeValues(doc!, made.ChangedIds!));
                    Undo(app); stage++; break;
                case 14:
                    if (Dims(doc!) != 0) return; app.ActiveUIDocument!.RequestViewChange(View(doc!, "Template Controlled")); stage++; break;
                case 15:
                    if (doc!.ActiveView.Name != "Template Controlled") return;
                    var templateModel = new RevitModel(app, host); var templateRange = templateModel.ReadViewRange();
                    Check("template range reports edit control", !templateRange.CanEdit);
                    Check("template-controlled range refuses edit", new OperationExecutor().Execute(templateModel, new(Guid.NewGuid(), OperationKind.SetViewRange, templateModel.Capture().Target,
                        ExpectedRange: templateRange.Settings, Range: templateRange.Settings with { Cut = new("Current", 5) })).Code == "write_blocked");
                    app.ActiveUIDocument!.RequestViewChange(View(doc, "Utilities Ceiling")); stage++; break;
                case 16:
                    if (doc!.ActiveView.Name != "Utilities Ceiling") return;
                    var ceilingModel = new RevitModel(app, host); var ceilingRange = ceilingModel.ReadViewRange();
                    originalCeiling = ceilingRange.Settings;
                    var ceilingEdit = ceilingRange.Settings with { Cut = ceilingRange.Settings.Cut with { OffsetFeet = ceilingRange.Settings.Cut.OffsetFeet - 0.5 },
                        Bottom = ceilingRange.Settings.Bottom with { OffsetFeet = ceilingRange.Settings.Bottom.OffsetFeet - 0.5 } };
                    var ceilingResult = new OperationExecutor().Execute(ceilingModel, new(Guid.NewGuid(), OperationKind.SetViewRange, ceilingModel.Capture().Target, ExpectedRange: ceilingRange.Settings, Range: ceilingEdit));
                    Check("ceiling range uses native validation and readback", ceilingResult.Outcome == Outcome.Applied && ceilingModel.ReadViewRange().Settings == ceilingEdit);
                    Undo(app); stage++; break;
                case 17:
                    if (new RevitModel(app, host).ReadViewRange().Settings != originalCeiling) return;
                    Check("one Undo restores full ceiling range", true);
                    app.ActiveUIDocument!.RequestViewChange(View(doc!, "Utilities Plan")); stage++; break;
                case 18:
                    if (doc!.ActiveView.Name != "Utilities Plan") return;
                    Check("all native test edits restored", Dims(doc) == 0 && Cut(doc) == 4 && new FilteredElementCollector(doc).GetElementCount() == count && Hash() == hash);
                    File.WriteAllText(Path.Combine(folder!, "ready.json"), JsonSerializer.Serialize(new { host.Binding, fixture, roomId, count, savedHash = hash }, Protocol.Json)); stage++; break;
                case 19:
                    if (!File.Exists(Path.Combine(folder!, "utilities-mcp-complete.json"))) return;
                    Check("MCP range restored independently", Cut(doc!) == 4);
                    var nativeDims = new FilteredElementCollector(doc!).OfClass(typeof(Dimension)).Cast<Dimension>().ToArray();
                    Check("MCP created real native associative face pair", nativeDims.Length == 2 && nativeDims.All(d => d.Value > 0 && d.References.Size == 2 && !d.IsLocked));
                    var thickness = new FilteredElementCollector(doc!).OfClass(typeof(Wall)).Cast<Wall>().First().Width;
                    Check("MCP values independently match fixture interior dimensions", nativeDims.Select(d => d.Value!.Value).Order()
                        .Zip(new[] { 14 - thickness, 16 - thickness }, (a, b) => Math.Abs(a - b) < 1e-5).All(x => x));
                    app.ActiveUIDocument!.RefreshActiveView();
                    Undo(app); stage++; break;
                case 20:
                    if (Dims(doc!) != 0) return;
                    Check("MCP pair shares one Undo item", true); Check("final model invariants restored", new FilteredElementCollector(doc!).GetElementCount() == count && Hash() == hash && Cut(doc!) == 4);
                    Finish(true); break;
            }
        }
        catch (Exception error) { Finish(false, error); }
    }
    private static Room[] Rooms(Document doc) => new FilteredElementCollector(doc).OfClass(typeof(SpatialElement)).OfType<Room>().ToArray();
    private static View View(Document doc, string name) => new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Single(v => v.Name == name);
    private static bool NativeValues(Document doc, long[] ids) => ids.Length == 2 && ids.All(id => doc.GetElement(new ElementId(id)) is Dimension d && d.Value > 0 && d.References.Size == 2 && !d.IsLocked);
    private void Association(Document doc, RoomDimensionPreview plan, long[] ids)
    {
        var originalValue = ((Dimension)doc.GetElement(new ElementId(ids[0]))).Value!.Value;
        using var group = new TransactionGroup(doc, "Temporary native dimension association probe"); group.Start();
        using (var transaction = new Transaction(doc, "Move fixture wall temporarily"))
        {
            transaction.Start(); var line = plan.Lines[0]; var start = new XYZ(line.Start.X, line.Start.Y, line.Start.Z); var end = new XYZ(line.End.X, line.End.Y, line.End.Z);
            ElementTransformUtils.MoveElement(doc, Reference.ParseFromStableRepresentation(doc, line.FirstReference).ElementId, (end - start).Normalize());
            doc.Regenerate();
            Check("native dimension follows wall-face movement", Math.Abs(Math.Abs(((Dimension)doc.GetElement(new ElementId(ids[0]))).Value!.Value - originalValue) - 1) < 1e-5);
            Check("association probe transaction commits", transaction.Commit() == TransactionStatus.Committed);
        }
        Check("association probe rolls back", group.RollBack() == TransactionStatus.RolledBack);
        Check("association probe restores dimension and room", Math.Abs(((Dimension)doc.GetElement(new ElementId(ids[0]))).Value!.Value - originalValue) < 1e-5
            && Math.Abs(((Room)doc.GetElement(plan.RoomId)).Area - plan.WidthFeet * plan.DepthFeet) < 1e-4);
    }
    private void RoomGuards(UIApplication app, FoundationApplication host)
    {
        var doc = app.ActiveUIDocument.Document; var model = new RevitModel(app, host);
        string Error(Action action) { try { action(); return "none"; } catch (OperationException e) { return e.Code; } }
        Check("native tag clearance refuses collision", Error(() => model.PlanRoomDimensions(roomId, new(5, 20))) == "tag_collision");
        Check("native small room refuses excessive inset", Error(() => model.PlanRoomDimensions(roomId, new(20, 1))) == "room_too_small");
        using var group = new TransactionGroup(doc, "Temporary native room guard probes"); group.Start();
        using (var t = new Transaction(doc, "Hide one existing dimension"))
        { t.Start(); doc.ActiveView.HideElements([new ElementId(dimensionIds[0])]); t.Commit(); }
        Check("hidden existing pair still prevents duplication", model.PlanRoomDimensions(roomId, new()).ExistingIds.Length == 2);
        using (var t = new Transaction(doc, "Delete one test dimension temporarily"))
        { t.Start(); doc.Delete(new ElementId(dimensionIds[0])); t.Commit(); }
        var partial = model.PlanRoomDimensions(roomId, new());
        Check("native partial pair refuses another dimension", new OperationExecutor().Execute(model, new(Guid.NewGuid(), OperationKind.CreateRoomDimensions,
            model.Capture().Target, ElementIds: [roomId], RoomPreview: partial)).Code == "partial_dimensions" && Dims(doc) == 1);
        using (var t = new Transaction(doc, "Hide Dimensions category temporarily"))
        { t.Start(); doc.ActiveView.SetCategoryHidden(new ElementId(BuiltInCategory.OST_Dimensions), true); t.Commit(); }
        Check("hidden category refuses invisible dimensions", Error(() => model.PlanRoomDimensions(roomId, new())) == "dimensions_hidden");
        Check("room guard probes roll back", group.RollBack() == TransactionStatus.RolledBack && Dims(doc) == 2 && model.PlanRoomDimensions(roomId, new()).ExistingIds.Length == 2);
    }
    private void Capture(Window window, string name)
    {
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(Path.Combine(folder!, name)); encoder.Save(stream);
    }
    private string Hash() { using var stream = new FileStream(fixture, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); return Convert.ToHexString(SHA256.HashData(stream)); }
    private void Finish(bool passed, Exception? error = null)
    {
        stopped = true; pulse?.Dispose(); File.WriteAllText(Path.Combine(folder!, "native-result.json"), JsonSerializer.Serialize(new { passed, checks, fixture, count, savedHash = hash,
            binding = FoundationApplication.Instance?.Binding, stage, error = error?.ToString() }, Protocol.Json));
    }
    public string GetName() => "RevitThyme utilities native qualification";
    public Result OnShutdown(UIControlledApplication app) { stopped = true; pulse?.Dispose(); if (folder is not null) app.Idling -= Idle; pulseEvent?.Dispose(); return Result.Succeeded; }
}
