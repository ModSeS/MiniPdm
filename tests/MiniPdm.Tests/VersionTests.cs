using MiniPdm.Core;
using Xunit;
using static MiniPdm.Tests.TestContext;

namespace MiniPdm.Tests;

public sealed class VersionTests
{
    [Fact]
    public async Task Reimport_IsIdempotent_UpdatesDraft_AndClonesApproved()
    {
        using var c = new TestContext();
        var part = Part();
        Assert.Equal(ImportOutcome.Created, (await c.Import(part)).Rows[0].Outcome);
        var original = c.Card(part.Name);
        Assert.Equal(ImportOutcome.Unchanged, (await c.Import(part)).Rows[0].Outcome);
        Assert.Equal(ImportOutcome.Updated, (await c.Import(part with { MassKg = 3 })).Rows[0].Outcome);
        Assert.Equal(original.VersionId, c.Card(part.Name).VersionId);
        c.Store.ChangeState(original.VersionId!.Value, VersionState.Approved);
        Assert.Equal(ImportOutcome.Unchanged, (await c.Import(part with { MassKg = 3 })).Rows[0].Outcome);
        Assert.Equal(ImportOutcome.NewVersion, (await c.Import(part with { MassKg = 4 })).Rows[0].Outcome);
        var second = c.Card(part.Name);
        Assert.Equal(2, second.VersionNumber); Assert.Equal(VersionState.Draft, second.State);
        c.Store.ChangeState(second.VersionId!.Value, VersionState.Cancelled);
        Assert.Equal(original.VersionId, c.Card(part.Name).VersionId);
        Assert.Equal(3m, c.Card(part.Name).MassKg);
        Assert.Equal(ImportOutcome.NewVersion, (await c.Import(part with { MassKg = 5 })).Rows[0].Outcome);
        Assert.Equal(3, c.Card(part.Name).VersionNumber);
    }

    [Fact]
    public async Task NameAndLinksBelongToVersion_OldApprovedCompositionIsPreserved()
    {
        using var c = new TestContext();
        var part = Part();
        var root = Assembly("root", "АБВГ.123456.002", new CadComponent(part.FileName, 2));
        await c.Import(part, root);
        var first = c.Card("root");
        c.Store.ChangeState(first.VersionId!.Value, VersionState.Approved);
        await c.Import(part, root with { Name = "new root", Components = [new CadComponent(part.FileName, 3)] });
        var second = c.Card("new root");
        Assert.Equal(6m, new BomService(c.Store).GetMass(second.Id));
        c.Store.ChangeState(second.VersionId!.Value, VersionState.Cancelled);
        Assert.Equal("root", c.Store.GetObjects().Single(o => o.Id == first.Id).Name);
        Assert.Equal(4m, new BomService(c.Store).GetMass(first.Id));
    }

    [Fact]
    public async Task AllVersionsCancelled_ReimportCreatesNextNumber()
    {
        using var c = new TestContext();
        await c.Import(Part());
        c.Store.ChangeState(c.Card("part.m3d").VersionId!.Value, VersionState.Cancelled);
        Assert.Null(c.Card("part.m3d").VersionId);
        await c.Import(Part());
        Assert.Equal(2, c.Card("part.m3d").VersionNumber);
    }

    [Fact]
    public async Task InvalidStateTransition_IsRejectedWithoutChanges()
    {
        using var c = new TestContext();
        await c.Import(Part());
        long versionId = c.Card("part.m3d").VersionId!.Value;
        c.Store.ChangeState(versionId, VersionState.Approved);
        Assert.Throws<DomainException>(() => c.Store.ChangeState(versionId, VersionState.Draft));
        Assert.Equal(VersionState.Approved, c.Card("part.m3d").State);
    }

    [Fact]
    public async Task ReorderedAndRepeatedLinks_AreSemanticallyUnchanged()
    {
        using var c = new TestContext();
        var p = Part(); var q = Part("q", "АБВГ.123456.003");
        var a = Assembly("a", "АБВГ.123456.002", new CadComponent(p.FileName, 2), new CadComponent(q.FileName, 1));
        await c.Import(p, q, a);
        c.Store.ChangeState(c.Card("a").VersionId!.Value, VersionState.Approved);
        var report = await c.Import(p, q, a with { Components = [new CadComponent(q.FileName, 1), new CadComponent(p.FileName, 1), new CadComponent(p.FileName, 1)] });
        Assert.All(report.Rows, r => Assert.Equal(ImportOutcome.Unchanged, r.Outcome));
    }

    [Fact]
    public async Task CancellingNewestVersion_CannotExposeCycleInOlderComposition()
    {
        using var c = new TestContext();
        var b = Assembly("b", "АБВГ.123456.002");
        var a = Assembly("a", "АБВГ.123456.001", new CadComponent("b", 1));
        await c.Import(a, b);
        c.Store.ChangeState(c.Card("a").VersionId!.Value, VersionState.Approved);
        a = a with { Components = [] };
        b = b with { Components = [new CadComponent("a", 1)] };
        await c.Import(a, b);
        long newestId = c.Card("a").VersionId!.Value;
        Assert.Throws<DomainException>(() => c.Store.ChangeState(newestId, VersionState.Cancelled));
        Assert.Equal(newestId, c.Card("a").VersionId);
    }
}
