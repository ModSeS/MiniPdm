using MiniPdm.Core;
using Xunit;
using static MiniPdm.Tests.TestContext;

namespace MiniPdm.Tests;

public sealed class ImportTests
{
    [Fact]
    public async Task DuplicateDesignations_RejectAllDuplicatesAndTheirParents()
    {
        using var c = new TestContext();
        var report = await c.Import(Part("a"), Part("b"), Assembly("root", "АБВГ.123456.002", new CadComponent("a", 1)),
            Part("good", "АБВГ.123456.003"));
        Assert.Equal(3, report.Rejected);
        Assert.Equal(1, report.Accepted);
        Assert.Single(c.Store.GetObjects());
    }

    [Fact]
    public async Task DuplicateStandardNames_RejectBoth()
    {
        using var c = new TestContext();
        var first = new CadDocument("a", ObjectType.StandardPart, null, "Болт", null, 0.1m, []);
        var report = await c.Import(first, first with { FileName = "b" });
        Assert.Equal(2, report.Rejected);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task NonPositiveQuantity_RejectsWholeAssembly(int quantity)
    {
        using var c = new TestContext();
        var report = await c.Import(Part(), Assembly("root", "АБВГ.123456.002", new CadComponent("part.m3d", quantity)));
        Assert.Equal(1, report.Rejected);
        Assert.Single(c.Store.GetObjects());
    }

    [Fact]
    public async Task QuantityOverflow_RejectsOnlyAssembly()
    {
        using var c = new TestContext();
        var report = await c.Import(Part(), Assembly("root", "АБВГ.123456.002", new CadComponent("part.m3d", int.MaxValue), new CadComponent("part.m3d", 1)));
        Assert.Equal(1, report.Rejected);
        Assert.Single(c.Store.GetObjects());
    }

    [Fact]
    public async Task UnreadableAndMissingReferences_PropagateToAllAncestors()
    {
        using var c = new TestContext();
        c.Cad.Unreadable.Add("bad");
        var report = await c.Import(Assembly("a", "АБВГ.123456.001", new CadComponent("bad", 1)),
            Assembly("b", "АБВГ.123456.002", new CadComponent("a", 1)), Assembly("missing", "АБВГ.123456.003", new CadComponent("absent", 1)), Part());
        // a и part имеют одинаковое обозначение; обе записи тоже должны быть отклонены.
        Assert.Equal(5, report.Rejected);
        Assert.Empty(c.Store.GetObjects());
    }

    [Fact]
    public async Task Cycles_RejectParticipantsAndAncestors_ButKeepIndependentObjects()
    {
        using var c = new TestContext();
        var report = await c.Import(Assembly("a", "АБВГ.123456.010", new CadComponent("b", 1)),
            Assembly("b", "АБВГ.123456.011", new CadComponent("a", 1)), Assembly("parent", "АБВГ.123456.012", new CadComponent("a", 1)), Part());
        Assert.Equal(3, report.Rejected);
        Assert.Equal(1, report.Accepted);
    }

    [Fact]
    public async Task SelfCycle_IsRejected()
    {
        using var c = new TestContext();
        Assert.Equal(1, (await c.Import(Assembly("a", "АБВГ.123456.001", new CadComponent("a", 1)))).Rejected);
    }

    [Fact]
    public async Task CancellationAfterFirstSave_RollsBackEntireFolder()
    {
        using var c = new TestContext();
        c.Cad.Documents = new[] { Part(), Part("other", "АБВГ.123456.002") }.ToDictionary(d => d.FileName);
        using var cancellation = new CancellationTokenSource();
        var progress = new ImmediateProgress(s => { if (s.StartsWith("Сохранение:")) cancellation.Cancel(); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => c.Importer.ImportAsync("virtual", progress, cancellation.Token));
        Assert.Empty(c.Store.GetObjects());
    }

    [Fact]
    public async Task ExceptionDuringSave_RollsBackEntireFolder()
    {
        using var c = new TestContext();
        c.Cad.Documents = new[] { Part(), Part("other", "АБВГ.123456.002") }.ToDictionary(d => d.FileName);
        int saved = 0;
        var progress = new ImmediateProgress(s => { if (s.StartsWith("Сохранение:") && ++saved == 2) throw new InvalidOperationException("simulated failure"); });
        await Assert.ThrowsAsync<InvalidOperationException>(() => c.Importer.ImportAsync("virtual", progress));
        Assert.Empty(c.Store.GetObjects());
    }

    [Fact]
    public async Task MissingStandardMass_IsError_ButMissingPartMassIsWarning()
    {
        using var c = new TestContext();
        var report = await c.Import(Part(mass: null), new("bolt", ObjectType.StandardPart, null, "Болт", null, null, []));
        Assert.Equal(1, report.Accepted); Assert.Equal(1, report.Rejected); Assert.Equal(1, report.Warnings);
    }

    private sealed class ImmediateProgress(Action<string> action) : IProgress<string>
    {
        public void Report(string value) => action(value);
    }
}
