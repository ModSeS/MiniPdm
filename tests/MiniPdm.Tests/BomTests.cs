using MiniPdm.Core;
using Xunit;
using static MiniPdm.Tests.TestContext;

namespace MiniPdm.Tests;

public sealed class BomTests
{
    [Fact]
    public async Task SharedPart_QuantitiesMultiplyAlongPathsAndSumAcrossBranches()
    {
        using var c = new TestContext();
        var p = Part(mass: 0.1m);
        await c.Import(p, Assembly("sub", "АБВГ.123456.002", new CadComponent(p.FileName, 3)),
            Assembly("root", "АБВГ.123456.003", new CadComponent("sub", 4), new CadComponent(p.FileName, 5)));
        var bom = new BomService(c.Store);
        var row = Assert.Single(bom.GetSpecification(c.Card("root").Id));
        Assert.Equal(17, row.Quantity);
        Assert.Equal(1.7m, bom.GetMass(c.Card("root").Id));
        Assert.Equal(4, c.Store.GetTree(c.Card("root").Id).Count);
    }

    [Fact]
    public async Task MissingMass_StaysBlankInSpecification_AndFailsMassWithComponentName()
    {
        using var c = new TestContext();
        await c.Import(Part(mass: null), Assembly("root", "АБВГ.123456.002", new CadComponent("part.m3d", 2)));
        var bom = new BomService(c.Store);
        var row = Assert.Single(bom.GetSpecification(c.Card("root").Id));
        Assert.Null(row.UnitMassKg); Assert.Null(row.TotalMassKg);
        Assert.Contains("part.m3d", Assert.Throws<DomainException>(() => bom.GetMass(c.Card("root").Id)).Message);
    }

    [Fact]
    public async Task ChildWithoutActiveVersion_IsNotSilentlyOmitted()
    {
        using var c = new TestContext();
        await c.Import(Part(), Assembly("root", "АБВГ.123456.002", new CadComponent("part.m3d", 2)));
        c.Store.ChangeState(c.Card("part.m3d").VersionId!.Value, VersionState.Cancelled);
        Assert.Contains("Нет действующей версии", Assert.Throws<DomainException>(() => new BomService(c.Store).GetMass(c.Card("root").Id)).Message);
    }

    [Fact]
    public async Task ParentAutomaticallyUsesNewCurrentChildVersion()
    {
        using var c = new TestContext();
        var part = Part();
        await c.Import(part, Assembly("root", "АБВГ.123456.002", new CadComponent(part.FileName, 2)));
        var root = c.Card("root");
        c.Store.ChangeState(c.Card(part.Name).VersionId!.Value, VersionState.Approved);
        await c.Import(part with { MassKg = 5 });
        Assert.Equal(10m, new BomService(c.Store).GetMass(root.Id));
        Assert.Equal(root.VersionId, c.Card("root").VersionId);
    }

    [Fact]
    public async Task EmptyAssembly_HasZeroMass()
    {
        using var c = new TestContext();
        await c.Import(Assembly("root", "АБВГ.123456.002"));
        Assert.Equal(0m, new BomService(c.Store).GetMass(c.Card("root").Id));
    }

    [Fact]
    public void ArithmeticOverflow_IsReportedInsteadOfReturningApproximateQuantity()
    {
        var rows = Enumerable.Range(0, 5).Select(i => new TreeRow
        {
            ObjectId = i + 1, Type = i == 4 ? ObjectType.Part : ObjectType.Assembly,
            VersionId = i + 1, Quantity = int.MaxValue, MassKg = 1,
            Path = string.Concat(Enumerable.Range(0, i + 1).Select(n => "/" + n)) + "/",
            ParentPath = i == 0 ? "" : string.Concat(Enumerable.Range(0, i).Select(n => "/" + n)) + "/"
        }).ToArray();
        Assert.Throws<OverflowException>(() => BomService.Calculate(rows));
    }
}
