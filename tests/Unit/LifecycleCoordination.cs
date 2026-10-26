using RevitThyme.Core;

namespace RevitThyme.Unit;

// These adapters model the interface, not Autodesk Revit. Native qualification is separate.
internal static class LifecycleCoordination
{
    private static readonly string OriginalHash = new('a', 64), ChangedHash = new('b', 64);

    public static void Run(Action<string, bool> check)
    {
        var open = new Runtime();
        var opened = open.Change(OperationKind.OpenDocument, open.Request(open.C));
        check("background open creates exactly one document without activation or save", opened is { Performed: true, IsActive: false, IsModified: false } &&
            opened.Path == open.C.PathName && opened.Sha256 == OriginalHash && open.Opens == 1 && open.Activates == 0 && open.C.Saves == 0 && open.Header.Disposed);
        var existing = new Runtime();
        check("opening an existing clean document is a no-op", !existing.Change(OperationKind.OpenDocument, existing.Request(existing.B)).Performed && existing.Opens == 0);
        var activate = new Runtime();
        check("activation changes only the explicit active document", activate.Change(OperationKind.ActivateDocument, activate.Request(activate.B)) is { Performed: true, IsActive: true }
            && activate.Active == activate.B && activate.Activates == 1);
        var noActivate = new Runtime();
        check("already-active activation is a no-op", !noActivate.Change(OperationKind.ActivateDocument, noActivate.Request(noActivate.A)).Performed && noActivate.Activates == 0);
        var save = new Runtime(); save.A.IsModified = true;
        var saved = save.Change(OperationKind.SaveDocument, save.Request(save.A));
        check("dirty active save returns the resulting file hash", saved is { Performed: true, IsModified: false } && saved.Sha256 == ChangedHash && save.A.Saves == 1);
        var clean = new Runtime();
        check("clean save never calls the native save", !clean.Change(OperationKind.SaveDocument, clean.Request(clean.A)).Performed && clean.A.Saves == 0);
        var backgroundClose = new Runtime();
        var closed = backgroundClose.Change(OperationKind.CloseDocument, backgroundClose.Request(backgroundClose.B));
        check("background close uses no activation or implicit save", closed is { Performed: true, IsActive: false, IsModified: false } && backgroundClose.B.Closes == 1 &&
            !backgroundClose.OpenDocuments.Contains(backgroundClose.B) && backgroundClose.Activates == 0 && !backgroundClose.B.SaveOnClose);
        var close = new Runtime();
        var result = close.Change(OperationKind.CloseDocument, close.Request(close.A) with { ActivateBeforeClose = close.Target(close.B) });
        check("active close activates and verifies the explicit replacement", result.DocumentId == close.A.Id && close.Active == close.B && close.A.Closes == 1 && close.Activates == 1);
        var dirty = new Runtime(); dirty.A.IsModified = true;
        Reject("dirty close is refused before missing activation", dirty, OperationKind.CloseDocument, dirty.Request(dirty.A), "document_dirty");
        var blocked = new Runtime(); blocked.A.IsModifiable = true;
        Reject("active edit scope is refused before file access", blocked, OperationKind.OpenDocument, blocked.Request(blocked.C), "lifecycle_blocked");
        check("active edit refusal preserves path-before-file validation order", blocked.Events.SequenceEqual(["scope:" + blocked.C.PathName]));
        var wrong = new Runtime(); wrong.B.IsLinked = true;
        Reject("wrong target ID is refused before target document kind", wrong, OperationKind.ActivateDocument, wrong.Request(wrong.B) with { DocumentId = "wrong" }, "wrong_document");
        var readOnly = new Runtime(); readOnly.A.IsReadOnlyFile = true;
        Reject("read-only file save remains a distinct preflight refusal", readOnly, OperationKind.SaveDocument, readOnly.Request(readOnly.A), "read_only_file");
        var format = new Runtime(); format.Header.Format = "2026"; format.B.IsModified = true;
        Reject("file format refusal precedes already-open dirty refusal", format, OperationKind.OpenDocument, format.Request(format.B), "unsupported_document");
        check("file metadata is disposed when preflight rejects", format.Header.Disposed && format.Opens == 0);
        var changed = new Runtime(); changed.AfterActivate = () => changed.A.IsModified = true;
        Reject("activation callbacks dirtying the close target are unconfirmed before close", changed, OperationKind.CloseDocument,
            changed.Request(changed.A) with { ActivateBeforeClose = changed.Target(changed.B) }, "lifecycle_unconfirmed");
        check("failed callback recheck never closes or restores the target", changed.A.Closes == 0 && changed.Active == changed.B && changed.Activates == 1);
        var changedView = new Runtime(); changedView.AfterClose = () => changedView.B.ActiveViewId = "another-view";
        Reject("post-close activation view drift is unconfirmed", changedView, OperationKind.CloseDocument,
            changedView.Request(changedView.A) with { ActivateBeforeClose = changedView.Target(changedView.B) }, "lifecycle_unconfirmed");
        check("uncertain close is not retried", changedView.A.Closes == 1);
        var refusedClose = new Runtime(); refusedClose.B.CloseResult = false;
        Reject("native close refusal is unconfirmed", refusedClose, OperationKind.CloseDocument, refusedClose.Request(refusedClose.B), "lifecycle_unconfirmed");
        check("refused native close is attempted once", refusedClose.B.Closes == 1);
        var changedFile = new Runtime(); changedFile.AfterOpen = () => changedFile.Hashes[changedFile.C.PathName] = ChangedHash;
        Reject("open completion with changed saved bytes is unconfirmed", changedFile, OperationKind.OpenDocument, changedFile.Request(changedFile.C), "lifecycle_unconfirmed");

        void Reject(string name, Runtime runtime, OperationKind operation, DocumentLifecycleRequest options, string code)
        {
            string? actual = null;
            try { runtime.Change(operation, options); } catch (OperationException error) { actual = error.Code; }
            check(name, actual == code);
        }
    }

    private sealed class Header : ILifecycleFileInfo
    {
        public bool IsWorkshared { get; set; }
        public string Format { get; set; } = "2027";
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }

    private sealed class Runtime : IDocumentLifecycleRuntime
    {
        public Doc A { get; } = new("a");
        public Doc B { get; } = new("b");
        public Doc C { get; } = new("c");
        public List<Doc> OpenDocuments { get; }
        public Doc? Active { get; set; }
        public ILifecycleDocument? ActiveDocument => Active;
        public IEnumerable<ILifecycleDocument> Documents => OpenDocuments;
        public Dictionary<string, string> Hashes { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> Events { get; } = [];
        public Header Header { get; } = new();
        public int Opens { get; private set; }
        public int Activates { get; private set; }
        public Action? AfterOpen { get; set; }
        public Action? AfterActivate { get; set; }
        public Action? AfterClose { get; set; }
        public Runtime()
        {
            OpenDocuments = [A, B]; Active = A;
            foreach (var doc in new[] { A, B, C })
            {
                Hashes.Add(doc.PathName, OriginalHash);
                doc.SaveAction = () => { Hashes[doc.PathName] = ChangedHash; doc.IsModified = false; };
                doc.CloseAction = () => { if (doc.CloseResult) OpenDocuments.Remove(doc); AfterClose?.Invoke(); };
            }
        }
        public DocumentLifecycleRequest Request(Doc doc) => new(doc.PathName, doc.Id, Hashes[doc.PathName]);
        public CloseActivationTarget Target(Doc doc) => new(doc.Id, doc.PathName, Hashes[doc.PathName]);
        public DocumentLifecycleResult Change(OperationKind operation, DocumentLifecycleRequest options) => new DocumentLifecycleCoordinator(this).ChangeDocument(operation, options);
        public void AssertPath(string path) => Events.Add("scope:" + path);
        public void AssertLocalFile(string path) => Events.Add("local:" + path);
        public string Hash(string path) { Events.Add("hash:" + path); return Hashes[path]; }
        public ILifecycleFileInfo ReadFileInfo(string path) => Header;
        public ILifecycleDocument Open(string path) { Opens++; OpenDocuments.Add(C); AfterOpen?.Invoke(); return C; }
        public ILifecycleDocument Activate(string path) { Activates++; Active = OpenDocuments.Single(d => d.PathName == path); AfterActivate?.Invoke(); return Active; }
    }

    private sealed class Doc(string id) : ILifecycleDocument
    {
        public string Id { get; set; } = id;
        public string PathName { get; set; } = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "lifecycle-policy-" + id + ".rvt"));
        public string Title => Id;
        public string ActiveViewId { get; set; } = "view";
        public bool IsValidObject { get; set; } = true;
        public bool IsLinked { get; set; }
        public bool IsFamilyDocument { get; set; }
        public bool IsWorkshared { get; set; }
        public bool IsReadOnly { get; set; }
        public bool IsModifiable { get; set; }
        public bool IsReadOnlyFile { get; set; }
        public bool IsModified { get; set; }
        public int Saves { get; private set; }
        public int Closes { get; private set; }
        public bool SaveOnClose { get; private set; }
        public bool CloseResult { get; set; } = true;
        public Action? SaveAction { get; set; }
        public Action? CloseAction { get; set; }
        public bool Matches(ILifecycleDocument? other) => ReferenceEquals(this, other);
        public void Save() { Saves++; SaveAction?.Invoke(); }
        public bool Close(bool saveModified) { Closes++; SaveOnClose = saveModified; CloseAction?.Invoke(); return CloseResult; }
    }
}
