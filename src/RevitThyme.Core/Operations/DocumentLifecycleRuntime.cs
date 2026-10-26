namespace RevitThyme.Core;

// Properties remain lazy: lifecycle checks must retain native short-circuit and callback order.
public interface ILifecycleDocument
{
    string Id { get; }
    string PathName { get; }
    string Title { get; }
    string ActiveViewId { get; }
    bool IsValidObject { get; }
    bool IsLinked { get; }
    bool IsFamilyDocument { get; }
    bool IsWorkshared { get; }
    bool IsReadOnly { get; }
    bool IsModifiable { get; }
    bool IsReadOnlyFile { get; }
    bool IsModified { get; }
    bool Matches(ILifecycleDocument? other);
    bool Close(bool saveModified);
    void Save();
}

public interface ILifecycleFileInfo : IDisposable
{
    bool IsWorkshared { get; }
    string Format { get; }
}

public interface IDocumentLifecycleRuntime
{
    ILifecycleDocument? ActiveDocument { get; }
    IEnumerable<ILifecycleDocument> Documents { get; }
    void AssertPath(string path);
    void AssertLocalFile(string path);
    string Hash(string path);
    ILifecycleFileInfo ReadFileInfo(string path);
    ILifecycleDocument? Open(string path);
    ILifecycleDocument Activate(string path);
}
