using Microsoft.Data.Sqlite;
using MiniPdm.Core;
using MiniPdm.Infrastructure;
using Xunit;

namespace MiniPdm.Tests;

public sealed class SampleTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SuppliedFolders_ImportAndVersionChangesMatchExpected(bool approve)
    {
        using var c = new TestContext();
        var reader = new JsonCadAdapter();
        var importer = new ImportService(reader, reader, c.Store);
        string samples = Path.Combine(AppContext.BaseDirectory, "samples");
        var first = await importer.ImportAsync(Path.Combine(samples, "cad-export"));
        Assert.Equal(45, first.Rows.Count);
        Assert.Equal(35, first.Accepted);
        Assert.Equal(10, first.Rejected);
        Assert.Equal(1, first.Warnings);
        long root = c.Store.GetObjects().Single(o => o.Designation == "РДЦЛ.304112.000").Id;
        var bom = new BomService(c.Store);
        decimal firstMass = bom.GetMass(root);
        var repeat = await importer.ImportAsync(Path.Combine(samples, "cad-export"));
        Assert.Equal(35, repeat.Rows.Count(r => r.Outcome == ImportOutcome.Unchanged));
        if (approve)
            foreach (var card in c.Store.GetObjects()) c.Store.ChangeState(card.VersionId!.Value, VersionState.Approved);
        var second = await importer.ImportAsync(Path.Combine(samples, "cad-export-v2"));
        Assert.Equal(35, second.Accepted);
        Assert.Equal(10, second.Rejected);
        Assert.Equal(33, second.Rows.Count(r => r.Outcome == ImportOutcome.Unchanged));
        Assert.Equal(2, second.Rows.Count(r => r.Outcome == (approve ? ImportOutcome.NewVersion : ImportOutcome.Updated)));
        Assert.Equal(firstMass + 0.076m, bom.GetMass(root));
        var sheet = bom.GetSpecification(root);
        Assert.Equal(16m, sheet.Single(r => r.Name == "Прокладка регулировочная").Quantity);
        Assert.Equal(22m, sheet.Single(r => r.Name == "Болт М8x25 ГОСТ 7798-70").Quantity);
    }

    [Fact]
    public async Task SqliteFile_SurvivesStoreRecreation()
    {
        string path = Path.Combine(Path.GetTempPath(), $"minipdm-{Guid.NewGuid():N}.db");
        string cs = $"Data Source={path};Pooling=False";
        try
        {
            var store = new SqlitePdmStore(cs);
            store.Initialize();
            var cad = new FakeCad { Documents = new[] { TestContext.Part() }.ToDictionary(d => d.FileName) };
            await new ImportService(cad, cad, store).ImportAsync("virtual");
            var reopened = new SqlitePdmStore(cs);
            reopened.Initialize();
            Assert.Single(reopened.GetObjects());
            Assert.Equal(2m, reopened.GetObjects()[0].MassKg);
        }
        finally { File.Delete(path); }
    }
}
