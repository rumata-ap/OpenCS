using CScore.Planar;
using CScore.Submodel;
using Xunit;

namespace CScore.Tests.Submodel;

public sealed class ChainCoverageJoinTests
{
    static IReadOnlyList<ChainCandidate> Build(IReadOnlyList<BeamSegmentInput> input)
    {
        var segments = RigidTransformFixture.Apply(input);
        var tolerances = ChainTolerances.Default.Resolve(SegmentInputValidation.CharacteristicSize(segments));
        var clusters = NodeClusterBuilder.Build(segments, tolerances.NodeCoincidenceM);
        return ChainCoverageJoin.BuildCandidates(segments, clusters, SegmentGraph.Build(segments, clusters), null, tolerances);
    }

    [Fact]
    public void BuildCandidates_ContinuousChain_IsSingleExtractableCandidate()
    {
        var candidate = Assert.Single(Build([RigidTransformFixture.Segment("a", 0, 1), RigidTransformFixture.Segment("b", 1, 2)]));
        Assert.True(candidate.IsExtractable);
        Assert.False(candidate.HasGap);
    }

    [Fact]
    public void BuildCandidates_CollinearGapWithinGapTolerance_IsJoinedAndBlocked()
    {
        var joined = Assert.Single(Build([RigidTransformFixture.Segment("a", 0, 1), RigidTransformFixture.Segment("b", 1.005, 2)]));
        Assert.True(joined.HasGap);
        Assert.False(joined.IsExtractable);
        Assert.Contains(joined.Diagnostics, d => d.Code == "chain_gap" && d.IsError);
        Assert.Equal(2, joined.SegmentIndices.Count);
    }

    [Fact]
    public void BuildCandidates_ThreeCollinearFragments_AreJoinedIntoSingleCandidate()
    {
        var joined = Assert.Single(Build([
            RigidTransformFixture.Segment("a", 0, 1), RigidTransformFixture.Segment("b", 1.005, 2),
            RigidTransformFixture.Segment("c", 2.005, 3)]));
        Assert.True(joined.HasGap);
        Assert.Equal(3, joined.SegmentIndices.Count);
        Assert.Equal(2, joined.Diagnostics.Count(d => d.Code == "chain_gap"));
    }

    [Fact]
    public void BuildCandidates_CollinearGapBeyondGapTolerance_StaysTwoIndependentCandidates()
    {
        var candidates = Build([RigidTransformFixture.Segment("a", 0, 1), RigidTransformFixture.Segment("b", 1.5, 2.5)]);
        Assert.Equal(2, candidates.Count);
        Assert.All(candidates, c => Assert.False(c.HasGap));
        Assert.All(candidates, c => Assert.True(c.IsExtractable));
    }

    [Fact]
    public void BuildCandidates_NonCollinearComponents_StayIndependent()
    {
        var candidates = Build([RigidTransformFixture.Segment("a", 0, 1),
            new("b", new PlanarVector3(0, 3, 0), new PlanarVector3(0, 4, 0), 0, BetaSource.Member, "m2")]);
        Assert.Equal(2, candidates.Count);
        Assert.All(candidates, c => Assert.False(c.HasGap));
    }

    [Fact]
    public void BuildCandidates_ParallelOffsetComponents_AreNotJoined()
    {
        var candidates = Build([RigidTransformFixture.Segment("a", 0, 1),
            new("b", new PlanarVector3(1.005, .5, 0), new PlanarVector3(2, .5, 0), 0, BetaSource.Member, "m2")]);
        Assert.Equal(2, candidates.Count);
    }

    [Fact]
    public void BuildCandidates_KeepsStructurallyInvalidComponentAsNonExtractable()
    {
        var candidates = Build([RigidTransformFixture.Segment("a", 0, 1), RigidTransformFixture.Segment("b", 1, 2),
            new("branch", new PlanarVector3(1, 0, 0), new PlanarVector3(1, 1, 0), 0, BetaSource.Member, "m1")]);
        var candidate = Assert.Single(candidates);
        Assert.False(candidate.IsExtractable);
        Assert.Contains(candidate.Diagnostics, d => d.Code == "chain_branching");
    }
}
