using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitThyme.Core;

namespace RevitThyme.Addin;

public sealed partial class RevitModel
{
    public DocumentLifecycleResult ChangeDocument(OperationKind operation, DocumentLifecycleRequest options) =>
        new DocumentLifecycleCoordinator(new LifecycleRuntime(app, host)).ChangeDocument(operation, options);

    private sealed class LifecycleRuntime(UIApplication application, FoundationApplication owner) : IDocumentLifecycleRuntime
    {
        public ILifecycleDocument? ActiveDocument => Wrap(application.ActiveUIDocument?.Document);
        public IEnumerable<ILifecycleDocument> Documents => application.Application.Documents.Cast<Document>().Select(d => new LifecycleDocument(d, owner));
        public void AssertPath(string path) => owner.AssertLifecyclePath(path);
        public void AssertLocalFile(string path) => NativeLifecycleFiles.AssertLocal(path);
        public string Hash(string path) => NativeLifecycleFiles.Hash(path);
        public ILifecycleFileInfo ReadFileInfo(string path) => new LifecycleFileInfo(BasicFileInfo.Extract(path));
        public ILifecycleDocument? Open(string path) => Wrap(application.Application.OpenDocumentFile(path));
        public ILifecycleDocument Activate(string path) => new LifecycleDocument(application.OpenAndActivateDocument(path).Document, owner);
        private ILifecycleDocument? Wrap(Document? document) => document is null ? null : new LifecycleDocument(document, owner);
    }

    private sealed class LifecycleFileInfo(BasicFileInfo info) : ILifecycleFileInfo
    {
        public bool IsWorkshared => info.IsWorkshared;
        public string Format => info.Format;
        public void Dispose() => info.Dispose();
    }

    private sealed class LifecycleDocument(Document document, FoundationApplication owner) : ILifecycleDocument
    {
        private Document Native { get; } = document;
        public string Id => owner.DocumentId(Native);
        public string PathName => Native.PathName;
        public string Title => Native.Title;
        public string ActiveViewId => Native.ActiveView.UniqueId;
        public bool IsValidObject => Native.IsValidObject;
        public bool IsLinked => Native.IsLinked;
        public bool IsFamilyDocument => Native.IsFamilyDocument;
        public bool IsWorkshared => Native.IsWorkshared;
        public bool IsReadOnly => Native.IsReadOnly;
        public bool IsModifiable => Native.IsModifiable;
        public bool IsReadOnlyFile => Native.IsReadOnlyFile;
        public bool IsModified => Native.IsModified;
        public bool Matches(ILifecycleDocument? other) => other is LifecycleDocument peer && Native.Equals(peer.Native);
        public bool Close(bool saveModified) => Native.Close(saveModified);
        public void Save() => Native.Save();
    }
}
