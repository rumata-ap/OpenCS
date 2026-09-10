using CScore.Fem;

namespace CScore.Submodel;

/// <summary>Проект автономной схемы, построенный без записи в базу данных.</summary>
public sealed record SubmodelExtractionDraft(
    int ParentSchemaId,
    IReadOnlyList<FemMeshNode> MeshNodes,
    IReadOnlyList<FemElement> MeshElements,
    IReadOnlyList<SubmodelNodeDraft> Nodes,
    IReadOnlyList<SubmodelSegmentDraft> Segments,
    ResolvedTolerances Tolerances,
    ChainMetrics Metrics,
    IReadOnlyList<FemValidationDiagnostic> Diagnostics);

/// <summary>Снимок происхождения одного mesh-узла дочерней схемы.</summary>
public sealed record SubmodelNodeDraft(
    int ParentNodeId,
    string ParentNodeTag,
    string? SourceNodeTag,
    string? SourceMemberTag,
    FemMeshNode SubmodelNode);

/// <summary>Снимок происхождения одного упорядоченного сегмента дочерней цепочки.</summary>
public sealed record SubmodelSegmentDraft(
    int Ordinal,
    int ParentElementId,
    string ParentElementTag,
    string? SourceMemberTag,
    bool IsReversed,
    double StartStationM,
    double EndStationM,
    double LengthM,
    double AngleToAxisDeg,
    double BetaDeg,
    BetaSource BetaSource,
    FemElement SubmodelElement);

/// <summary>Результат чистой сборки проекта субмодели.</summary>
public sealed record SubmodelDraftBuildResult(
    SubmodelExtractionDraft? Draft,
    IReadOnlyList<FemValidationDiagnostic> Diagnostics)
{
    /// <summary>Признак отсутствия блокирующих диагностик и наличия проекта.</summary>
    public bool IsSuccess => Draft is not null && !Diagnostics.Any(x => x.IsError);
}
