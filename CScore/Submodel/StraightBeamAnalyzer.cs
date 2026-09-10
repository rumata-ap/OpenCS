using CScore.Fem;
using CScore.Planar;
namespace CScore.Submodel;
public static class StraightBeamAnalyzer
{
    public static StraightBeamChainAnalysis Analyze(IReadOnlyList<BeamSegmentInput> segments, IReadOnlyList<EnvironmentElement> environment, ChainTolerances tolerances, PlanarVector3? preferredDirection, IBeamLocalFrameProvider? frameProvider, IReadOnlyList<FemValidationDiagnostic>? preflightDiagnostics=null)
    {
        var diagnostics=new List<FemValidationDiagnostic>(preflightDiagnostics??[]); var (valid,input)=SegmentInputValidation.Filter(segments); diagnostics.AddRange(input); var resolved=tolerances.Resolve(SegmentInputValidation.CharacteristicSize(valid));
        if(valid.Count==0){diagnostics.Add(new("chain_input_empty","В выборе нет ни одного пригодного стержневого элемента.",true,[])); return new(ChainVerdict.NotExtractable,null,[],diagnostics,resolved,new(null,null,null,null,null,0,0));}
        var clusters=NodeClusterBuilder.Build(valid,resolved.NodeCoincidenceM); diagnostics.AddRange(clusters.Diagnostics); var components=SegmentGraph.Build(valid,clusters);
        var candidates=ChainCoverageJoin.BuildCandidates(valid,clusters,components,preferredDirection,resolved).Select(candidate=>{
            if(candidate.Chain.Nodes.Count==0)return candidate; var scan=ChainEnvironmentScan.Scan(candidate,environment,resolved); var chain=candidate.Chain with {StartAttachments=scan.StartAttachments,EndAttachments=scan.EndAttachments}; return (candidate with {Chain=chain}).WithDiagnostics(scan.Diagnostics);}).ToList();
        var ranked=ChainCandidateRanking.Rank(candidates,resolved); var chosen=ranked.FirstOrDefault();
        if(chosen is not null && ranked.Count>1) diagnostics.Add(new("chain_multiple_components",$"Извлекаемых цепочек найдено {ranked.Count}; выбрана наибольшая по суммарной длине.",false,ranked.SelectMany(c=>c.SegmentKeys).ToList()));
        if(chosen is null)
        {
            diagnostics.AddRange(candidates.SelectMany(candidate => candidate.Diagnostics));
            diagnostics.Add(new("chain_no_valid_component","Ни одна из выбранных групп элементов не образует извлекаемую прямую цепочку.",true,valid.Select(s=>s.SourceKey).ToList()));
        }
        else {diagnostics.AddRange(chosen.Diagnostics); diagnostics.AddRange(ChainFrameConsistency.Check(chosen.Chain,frameProvider,resolved));}
        var verdict=chosen is null||diagnostics.Any(d=>d.IsError)?ChainVerdict.NotExtractable:
            diagnostics.Any(d=>d.Code != "chain_frame_check_skipped")?ChainVerdict.ExtractableWithWarnings:ChainVerdict.Extractable;
        var others=candidates.Where(c=>!ReferenceEquals(c,chosen)).Select(c=>new ChainCandidateSummary(c.SegmentKeys,c.TotalLengthM,c.SpanM,c.IsExtractable,c.Diagnostics)).ToList();
        return new(verdict,verdict==ChainVerdict.NotExtractable?null:chosen?.Chain,others,diagnostics,resolved,Metrics(clusters,candidates,chosen));
    }
    static ChainMetrics Metrics(NodeClusterSet clusters,IReadOnlyList<ChainCandidate> candidates,ChainCandidate? chosen)
    {
        MetricPeak? diameter=null; foreach(var c in clusters.Clusters)if(diameter is null||c.DiameterM>diameter.Value)diameter=new(c.DiameterM,c.SourceKeys.FirstOrDefault()??"");
        return new(null,null,null,null,diameter,0,candidates.Count);
    }
}
