namespace CScore.Submodel;
public static class ChainCandidateRanking
{
    public static IReadOnlyList<ChainCandidate> Rank(IReadOnlyList<ChainCandidate> candidates, ResolvedTolerances tolerances) => candidates.Where(c=>c.IsExtractable).OrderByDescending(c=>c.TotalLengthM).ThenByDescending(c=>c.SpanM).ThenBy(c=>c.Chain.Nodes.Count>0?c.Chain.Nodes[0].Point.X:0).ThenBy(c=>c.Chain.Nodes.Count>0?c.Chain.Nodes[0].Point.Y:0).ThenBy(c=>c.Chain.Nodes.Count>0?c.Chain.Nodes[0].Point.Z:0).ToList();
}
