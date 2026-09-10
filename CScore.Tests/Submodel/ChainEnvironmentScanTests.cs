using CScore.Planar;
using CScore.Submodel;
using Xunit;

namespace CScore.Tests.Submodel;

public sealed class ChainEnvironmentScanTests
{
    static (ChainCandidate Candidate, ResolvedTolerances Tolerances) Chain(IReadOnlyList<BeamSegmentInput> input)
    {
        var segments = RigidTransformFixture.Apply(input);
        var tolerances = ChainTolerances.Default.Resolve(SegmentInputValidation.CharacteristicSize(segments));
        var clusters = NodeClusterBuilder.Build(segments, tolerances.NodeCoincidenceM);
        return (ChainCoverageJoin.BuildCandidates(segments, clusters, SegmentGraph.Build(segments, clusters), null, tolerances).Single(), tolerances);
    }
    static EnvironmentElement Beam(string key, PlanarVector3 a, PlanarVector3 b) =>
        RigidTransformFixture.Apply(new EnvironmentElement(key, EnvironmentElementKind.Beam, [a, b]));
    static readonly IReadOnlyList<BeamSegmentInput> TwoSegments = [RigidTransformFixture.Segment("a", 0, 1), RigidTransformFixture.Segment("b", 1, 2)];

    [Fact]
    public void Scan_ExternalBeamAtChainEnd_IsRecordedAsAttachment()
    {
        var (candidate, tolerances) = Chain(TwoSegments);
        var result = ChainEnvironmentScan.Scan(candidate, [Beam("ext", new PlanarVector3(2, 0, 0), new PlanarVector3(2, 1, 0))], tolerances);
        Assert.DoesNotContain(result.Diagnostics, d => d.IsError);
        var attachment = Assert.Single(result.EndAttachments);
        Assert.Equal("ext", attachment.SourceKey);
        Assert.False(attachment.AtStart);
    }
    [Fact]
    public void Scan_ExternalBeamAtChainStart_IsRecordedOnStartSide()
    {
        var (candidate, tolerances) = Chain(TwoSegments);
        Assert.True(Assert.Single(ChainEnvironmentScan.Scan(candidate, [Beam("ext", new PlanarVector3(0,0,0), new PlanarVector3(0,-1,0))], tolerances).StartAttachments).AtStart);
    }
    [Fact]
    public void Scan_ExternalBeamAtInternalNode_IsBlocking()
    {
        var (candidate, tolerances) = Chain(TwoSegments);
        var result = ChainEnvironmentScan.Scan(candidate, [Beam("ext", new PlanarVector3(1,0,0), new PlanarVector3(1,1,0))], tolerances);
        Assert.Contains(result.Diagnostics, d => d.Code == "chain_internal_attachment" && d.IsError && d.SourceKeys!.Contains("ext"));
    }
    [Fact]
    public void Scan_ElementTouchingBothEndAndInternalNode_IsBlockingInAnyNodeOrder()
    {
        var (candidate, tolerances) = Chain(TwoSegments);
        foreach (var element in new [] { Beam("ext1", new PlanarVector3(2,0,0), new PlanarVector3(1,0,0)), Beam("ext2", new PlanarVector3(1,0,0), new PlanarVector3(2,0,0)) })
        {
            var result = ChainEnvironmentScan.Scan(candidate, [element], tolerances);
            Assert.Contains(result.Diagnostics, d => d.Code == "chain_internal_attachment" && d.IsError);
            Assert.Empty(result.EndAttachments);
        }
    }
    [Fact]
    public void Scan_ExternalNodeLyingOnSegmentWithoutSharedNode_IsWarningOnly()
    {
        var (candidate, tolerances) = Chain(TwoSegments);
        var result = ChainEnvironmentScan.Scan(candidate, [Beam("ext", new PlanarVector3(.5,0,0), new PlanarVector3(.5,1,0))], tolerances);
        Assert.False(Assert.Single(result.Diagnostics, d => d.Code == "chain_dangling_contact").IsError);
    }
    [Fact]
    public void Scan_DistantExternalBeam_IsIgnored()
    {
        var (candidate, tolerances) = Chain(TwoSegments);
        var result = ChainEnvironmentScan.Scan(candidate, [Beam("far", new PlanarVector3(50,0,0), new PlanarVector3(50,1,0))], tolerances);
        Assert.Empty(result.Diagnostics);
        Assert.Empty(result.StartAttachments);
        Assert.Empty(result.EndAttachments);
    }
}
