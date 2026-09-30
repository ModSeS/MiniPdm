namespace MiniPdm.Core;

public interface ICadDocumentReader
{
    Task<CadDocument> ReadAsync(string path, CancellationToken ct);
}

// Перечисление документов тоже изолировано: Core не обращается к Directory или File.
public interface ICadCatalog
{
    Task<IReadOnlyList<CadSource>> ListAsync(string location, CancellationToken ct);
}

public interface IImportSession : IDisposable
{
    IReadOnlyList<PdmObject> LoadObjects();
    long CreateObject(CadDocument document);
    ImportOutcome Save(long objectId, CadDocument document, IReadOnlyList<BomLink> links);
    void EnsureAcyclic();
    void Commit();
}

public interface IPdmStore
{
    IImportSession BeginImport();
    IReadOnlyList<ObjectCard> GetObjects(string search = "");
    IReadOnlyList<TreeRow> GetTree(long rootId);
    void ChangeState(long versionId, VersionState target);
}
