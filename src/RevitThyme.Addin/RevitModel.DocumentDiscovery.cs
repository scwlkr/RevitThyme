using Autodesk.Revit.DB;
using RevitThyme.Core;

namespace RevitThyme.Addin;

public sealed partial class RevitModel
{
    public IEnumerable<OpenDocumentInfo> ReadDocuments()
    {
        foreach (Document document in app.Application.Documents)
        {
            if (!host.CanDiscoverDocument(document)) continue;
            yield return new(host.DocumentId(document), document.Title, document.PathName,
                document.Equals(app.ActiveUIDocument?.Document), document.IsModified, document.IsFamilyDocument,
                document.IsLinked, document.IsWorkshared, document.IsReadOnly);
        }
    }
}
