using Microsoft.Data.Sqlite;
using MiniPdm.Core;
using MiniPdm.Infrastructure;

namespace MiniPdm.Tests;

// Настоящая реляционная БД в памяти + подмена CAD: тесты бизнес-логики не читают файлы.
public sealed class TestContext : IDisposable
{
    private readonly SqliteConnection keeper;
    public SqlitePdmStore Store { get; }
    public FakeCad Cad { get; } = new();
    public ImportService Importer { get; }
    public TestContext()
    {
        var connection = $"Data Source=test-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        keeper = new(connection);
        keeper.Open();
        Store = new(connection);
        Store.Initialize();
        Importer = new(Cad, Cad, Store);
    }
    public Task<ImportReport> Import(params CadDocument[] documents)
    {
        Cad.Documents = documents.ToDictionary(d => d.FileName);
        return Importer.ImportAsync("virtual-cad");
    }
    public ObjectCard Card(string name) => Store.GetObjects().Single(o => o.Name == name);
    public void Dispose() => keeper.Dispose();

    public static CadDocument Part(string file = "part.m3d", string designation = "АБВГ.123456.001", decimal? mass = 2m) =>
        new(file, ObjectType.Part, designation, file, "Сталь", mass, []);
    public static CadDocument Assembly(string file, string designation, params CadComponent[] components) =>
        new(file, ObjectType.Assembly, designation, file, null, null, components);
}

public sealed class FakeCad : ICadCatalog, ICadDocumentReader
{
    public Dictionary<string, CadDocument> Documents { get; set; } = [];
    public HashSet<string> Unreadable { get; } = [];
    public Task<IReadOnlyList<CadSource>> ListAsync(string location, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<CadSource>>(Documents.Keys.Concat(Unreadable).Distinct().Select(f => new CadSource(f, f)).ToArray());
    public Task<CadDocument> ReadAsync(string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (Unreadable.Contains(path)) throw new CadReadException("Документ повреждён");
        return Task.FromResult(Documents[path]);
    }
}
