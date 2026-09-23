using CScore.Fem;

namespace CScore.Submodel;

/// <summary>DOF, добавленный для фиксации жёстких мод: <c>sp</c> со значением перемещения родителя
/// поверх приложенной граничной силы. <see cref="Dof"/> — 0..5.</summary>
public sealed record GaugeDof(bool AtStart, string ChildNodeTag, int Dof, double Value);

/// <summary>
/// Сохраняемая сводка материализации (<c>plan_json</c>). Теги созданных узлов и стержней не хранятся:
/// по построению они равны тегам mesh-снимка извлечения. <see cref="Diagnostics"/> — полный список
/// диагностик материализации (диагностики сценария остаются в сценарии).
/// </summary>
public sealed record SubmodelMaterializationSummary(
    int ExtractionId,
    int RankBefore,
    IReadOnlyList<GaugeDof> GaugeDofs,
    int NodeCount,
    int MemberCount,
    int NodeLoadCount,
    int MemberLoadCount,
    int KinematicLoadCount,
    IReadOnlyList<FemValidationDiagnostic> Diagnostics);

/// <summary>
/// Конструктивный слой дочерней схемы, построенный из граничного сценария. Объекты имеют временные
/// <c>Id</c> (узлы 1..n, стержни 1..m, загружение 1), на которые ссылаются нагрузки; при сохранении
/// <c>DatabaseService</c> перенумеровывает ссылки. Mesh-копии — с переписанными <c>Source*</c>-тегами.
/// </summary>
public sealed record SubmodelMaterializationPlan(
    IReadOnlyList<FemNode> Nodes,
    IReadOnlyList<FemMember> Members,
    FemLoadCase LoadCase,
    IReadOnlyList<FemNodeLoad> NodeLoads,
    IReadOnlyList<FemMemberLoad> MemberLoads,
    IReadOnlyList<FemKinematicLoad> KinematicLoads,
    FemAnalysis LinearAnalysis,
    IReadOnlyList<FemMeshNode> MeshNodes,
    IReadOnlyList<FemElement> MeshElements,
    SubmodelMaterializationSummary Summary);

/// <summary>Результат планирования. <see cref="Diagnostics"/> — единственный полный список.</summary>
public sealed record SubmodelMaterializationBuild(
    SubmodelMaterializationPlan? Plan,
    IReadOnlyList<FemValidationDiagnostic> Diagnostics)
{
    /// <summary>План построен и блокирующих диагностик нет.</summary>
    public bool IsSuccess => Plan is not null && !Diagnostics.Any(d => d.IsError);
}

/// <summary>Сохранённая материализация граничного сценария.</summary>
public sealed record SubmodelMaterialization(int Id, int ScenarioId, SubmodelMaterializationSummary Summary, string Created);

/// <summary>Коды диагностик материализации, фиксации жёстких мод и линейной сверки.</summary>
public static class SubmodelMaterializationDiagnostics
{
    public const string ScenarioBlocked = "submodel_materialization_scenario_blocked";
    public const string MemberIncomplete = "submodel_materialization_member_incomplete";
    public const string MeshStale = "submodel_materialization_mesh_stale";
    public const string Stale = "submodel_materialization_stale";
    public const string LoadsUnknown = "submodel_materialization_loads_unknown";
    public const string GaugeUnavailable = "submodel_gauge_unavailable";
    public const string VerificationMismatch = "submodel_verification_mismatch";
    public const string Info = "submodel_materialization_info";
}
