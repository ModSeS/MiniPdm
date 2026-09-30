using MiniPdm.Core;
using Xunit;

namespace MiniPdm.Tests;

public sealed class RulesTests
{
    [Theory]
    [InlineData("АБВГ.301245.001", true)]
    [InlineData("АБВЁ.301245.001", true)]
    [InlineData("PДЦЛ.304112.601", false)]
    [InlineData("АБВГ.30124.001", false)]
    [InlineData("абвг.301245.001", false)]
    [InlineData("АБВГ.301245.001\n", false)]
    [InlineData("АБВГ.٣٠١٢٤٥.001", false)]
    [InlineData(null, false)]
    public void Designation_UsesExactCyrillicFormat(string? value, bool expected) => Assert.Equal(expected, Rules.IsDesignationValid(value));

    [Theory]
    [InlineData(VersionState.Draft, VersionState.Approved, true)]
    [InlineData(VersionState.Draft, VersionState.Cancelled, true)]
    [InlineData(VersionState.Approved, VersionState.Cancelled, true)]
    [InlineData(VersionState.Approved, VersionState.Draft, false)]
    [InlineData(VersionState.Cancelled, VersionState.Draft, false)]
    [InlineData(VersionState.Cancelled, VersionState.Approved, false)]
    [InlineData(VersionState.Draft, VersionState.Draft, false)]
    [InlineData(VersionState.Approved, VersionState.Approved, false)]
    [InlineData(VersionState.Cancelled, VersionState.Cancelled, false)]
    public void States_HaveOnlyForwardTransitions(VersionState from, VersionState to, bool expected) =>
        Assert.Equal(expected, Rules.CanTransition(from, to));

    [Fact]
    public void Cycles_DoNotConfuseSharedChildrenWithBackEdges()
    {
        var graph = new Dictionary<int, IReadOnlyList<int>> { [1] = [2, 3], [2] = [4], [3] = [4], [4] = [] };
        Assert.Empty(Graph.CycleNodes(graph));
        graph[4] = [2];
        Assert.True(Graph.CycleNodes(graph).SetEquals([2, 4]));
    }
}
