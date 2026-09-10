using CScore.Fem;

namespace CScore.Submodel;
public enum ChainVerdict { Extractable, ExtractableWithWarnings, NotExtractable }
public sealed record MetricPeak(double Value, string SourceKey);
public sealed record ChainMetrics(MetricPeak? MaxNodeOffsetFromAxis, MetricPeak? MaxSegmentAngleDeg, MetricPeak? MaxOverlapM, MetricPeak? MaxGapM, MetricPeak? MaxClusterDiameterM, int SegmentsWithLinearAngleCriterion, int CandidatesConsidered);
public sealed record ChainCandidateSummary(IReadOnlyList<string> SegmentKeys, double TotalLengthM, double SpanM, bool IsExtractable, IReadOnlyList<FemValidationDiagnostic> Diagnostics);
public sealed record StraightBeamChainAnalysis(ChainVerdict Verdict, StraightBeamChain? Chain, IReadOnlyList<ChainCandidateSummary> OtherCandidates, IReadOnlyList<FemValidationDiagnostic> Diagnostics, ResolvedTolerances Tolerances, ChainMetrics Metrics);
