using CScore.Planar;
using CScore.Submodel;
using Xunit;

namespace CScore.Tests.Submodel;

public sealed class RigidModeGaugeTests
{
    static readonly PlanarVector3 Start = new(0, 0, 0);
    static readonly PlanarVector3 End = new(6, 0, 0);

    static List<GaugeCandidate> Candidates(PlanarVector3 start, PlanarVector3 end) =>
        [.. Enumerable.Range(0, 6).Select(d => new GaugeCandidate(true, start, d)),
         .. Enumerable.Range(0, 6).Select(d => new GaugeCandidate(false, end, d))];

    [Fact]
    public void BothEndsForce_ClampsStartEnd()
    {
        var result = RigidModeGauge.Complete(Start, 6, [], Candidates(Start, End));

        Assert.Equal(0, result.RankBefore);
        Assert.True(result.Complete);
        Assert.Equal(6, result.Added.Count);
        Assert.All(result.Added, c => Assert.True(c.AtStart));
        Assert.Equal([0, 1, 2, 3, 4, 5], result.Added.Select(c => c.Dof));
    }

    [Fact]
    public void FullyConstrainedEnd_NeedsNoGauge()
    {
        var constrained = Enumerable.Range(0, 6).Select(d => new KinematicDofRef(End, d)).ToList();

        var result = RigidModeGauge.Complete(Start, 6, constrained, Candidates(Start, End));

        Assert.Equal(6, result.RankBefore);
        Assert.True(result.Complete);
        Assert.Empty(result.Added);
    }

    [Fact]
    public void PinnedBothEnds_AddsTorsionAboutAxisOnly()
    {
        var constrained = new List<KinematicDofRef>();
        foreach (var p in new[] { Start, End })
            for (int d = 0; d < 3; d++) constrained.Add(new KinematicDofRef(p, d));
        var candidates = new List<GaugeCandidate>
        {
            new(true, Start, 3), new(true, Start, 4), new(true, Start, 5),
            new(false, End, 3), new(false, End, 4), new(false, End, 5),
        };

        var result = RigidModeGauge.Complete(Start, 6, constrained, candidates);

        Assert.Equal(5, result.RankBefore);
        Assert.True(result.Complete);
        var added = Assert.Single(result.Added);
        Assert.True(added.AtStart);
        Assert.Equal(3, added.Dof);
    }

    [Fact]
    public void PinnedInclinedChain_TorsionCandidateSkipsDofOrthogonalToAxis()
    {
        // Ось вдоль (0,1,1): Rx перпендикулярен оси и кручения не фиксирует — берётся Ry.
        var end = new PlanarVector3(0, 3, 3);
        var constrained = new List<KinematicDofRef>();
        foreach (var p in new[] { Start, end })
            for (int d = 0; d < 3; d++) constrained.Add(new KinematicDofRef(p, d));
        var candidates = new List<GaugeCandidate> { new(true, Start, 3), new(true, Start, 4), new(true, Start, 5) };

        var result = RigidModeGauge.Complete(Start, end.Length, constrained, candidates);

        Assert.Equal(5, result.RankBefore);
        var added = Assert.Single(result.Added);
        Assert.Equal(4, added.Dof);
    }

    [Theory]
    [InlineData(0.3)]
    [InlineData(300)]
    public void ScaleInvariant(double length)
    {
        var start = new PlanarVector3(10, 20, 30);
        var end = start + new PlanarVector3(length, 0, 0);
        var constrained = new List<KinematicDofRef> { new(end, 1), new(end, 2) };

        var result = RigidModeGauge.Complete(start, length, constrained, Candidates(start, end));

        Assert.Equal(2, result.RankBefore);
        Assert.Equal([(true, 0), (true, 1), (true, 2), (true, 3)],
            result.Added.Select(c => (c.AtStart, c.Dof)));
    }

    [Fact]
    public void NotEnoughCandidates_IsIncomplete()
    {
        var result = RigidModeGauge.Complete(Start, 6, [], [new GaugeCandidate(true, Start, 0)]);

        Assert.False(result.Complete);
        Assert.Single(result.Added);
    }
}
