using System.Collections.Immutable;
using System.IO;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitThyme.Core;

namespace RevitThyme.Addin;

internal static partial class NativeTimberKit
{
    internal static TimberNativeKitReceipt Finalize(UIApplication app, RevitModel model, TimberNativeKitRequest request, CancellationToken ct)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var source = app.ActiveUIDocument?.Document ?? throw new OperationException("no_document", "Open the pinned source document.");
        var after = request.Target; var files = ImmutableArray<TimberNativeFile>.Empty;
        var created = ImmutableArray<TimberNativeElement>.Empty; string? preparedHash = null;
        NativeTimberKitPreservation? preservation = null; TransactionGroup? group = null;
        NativeTimberKitDocuments? documents = null;
        TimberNativeKitPlan? plan = null; bool committed = false, readback = false;
        try
        {
            plan = NativeTimberKitFiles.Read(request, ct);
            if (source.IsFamilyDocument || !OptionalFunctionalityUtils.IsDWGExportAvailable())
                throw new InvalidDataException("Native kit generation needs a project document and the installed DWG export module.");
            if (!new FilteredElementCollector(source).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>().Any(t => t.ViewFamily == ViewFamily.Drafting) ||
                source.GetElement(source.GetDefaultElementTypeId(ElementTypeGroup.TextNoteType)) is not TextNoteType)
                throw new InvalidDataException("Source project lacks a drafting-view or native text type.");
            var before = model.Capture();
            if (before.Target != request.Target || !before.CanModify) throw new InvalidDataException("Source target changed before native finalization.");
            var captured = NativeTimberExtraction.ExtractCancellable(source, request.Target, plan.Profile, plan.Scope, ct);
            if (captured.DocumentFingerprint != plan.SourceFingerprint || captured.Diagnostics.Any(d => d.Severity == TimberSeverity.Error))
                throw new InvalidDataException("Source geometry fingerprint changed before native generation.");
            preservation = new(app.Application, source, ct);
            preservation.CalibrateBookkeeping(ct);
            string cuts = NativeTimberKitFiles.FreshDirectory(request.Directory, "native-cuts");
            string support = NativeTimberKitFiles.FreshDirectory(request.Directory, "native-export-support");
            NativeTimberKitFiles.FreshDirectory(request.Directory, "native-reference-inputs");
            group = new TransactionGroup(source, "TimberFold kit " + request.RunId.ToString("N")[..12]);
            if (group.Start() != TransactionStatus.Started) throw new InvalidOperationException("Native generation group did not start.");
            documents = new NativeTimberKitDocuments(source, plan, request.Directory);
            documents.Create(ct); documents.AuditNew(preservation.Before.Select(e => e.Id).ToArray(), ct);
            created = documents.Elements.ToImmutableArray();
            files = NativeTimberKitExport.Run(app, source, documents, plan, request.Directory, cuts, support, ct);
            readback = true;
            documents.Verify(ct); documents.AuditNew(preservation.Before.Select(e => e.Id).ToArray(), ct);
            created = documents.Elements.ToImmutableArray();
            preservation.RequireUnchanged(true, ct);
            if (!source.Equals(app.ActiveUIDocument?.Document) || NativeTimberKitPreservation.OriginalFingerprint(source, plan, request.Target, created, ct) != plan.SourceFingerprint)
                throw new InvalidDataException("Original source fingerprint changed after native documentation.");
            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                if (NativeTimberKitFiles.Hash(Path.Combine(request.Directory, file.RelativePath)) != file.Sha256)
                    throw new InvalidDataException("Native output changed before assimilation.");
            }
            after = model.Capture().Target;
            var createdIds = created.Select(e => e.Id).ToHashSet();
            var createdCatalog = NativeTimberKitPreservation.Catalog(source, ct).Where(e => createdIds.Contains(e.Id)).ToArray();
            preparedHash = NativeTimberKitFiles.WriteNew(Path.Combine(request.Directory, "native-prepared.json"), new
            {
                schema = 2, request.RunId, plan.PreviewRunId, state = "Prepared", sourcePath = source.PathName,
                request.InputSealSha256,
                savedSourceSha256 = preservation.SavedSourceHash, sourceWasModified = preservation.WasModified, originalViewId = preservation.ViewId,
                sourceTarget = request.Target, afterTarget = after, plan.SourceFingerprint, plan.Profile, plan.Scope, sourcePreserved = true, nativeReadbackPassed = true,
                beforeCatalog = preservation.Before, bookkeepingCalibration = preservation.BookkeepingCalibration, createdElements = created,
                createdCatalog, files,
                referenceMultiplier = NativeTimberKitReferenceTransform.Multiplier, referenceInputs = documents.ReferenceMappings,
                faceMap = plan.Fitted.Draft.FaceMap, openingMap = plan.Fitted.Draft.OpeningMap, labels = plan.Guides.Labels,
                undoLabel = "TimberFold kit " + request.RunId.ToString("N")[..12], physicalChecksOutstanding = true
            });
            if (new FileInfo(Path.Combine(request.Directory, "native-prepared.json")).Length > 33554432)
                throw new InvalidDataException("Prepared native evidence exceeds its bounded recovery limit.");
            ct.ThrowIfCancellationRequested();
            NativeTimberKitFiles.CheckInputSeal(request);
            if (group.Assimilate() != TransactionStatus.Committed) throw new InvalidOperationException("Native generation assimilation was not committed.");
            committed = true;
            after = model.Capture().Target;
            return new(TimberNativeKitState.Committed, request.RunId, after, created, files, [], true, true, preparedHash, clock.Elapsed.TotalMilliseconds, request.InputSealSha256);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            if (documents is not null) created = documents.Elements.ToImmutableArray();
            return NativeTimberKitRecovery.Recover(new RecoveryRuntime(app, model, source, preservation, group),
                new(request, plan, after, created, files, preparedHash, committed, readback, clock), error);
        }
        finally
        {
            preservation?.Dispose();
            // A pending/failing group is already Unconfirmed above. Cleanup must
            // not discard its owned receipt/IDs by replacing it with a thrown Dispose.
            try { group?.Dispose(); }
            catch (Exception error) when (!committed && error is not OutOfMemoryException) { }
        }
    }
}
