using System.Globalization;
using System.Collections.Immutable;
using System.IO;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using RevitThyme.Core;

namespace RevitThyme.Addin;

internal sealed class NativeTimberKitPreservation : IDisposable
{
    internal sealed record ElementStamp(long Id, string UniqueId, string VersionGuid, string ContentHash);
    internal sealed record BookkeepingProof(string Method, string Transaction, bool RollbackVerified,
        ImmutableArray<NativeTimberPreservationStamp> Allowed);
    private readonly Autodesk.Revit.ApplicationServices.Application application;
    private readonly Document document;
    private readonly Document[] originalDocuments;
    private readonly HashSet<long> changed = [];
    private readonly HashSet<long> deleted = [];
    private readonly NativeTimberPreservationStamp[] originalStates;
    private readonly string? sourceFileHash;
    internal ElementStamp[] Before { get; }
    internal long ViewId { get; }
    internal bool WasModified { get; }
    internal string? SavedSourceHash => sourceFileHash;
    internal BookkeepingProof? BookkeepingCalibration { get; private set; }

    internal NativeTimberKitPreservation(Autodesk.Revit.ApplicationServices.Application application, Document document, CancellationToken ct)
    {
        this.application = application; this.document = document;
        originalDocuments = application.Documents.Cast<Document>().ToArray();
        Before = Catalog(document, ct); ViewId = document.ActiveView.Id.Value; WasModified = document.IsModified;
        originalStates = States(document, Before);
        sourceFileHash = System.IO.File.Exists(document.PathName) ? NativeTimberKitFiles.Hash(document.PathName) : null;
        application.DocumentChanged += OnChanged;
    }
    internal void CalibrateBookkeeping(CancellationToken ct)
    {
        if (BookkeepingCalibration is not null || changed.Count != 0 || deleted.Count != 0)
            throw new InvalidDataException("Bookkeeping calibration requires an untouched per-run monitor.");
        const string transactionName = "TimberFold documentation bookkeeping calibration";
        NativeTimberPreservationStamp[] candidates = [];
        using var group = new TransactionGroup(document, transactionName);
        if (group.Start() != TransactionStatus.Started) throw new InvalidOperationException("Bookkeeping calibration group did not start.");
        try
        {
            ct.ThrowIfCancellationRequested();
            using (var transaction = NativeTimberKitDocuments.StartTransaction(document, transactionName))
            {
                var type = new FilteredElementCollector(document).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>().First(t => t.ViewFamily == ViewFamily.Drafting);
                var view = ViewDrafting.Create(document, type.Id); view.Scale = 1; document.Regenerate();
                ct.ThrowIfCancellationRequested();
                if (transaction.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Bookkeeping calibration did not commit its temporary view.");
            }
            candidates = originalStates.Where(e => changed.Contains(e.Id)).ToArray();
            if (!NativeTimberKitPreservationCheck.Check(originalStates, States(document, Catalog(document, ct)), changed, deleted, candidates))
                throw new InvalidDataException("Documentation calibration changed source content or an ineligible original element.");
            RequireContext();
        }
        finally
        {
            if (group.GetStatus() != TransactionStatus.Started || group.RollBack() != TransactionStatus.RolledBack)
                throw new InvalidOperationException("Bookkeeping calibration rollback was not confirmed.");
            RequireUnchanged(false, CancellationToken.None);
        }
        ct.ThrowIfCancellationRequested();
        BookkeepingCalibration = new("Controlled ViewDrafting.Create+Scale1; committed inner transaction; checked TransactionGroup rollback",
            transactionName, true, candidates.ToImmutableArray());
        changed.Clear(); deleted.Clear();
    }
    internal void RequireUnchanged(bool committed, CancellationToken ct)
    {
        var current = Catalog(document, ct);
        if (!NativeTimberKitPreservationCheck.Check(originalStates, States(document, current),
            committed ? changed : [], committed ? deleted : [], BookkeepingCalibration?.Allowed ?? []))
            throw new InvalidDataException("Native documentation changed a pre-existing source element, parameter or uncalibrated bookkeeping element.");
        RequireContext();
        if (!committed && (current.Length != Before.Length || document.IsModified != WasModified))
            throw new InvalidDataException("Native rollback did not restore the complete source before-state.");
    }
    private void RequireContext()
    {
        var openDocuments = application.Documents.Cast<Document>().ToArray();
        if (openDocuments.Length != originalDocuments.Length || originalDocuments.Any(d => !d.IsValidObject || !openDocuments.Any(d.Equals)))
            throw new InvalidDataException("Native verification did not restore the complete original open-document inventory.");
        if (document.ActiveView.Id.Value != ViewId || sourceFileHash is not null && NativeTimberKitFiles.Hash(document.PathName) != sourceFileHash)
            throw new InvalidDataException("Native documentation changed the original active view or saved source file.");
    }
    private static NativeTimberPreservationStamp[] States(Document doc, IEnumerable<ElementStamp> catalog) => catalog.Select(stamp =>
    {
        var element = doc.GetElement(new ElementId(stamp.Id));
        return new NativeTimberPreservationStamp(stamp.Id, stamp.UniqueId, stamp.ContentHash, element.GetType().FullName!,
            element.Category is not null, element.GetTypeId().Value, element.OwnerViewId.Value, element.ViewSpecific, element.GetEntitySchemaGuids().Count);
    }).ToArray();
    internal static long[] Ids(Document doc) => new FilteredElementCollector(doc).WhereElementIsNotElementType().ToElementIds()
        .Concat(new FilteredElementCollector(doc).WhereElementIsElementType().ToElementIds()).Select(id => id.Value).Order().ToArray();
    internal static ElementStamp[] Catalog(Document doc, CancellationToken ct)
    {
        var ids = Ids(doc);
        if (ids.Length > 500000) throw new InvalidDataException("Source preservation catalog exceeds the bounded 500000-element limit.");
        return ids.Select(id =>
        {
            ct.ThrowIfCancellationRequested();
            var element = doc.GetElement(new ElementId(id)) ?? throw new InvalidDataException("Source element disappeared during capture.");
            var parameters = element.Parameters.Cast<Parameter>().Select(p => new NativeTimberParameterStamp(
                p.Id.Value, p.StorageType.ToString(), p.HasValue, p.StorageType switch
                {
                    StorageType.Double => p.AsDouble().ToString("R", CultureInfo.InvariantCulture),
                    StorageType.Integer => p.AsInteger().ToString(CultureInfo.InvariantCulture),
                    StorageType.ElementId => p.AsElementId().Value.ToString(CultureInfo.InvariantCulture),
                    StorageType.String => p.AsString(), _ => null
                })).ToArray();
            // VersionGuid is retained only as persistence metadata: it does NOT detect in-session edits.
            return new ElementStamp(id, element.UniqueId, element.VersionGuid.ToString(),
                NativeTimberKitPreservationCheck.ContentHash(element.Name, element.GetTypeId().Value, parameters,
                    NativeTimberKitOwnership.ReadOwnershipText(element)));
        }).ToArray();
    }
    private void OnChanged(object? sender, DocumentChangedEventArgs args)
    {
        if (!document.Equals(args.GetDocument())) return;
        changed.UnionWith(args.GetModifiedElementIds().Select(id => id.Value));
        deleted.UnionWith(args.GetDeletedElementIds().Select(id => id.Value));
    }
    internal static string OriginalFingerprint(Document doc, TimberNativeKitPlan plan, Target target,
        IReadOnlyCollection<TimberNativeElement> owned, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var original = System.Text.Json.JsonSerializer.Deserialize<TimberSnapshot>(plan.SourceSnapshotJson.AsSpan(), Protocol.Json)
            ?? throw new InvalidDataException("Original snapshot is missing.");
        var actual = NativeTimberExtraction.ExtractCancellable(doc, target, plan.Profile, plan.Scope, ct);
        var sourceIds = original.Sources.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        var allowed = owned.Where(e => e.Role is "CutImport" or "ReferenceImport").Select(e => e.Id.ToString(CultureInfo.InvariantCulture)).ToHashSet(StringComparer.Ordinal);
        foreach (var extra in actual.Sources.Where(s => !sourceIds.Contains(s.Id)))
        {
            ct.ThrowIfCancellationRequested();
            if (!allowed.Contains(extra.Id) || doc.GetElement(extra.UniqueId) is not ImportInstance import || !import.ViewSpecific ||
                !NativeTimberKitOwnership.Matches(import, plan.RunId))
                throw new InvalidDataException("Unexpected new source geometry appeared during native documentation: " + extra.Id);
        }
        var sources = actual.Sources.Where(s => sourceIds.Contains(s.Id)).OrderBy(s => s.Id, StringComparer.Ordinal).ToArray();
        if (sources.Length != original.Sources.Length || actual.Diagnostics.Any(d => d.Severity == TimberSeverity.Error &&
            (d.SourceId is null || !allowed.Contains(d.SourceId)))) throw new InvalidDataException("Source geometry became incomplete during documentation.");
        return NativeTimberGeometry.Hash(new { Project = doc.ProjectInformation.UniqueId, actual.PhaseId, OptionIds = actual.DesignOptionIds,
            Sources = sources, Faces = actual.Faces, Openings = actual.Openings });
    }
    public void Dispose() => application.DocumentChanged -= OnChanged;
}
