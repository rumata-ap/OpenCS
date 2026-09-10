using CScore.Planar;
using CScore.Submodel;
using Xunit;

namespace CScore.Tests.Submodel;

public sealed class ChainFrameConsistencyTests
{
    sealed class Provider : IBeamLocalFrameProvider
    {
        public BeamLocalFrame Frame(PlanarVector3 axis, double beta)
        {
            var x = axis.Normalize(); var reference = Math.Abs(x.Z) > .99996 ? new PlanarVector3(1,0,0) : new PlanarVector3(0,0,1);
            var y = (reference - x * reference.Dot(x)).Normalize(); var z = x.Cross(y);
            var r = beta * Math.PI / 180; var c = Math.Cos(r); var s = Math.Sin(r);
            return new BeamLocalFrame(y*c + x.Cross(y)*s + x*(x.Dot(y)*(1-c)), z*c + x.Cross(z)*s + x*(x.Dot(z)*(1-c)));
        }
    }
    static StraightBeamChain Chain(IReadOnlyList<BeamSegmentInput> segments)
    {
        var t=ChainTolerances.Default.Resolve(SegmentInputValidation.CharacteristicSize(segments)); var c=NodeClusterBuilder.Build(segments,t.NodeCoincidenceM); var g=SegmentGraph.Build(segments,c).Single();
        return ChainOrdering.OrderByTraversal(segments,c,g,ChainStraightnessCheck.Check(segments,c,g,t),null,t);
    }
    static readonly ResolvedTolerances Tol = ChainTolerances.Default.Resolve(2);
    [Fact] public void Check_SameBetaAlongChain_ProducesNoDiagnostic() => Assert.Empty(ChainFrameConsistency.Check(Chain([RigidTransformFixture.Segment("a",0,1,30),RigidTransformFixture.Segment("b",1,2,30)]),new Provider(),Tol));
    [Fact] public void Check_SamePhysicalSectionWithSwappedNodes_ProducesNoMismatch() => Assert.DoesNotContain(ChainFrameConsistency.Check(Chain([RigidTransformFixture.Segment("a",0,1,30),RigidTransformFixture.Segment("b",2,1,-30)]),new Provider(),Tol), d=>d.Code=="chain_frame_mismatch");
    [Fact] public void Check_DifferentBeta_ProducesWarningNotError()
    {
        var d=Assert.Single(ChainFrameConsistency.Check(Chain([RigidTransformFixture.Segment("a",0,1,0),RigidTransformFixture.Segment("b",1,2,45)]),new Provider(),Tol),d=>d.Code=="chain_frame_mismatch"); Assert.False(d.IsError);
    }
    [Fact] public void Check_MixedBetaSources_ProducesSeparateWarning()
    {
        var d=Assert.Single(ChainFrameConsistency.Check(Chain([RigidTransformFixture.Segment("a",0,1),new("b",new PlanarVector3(1,0,0),new PlanarVector3(2,0,0),0,BetaSource.Absent,null)]),new Provider(),Tol),d=>d.Code=="chain_beta_source_mixed"); Assert.False(d.IsError);
    }
    [Fact] public void Check_WithoutProvider_ReportsSkippedAsInformation() => Assert.False(Assert.Single(ChainFrameConsistency.Check(Chain([RigidTransformFixture.Segment("a",0,1),RigidTransformFixture.Segment("b",1,2)]),null,Tol),d=>d.Code=="chain_frame_check_skipped").IsError);
}
