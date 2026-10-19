using System.IO;
using System.Security.Cryptography;
using Autodesk.Revit.DB;
using RevitThyme.Core;

namespace RevitThyme.Addin;

public sealed partial class RevitModel
{
    public DocumentLifecycleResult ChangeDocument(OperationKind operation, DocumentLifecycleRequest options)
    {
        host.AssertLifecyclePath(options.Path);
        // File/UI lifecycle must never run inside the query/modify transaction.
        if (Document.IsModifiable || Document.IsReadOnly || Document.IsFamilyDocument || Document.IsWorkshared)
            throw new OperationException("lifecycle_blocked", "Use an editable local nonworkshared project outside a transaction.");
        AssertLocalFile(options.Path);
        string beforeHash = LifecycleHash(options.Path);
        if (options.ExpectedSha256 is { } expected && !beforeHash.Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new OperationException("file_changed", "Saved file SHA-256 differs from the expected value. Nothing was opened or saved.");
        var document = app.Application.Documents.Cast<Document>().SingleOrDefault(d => string.Equals(d.PathName, options.Path, StringComparison.OrdinalIgnoreCase));
        if (operation != OperationKind.OpenDocument && (document is null || host.DocumentId(document) != options.DocumentId))
            throw new OperationException("wrong_document", "The exact document ID/path is not open in this session.");
        if (document is not null && (document.IsLinked || document.IsFamilyDocument || document.IsWorkshared || document.IsReadOnly || document.IsModifiable))
            throw new OperationException("lifecycle_blocked", "Linked, family, workshared, read-only or modifiable documents are outside this scope.");
        if (operation == OperationKind.OpenDocument)
        {
            using var info = BasicFileInfo.Extract(options.Path);
            if (info.IsWorkshared || info.Format != "2027") throw new OperationException("unsupported_document", "Open only an existing Revit 2027 nonworkshared RVT; no upgrade or detach.");
            if (document?.IsModified == true) throw new OperationException("document_dirty", "This path is already open with unsaved changes. Inspect its native identity instead.");
        }
        bool performed = false;
        try
        {
            if (operation == OperationKind.OpenDocument && document is null)
            { performed = true; document = app.Application.OpenDocumentFile(options.Path); }
            else if (operation == OperationKind.ActivateDocument && !document!.Equals(app.ActiveUIDocument?.Document))
            {
                performed = true;
                if (!app.OpenAndActivateDocument(options.Path).Document.Equals(document)) throw new InvalidDataException("Activated a different native document.");
            }
            else if (operation == OperationKind.SaveDocument)
            {
                if (!document!.Equals(Document)) throw new InvalidDataException("Save target is no longer active.");
                if (document.IsModified) { performed = true; document.Save(); }
            }
            if (document is null || !document.IsValidObject || document.IsWorkshared || document.IsFamilyDocument || document.IsLinked
                || !string.Equals(document.PathName, options.Path, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Native document identity changed.");
            string hash = LifecycleHash(options.Path);
            if (operation != OperationKind.SaveDocument && hash != beforeHash) throw new InvalidDataException("Open/activation changed the saved file.");
            return new(host.DocumentId(document), document.PathName, document.Title, document.Equals(app.ActiveUIDocument?.Document), document.IsModified, hash, performed);
        }
        catch (Exception)
        { throw new OperationException("lifecycle_unconfirmed", "Native lifecycle call or completion verification failed. It may have opened, activated or saved the file. Recover this request and inspect native state."); }
    }
    private static void AssertLocalFile(string path)
    {
        if (new DriveInfo(Path.GetPathRoot(path)!).DriveType != DriveType.Fixed)
            throw new OperationException("unsupported_path", "Use a local fixed-drive RVT; mapped network and removable drives are outside this scope.");
        if (!File.Exists(path)) throw new OperationException("file_missing", "The explicit local RVT does not exist.");
        for (string? current = path; current is not null; current = Path.GetDirectoryName(current))
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new OperationException("unsupported_path", "Use an explicit local RVT without reparse-point aliases.");
    }
    private static string LifecycleHash(string path)
    {
        var before = new FileInfo(path);
        long length = before.Length; DateTime stamp = before.LastWriteTimeUtc;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        string hash = Convert.ToHexStringLower(SHA256.HashData(stream));
        before.Refresh();
        if (length != before.Length || stamp != before.LastWriteTimeUtc) throw new OperationException("file_changed", "File changed during verification.");
        return hash;
    }
}
