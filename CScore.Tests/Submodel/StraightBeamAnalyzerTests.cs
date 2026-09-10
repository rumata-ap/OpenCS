using CScore.Fem;
using CScore.Planar;
using CScore.Submodel;
using Xunit;

namespace CScore.Tests.Submodel;

public sealed class StraightBeamAnalyzerTests
{
    static StraightBeamChainAnalysis Analyze(IReadOnlyList<BeamSegmentInput> s, IReadOnlyList<EnvironmentElement>? e=null) => StraightBeamAnalyzer.Analyze(RigidTransformFixture.Apply(s),(e??[]).Select(RigidTransformFixture.Apply).ToList(),ChainTolerances.Default,null,null);
    [Fact] public void Analyze_StraightChain_IsExtractable() { var a=Analyze([RigidTransformFixture.Segment("a",0,1),RigidTransformFixture.Segment("b",1,2)]); Assert.Equal(ChainVerdict.Extractable,a.Verdict); Assert.Equal(2,a.Chain!.Segments.Count); }
    [Fact] public void Analyze_EmptySelection_IsNotExtractableWithoutThrowing() { var a=StraightBeamAnalyzer.Analyze([],[],ChainTolerances.Default,null,null); Assert.Equal(ChainVerdict.NotExtractable,a.Verdict); Assert.Contains(a.Diagnostics,d=>d.Code=="chain_input_empty"); }
    [Fact] public void Analyze_PrefersShortCleanCandidate_OverLongOneWithInternalAttachment()
    {
        var a=Analyze([RigidTransformFixture.Segment("l1",0,1),RigidTransformFixture.Segment("l2",1,2),RigidTransformFixture.Segment("l3",2,3),RigidTransformFixture.Segment("s1",10,11),RigidTransformFixture.Segment("s2",11,12)], [new("ext",EnvironmentElementKind.Beam,[new PlanarVector3(2,0,0),new PlanarVector3(2,1,0)])]);
        Assert.Equal(["s1","s2"],a.Chain!.Segments.Select(x=>x.Source.SourceKey)); Assert.Equal(ChainVerdict.Extractable,a.Verdict);
    }
    [Fact] public void Analyze_TwoCleanCandidates_ReportMultipleComponentsWarning() { var a=Analyze([RigidTransformFixture.Segment("a",0,2),RigidTransformFixture.Segment("b",50,51)]); Assert.Equal(ChainVerdict.ExtractableWithWarnings,a.Verdict); Assert.Contains(a.Diagnostics,d=>d.Code=="chain_multiple_components"); }
    [Fact] public void Analyze_PreflightErrorLowersVerdict() { var a=StraightBeamAnalyzer.Analyze([RigidTransformFixture.Segment("a",0,1)],[],ChainTolerances.Default,null,null,[new("chain_input_invalid","bad",true,["bad"])]); Assert.Equal(ChainVerdict.NotExtractable,a.Verdict); }
    [Fact] public void Analyze_GapWithinTolerance_IsBlocked() { var a=Analyze([RigidTransformFixture.Segment("a",0,1),RigidTransformFixture.Segment("b",1.005,2)]); Assert.Equal(ChainVerdict.NotExtractable,a.Verdict); Assert.Contains(a.Diagnostics,d=>d.Code=="chain_gap"); }
}
