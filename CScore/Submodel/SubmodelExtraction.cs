using CScore.Fem;

namespace CScore.Submodel;

/// <summary>Запрос создания автономной субмодели прямой стержневой цепочки.</summary>
public sealed record StraightBeamSubmodelRequest(string SubmodelTag, int ParentSchemaId,
    int ParentAnalysisId, int ExpectedParentResultId, SubmodelExtractionDraft Draft);

/// <summary>Сохранённое извлечение субмодели с неизменяемым provenance.</summary>
public sealed class SubmodelExtraction
{
    public int Id { get; init; }
    public int ParentSchemaId { get; init; }
    public int SubmodelSchemaId { get; init; }
    public int ParentAnalysisId { get; init; }
    public int ParentResultId { get; init; }
    public string LoadExpressionJson { get; init; } = "{}";
    public double ReferenceScale { get; init; }
    public ResolvedTolerances Tolerances { get; init; } = null!;
    public ChainMetrics Metrics { get; init; } = null!;
    public IReadOnlyList<FemValidationDiagnostic> Diagnostics { get; init; } = [];
    public IReadOnlyList<SubmodelExtractionNode> Nodes { get; init; } = [];
    public IReadOnlyList<SubmodelExtractionSegment> Segments { get; init; } = [];
}

/// <summary>Сохранённая связь mesh-узла субмодели с исходным узлом.</summary>
public sealed record SubmodelExtractionNode(int Id, int SubmodelNodeId, string SubmodelNodeTag,
    int ParentNodeId, string ParentNodeTag, double X, double Y, double Z,
    string? SourceNodeTag, string? SourceMemberTag);

/// <summary>Сохранённая связь сегмента субмодели с исходным mesh-элементом.</summary>
public sealed record SubmodelExtractionSegment(int Id, int Ordinal, int SubmodelElementId,
    string SubmodelElementTag, int ParentElementId, string ParentElementTag,
    string? SourceMemberTag, bool IsReversed, double StartStationM, double EndStationM,
    double LengthM, double AngleToAxisDeg, double BetaDeg, BetaSource BetaSource);
