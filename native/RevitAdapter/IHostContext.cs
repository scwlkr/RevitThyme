using Autodesk.Revit.DB;

namespace RevitThyme.RevitAdapter;
public interface IHostContext
{
    int ProcessId { get; }
    string StartTicks { get; }
    string SessionId { get; }
    uint Revision { get; }
    string DocumentId(Document document);
    void AssertDocument(Document document);
    void AssertWrite();
}
