using System.IO;

namespace RevitThyme.Core;

public sealed class DocumentLifecycleCoordinator(IDocumentLifecycleRuntime runtime)
{
    private sealed record ClosePlan(ILifecycleDocument Document, CloseActivationTarget Target);
    private sealed record ActivatedClose(ClosePlan Plan, string ViewId);
    private const string CloseBlocked = "Close requires exact editable nonworkshared local documents outside a transaction.";

    public DocumentLifecycleResult ChangeDocument(OperationKind operation, DocumentLifecycleRequest options)
    {
        runtime.AssertPath(options.Path);
        AssertActive();
        runtime.AssertLocalFile(options.Path);
        string beforeHash = runtime.Hash(options.Path);
        if (options.ExpectedSha256 is { } expected && !beforeHash.Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new OperationException("file_changed", "Saved file SHA-256 differs from the expected value. Nothing was opened or saved.");
        var document = Find(options.Path);
        AssertIdentity(operation, document, options);
        if (document is not null) AssertKind(document, "Linked, family, workshared, read-only or modifiable documents are outside this scope.");
        if (operation == OperationKind.SaveDocument && Required(document).IsReadOnlyFile)
            throw new OperationException("read_only_file", "This document was opened from a read-only file. Nothing was saved. Saving to another path requires SaveAs, which is outside this operation's scope.");
        AssertOpen(operation, document, options.Path);
        var close = PrepareClose(operation, document, options);
        try
        {
            if (operation == OperationKind.CloseDocument) return Close(Required(document), options.Path, beforeHash, close);
            var execution = Perform(operation, document, options.Path);
            return Complete(operation, execution.Document, options.Path, beforeHash, execution.Performed);
        }
        catch (Exception)
        { throw new OperationException("lifecycle_unconfirmed", "Native lifecycle call or completion verification failed. It may have opened, activated, saved or closed the file. Recover this request and inspect native state; no automatic restoration or retry."); }
    }

    private void AssertActive()
    {
        if (runtime.ActiveDocument is { } active && (active.IsModifiable || active.IsReadOnly || active.IsFamilyDocument || active.IsWorkshared))
            throw new OperationException("lifecycle_blocked", "Use an editable local nonworkshared project outside a transaction.");
    }

    private ILifecycleDocument? Find(string path) => runtime.Documents.SingleOrDefault(d => string.Equals(d.PathName, path, StringComparison.OrdinalIgnoreCase));
    private static ILifecycleDocument Required(ILifecycleDocument? document) => document ?? throw new InvalidDataException("Native document identity changed.");

    private static void AssertIdentity(OperationKind operation, ILifecycleDocument? document, DocumentLifecycleRequest options)
    {
        if (operation != OperationKind.OpenDocument && (document is null || document.Id != options.DocumentId))
            throw new OperationException("wrong_document", "The exact document ID/path is not open in this session.");
    }

    private static void AssertKind(ILifecycleDocument document, string message)
    {
        if (document.IsLinked || document.IsFamilyDocument || document.IsWorkshared || document.IsReadOnly || document.IsModifiable)
            throw new OperationException("lifecycle_blocked", message);
    }

    private void AssertOpen(OperationKind operation, ILifecycleDocument? document, string path)
    {
        if (operation != OperationKind.OpenDocument) return;
        using var info = runtime.ReadFileInfo(path);
        if (info.IsWorkshared || info.Format != "2027")
            throw new OperationException("unsupported_document", "Open only an existing Revit 2027 nonworkshared RVT; no upgrade or detach.");
        if (document?.IsModified == true)
            throw new OperationException("document_dirty", "This path is already open with unsaved changes. Inspect its native identity instead.");
    }

    private ClosePlan? PrepareClose(OperationKind operation, ILifecycleDocument? document, DocumentLifecycleRequest options)
    {
        if (operation != OperationKind.CloseDocument) return null;
        var target = Required(document);
        if (target.IsModified) throw new OperationException("document_dirty", "Close refuses unsaved changes. Save explicitly first; no implicit discard.");
        if (target.Matches(runtime.ActiveDocument) && options.ActivateBeforeClose is null)
            throw new OperationException("close_blocked", "Active close requires an explicit different already-open clean document to activate first. Nothing was closed.");
        if (options.ActivateBeforeClose is not { } next) return null;
        runtime.AssertPath(next.Path); runtime.AssertLocalFile(next.Path);
        var nextDocument = Find(next.Path);
        if (nextDocument is null || nextDocument.Id != next.DocumentId)
            throw new OperationException("wrong_document", "The exact activation document ID/path is not open in this session.");
        AssertCleanClose(nextDocument, next.Path, next.DocumentId, next.ExpectedSha256);
        return new(nextDocument, next);
    }

    private DocumentLifecycleResult Close(ILifecycleDocument document, string path, string beforeHash, ClosePlan? next)
    {
        string id = document.Id, title = document.Title;
        var activated = ActivateForClose(document, path, id, beforeHash, next);
        if (!document.Close(false) || runtime.Documents.Any(d => d.Id == id) || runtime.Hash(path) != beforeHash)
            throw new InvalidDataException("Native close did not verify.");
        if (activated is not null) VerifyCloseActivation(activated.Plan, activated.ViewId);
        return new(id, path, title, false, false, beforeHash, true);
    }

    private ActivatedClose? ActivateForClose(ILifecycleDocument document, string path, string id, string beforeHash, ClosePlan? next)
    {
        if (next is null) return null;
        if (!runtime.Activate(next.Target.Path).Matches(next.Document))
            throw new InvalidDataException("Activated a different native document.");
        // First UI activation can establish a background document's active view.
        string view = next.Document.ActiveViewId;
        AssertCleanClose(document, path, id, beforeHash);
        VerifyCloseActivation(next, view);
        return new(next, view);
    }

    private (ILifecycleDocument? Document, bool Performed) Perform(OperationKind operation, ILifecycleDocument? document, string path)
    {
        bool performed = false;
        if (operation == OperationKind.OpenDocument && document is null)
        { performed = true; document = runtime.Open(path); }
        else if (operation == OperationKind.ActivateDocument && !Required(document).Matches(runtime.ActiveDocument))
        {
            performed = true;
            if (!runtime.Activate(path).Matches(document)) throw new InvalidDataException("Activated a different native document.");
        }
        else if (operation == OperationKind.SaveDocument)
            performed = Save(Required(document));
        return (document, performed);
    }

    private bool Save(ILifecycleDocument document)
    {
        if (!document.Matches(runtime.ActiveDocument ?? throw new OperationException("no_document", "Open a Revit project and refresh.")))
            throw new InvalidDataException("Save target is no longer active.");
        if (!document.IsModified) return false;
        document.Save();
        return true;
    }

    private DocumentLifecycleResult Complete(OperationKind operation, ILifecycleDocument? document, string path, string beforeHash, bool performed)
    {
        if (document is null || !document.IsValidObject || document.IsWorkshared || document.IsFamilyDocument || document.IsLinked
            || !string.Equals(document.PathName, path, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Native document identity changed.");
        string hash = runtime.Hash(path);
        if (operation != OperationKind.SaveDocument && hash != beforeHash)
            throw new InvalidDataException("Open/activation changed the saved file.");
        return new(document.Id, document.PathName, document.Title, document.Matches(runtime.ActiveDocument), document.IsModified, hash, performed);
    }

    private void VerifyCloseActivation(ClosePlan next, string viewId)
    {
        AssertCleanClose(next.Document, next.Target.Path, next.Target.DocumentId, next.Target.ExpectedSha256);
        if (!next.Document.Matches(runtime.ActiveDocument) || next.Document.ActiveViewId != viewId)
            throw new InvalidDataException("Close activation context changed.");
    }

    private void AssertCleanClose(ILifecycleDocument document, string path, string id, string expectedHash)
    {
        if (!document.IsValidObject || document.Id != id || !string.Equals(document.PathName, path, StringComparison.OrdinalIgnoreCase))
            throw new OperationException("lifecycle_blocked", CloseBlocked);
        AssertKind(document, CloseBlocked);
        if (document.IsModified) throw new OperationException("document_dirty", "Both close and activation documents must be clean; no implicit save or discard.");
        runtime.AssertLocalFile(path);
        if (!runtime.Hash(path).Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new OperationException("file_changed", "Saved file differs from the expected activation/close hash.");
    }
}
