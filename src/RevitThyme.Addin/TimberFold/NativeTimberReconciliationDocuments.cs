using System.Collections.Immutable;
using System.IO;
using System.Text.Json;
using Autodesk.Revit.DB;
using RevitThyme.Core;

namespace RevitThyme.Addin;

internal static class NativeTimberReconciliationDocuments
{
    internal static void Check(Document doc, TimberNativeKitPlan plan, string directory, JsonElement prepared,
        ImmutableArray<TimberNativeElement> created, CancellationToken ct)
    {
        string prefix = "TF-" + plan.RunId.ToString("N")[..12];
        var ids = created.Select(e => e.Id).ToHashSet();
        var cutViews = created.Where(e => e.Role == "CutView").Select(e => Require<ViewDrafting>(e.Id)).ToArray();
        var cutImports = created.Where(e => e.Role == "CutImport").Select(e => Require<ImportInstance>(e.Id)).ToArray();
        var expected = NativeTimberKitContours.Expected(plan, directory, ct);
        if (cutViews.Length != expected.Count || cutImports.Length != expected.Count)
            throw new InvalidDataException("Committed kit has an incomplete native cutting-view inventory.");
        foreach (var (file, contours) in expected)
        {
            ct.ThrowIfCancellationRequested();
            var view = cutViews.Single(v => v.Name == prefix + " CUT " + Path.GetFileNameWithoutExtension(file));
            var import = cutImports.Single(i => i.ViewSpecific && i.OwnerViewId == view.Id);
            if (!view.CanBePrinted || view.Scale != 1 || new FilteredElementCollector(doc, view.Id).OfClass(typeof(TextNote)).Any())
                throw new InvalidDataException("Committed native CUT view has changed scale, printability or labels.");
            NativeTimberKitReadback.Check(doc, import, file, contours, "CUT", ct);
            RequireOwnedAnnotations(view);
        }
        var mappings = JsonSerializer.Deserialize<NativeTimberKitDocuments.ReferenceMapping[]>(
            prepared.GetProperty("referenceInputs").GetRawText(), Protocol.Json) ?? [];
        if (mappings.Length != plan.Guides.Drawings.Length || prepared.GetProperty("referenceMultiplier").GetInt32() != 48 ||
            mappings.Select(m => m.PhysicalGuideFile).Distinct(StringComparer.Ordinal).Count() != mappings.Length ||
            created.Count(e => e.Role == "ReferenceView") != mappings.Length || created.Count(e => e.Role == "ReferenceImport") != mappings.Length)
            throw new InvalidDataException("Committed native reference mapping is incomplete or ambiguous.");
        var notes = created.Where(e => e.Role is "PartLabel" or "GuideLabel").Select(e => Require<TextNote>(e.Id)).ToList();
        var sheets = created.Where(e => e.Role == "NativeSheet").Select(e => Require<ViewSheet>(e.Id)).ToArray();
        var ports = created.Where(e => e.Role == "Viewport").Select(e => Require<Viewport>(e.Id)).ToArray();
        if (sheets.Length != mappings.Length || ports.Length != mappings.Length)
            throw new InvalidDataException("Committed native sheet/viewport inventory is incomplete.");
        for (int i = 0; i < plan.Guides.Drawings.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            var drawing = plan.Guides.Drawings[i];
            var mapping = mappings.Single(m => m.PhysicalGuideFile == drawing.File);
            var view = Require<ViewDrafting>(mapping.ViewId); var import = Require<ImportInstance>(mapping.ImportId);
            string path = Path.Combine(directory, mapping.NativeRelativePath);
            if (mapping.CoordinateMultiplier != 48 || view.Scale != 48 || view.UniqueId != mapping.ViewUniqueId ||
                import.UniqueId != mapping.ImportUniqueId || !import.ViewSpecific || import.OwnerViewId != view.Id ||
                !created.Any(e => e.Id == view.Id.Value && e.Role == "ReferenceView") ||
                !created.Any(e => e.Id == import.Id.Value && e.Role == "ReferenceImport") ||
                NativeTimberKitFiles.Hash(path) != mapping.NativeInputSha256 ||
                NativeTimberKitFiles.Hash(Path.Combine(directory, "guides", drawing.File)) != mapping.PhysicalGuideSha256)
                throw new InvalidDataException("Committed native reference scale, ownership or immutable inputs changed.");
            NativeTimberKitReference.Check(doc, import, path, ct);
            foreach (var label in plan.Guides.Labels.Where(l => l.GuideFile == drawing.File))
            {
                ct.ThrowIfCancellationRequested();
                var position = new XYZ(label.PhysicalPositionInches.X * 48 / 12, label.PhysicalPositionInches.Y * 48 / 12, 0);
                int match = notes.FindIndex(n => n.ViewSpecific && n.OwnerViewId == view.Id && Normalize(n.Text) == Normalize(label.Text) &&
                    n.Coord.DistanceTo(position) * 12 / 48 <= .00001 &&
                    Math.Abs(((TextNoteType)doc.GetElement(n.GetTypeId())).get_Parameter(BuiltInParameter.TEXT_SIZE).AsDouble() * 12 - label.TextHeightInches) <= .0000001);
                if (match < 0) throw new InvalidDataException("A committed native guide label is missing, moved or changed.");
                notes.RemoveAt(match);
            }
            var sheet = sheets.Single(s => s.SheetNumber == prefix + "-" + (i + 1).ToString("000", System.Globalization.CultureInfo.InvariantCulture));
            var viewport = ports.Single(p => p.SheetId == sheet.Id && p.ViewId == view.Id);
            var center = new XYZ((drawing.MaximumInches.X - drawing.MinimumInches.X) / 24 + .5,
                (drawing.MaximumInches.Y - drawing.MinimumInches.Y) / 24 + .5, 0);
            if (sheet.Name != drawing.Title || sheet.GetAllViewports().Count != 1 || !sheet.GetAllViewports().Contains(viewport.Id) ||
                viewport.GetBoxCenter().DistanceTo(center) > .00001 / 12 ||
                new FilteredElementCollector(doc, sheet.Id).OfCategory(BuiltInCategory.OST_TitleBlocks).Any())
                throw new InvalidDataException("A committed native sheet, viewport placement or titleblock policy changed.");
            RequireOwnedAnnotations(view); RequireOwnedAnnotations(sheet);
        }
        if (notes.Count != 0) throw new InvalidDataException("Committed native kit has unmapped guide labels.");

        T Require<T>(long id) where T : Element => doc.GetElement(new ElementId(id)) as T
            ?? throw new InvalidDataException("Committed native element has a different kind: " + id);
        void RequireOwnedAnnotations(View view)
        {
            if (new FilteredElementCollector(doc, view.Id).WhereElementIsNotElementType()
                .Any(e => e.ViewSpecific && e.OwnerViewId == view.Id && !ids.Contains(e.Id.Value)))
                throw new InvalidDataException("Committed kit view/sheet contains an unrecorded annotation.");
        }
        static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').TrimEnd('\n');
    }
}
