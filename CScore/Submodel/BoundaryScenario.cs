using System.Text.Json.Serialization;
using CScore.Fem;
using CScore.Planar;

namespace CScore.Submodel;

/// <summary>Итоговое состояние граничного сценария.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ScenarioStatus
{
    /// <summary>Нет ошибок, нагрузочная информация известна.</summary>
    Complete,
    /// <summary>Нет ошибок, но наличие нагрузок родителя неизвестно.</summary>
    Incomplete,
    /// <summary>Есть хотя бы одна блокирующая диагностика.</summary>
    Blocked
}

/// <summary>Полнота нагрузочной информации родителя.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum LoadCompleteness
{
    /// <summary>Нагрузки родителя известны (источник internal/opensees); их отсутствие — факт.</summary>
    Known,
    /// <summary>Источник не хранит нагрузки (импорт ЛИРА/SCAD и др.): нули не подставляются.</summary>
    Unknown
}

/// <summary>Режим граничного DOF.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DofMode
{
    /// <summary>Физическое закрепление родителя.</summary>
    Fixed,
    /// <summary>Задаётся граничная сила/момент.</summary>
    Force,
    /// <summary>Задаётся перемещение/поворот.</summary>
    Kinematic
}

/// <summary>Происхождение режима DOF.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DofSource { Auto, Override }

/// <summary>Качество перевода вклада в глобальную систему.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ConversionQuality { Exact }

/// <summary>Распределённая нагрузка, оставшаяся на сегменте субмодели. Доли — от узла i дочернего КЭ,
/// интенсивности — глобальные компоненты, Н/м.</summary>
public sealed record RetainedDistributedLoad(string ChildElementTag, double AOverL, double BOverL,
    PlanarVector3 QAtA, PlanarVector3 QAtB, int SourceMemberLoadId, string SourceMemberTag);

/// <summary>Сосредоточенная сила внутри сегмента субмодели; глобальные компоненты, Н.</summary>
public sealed record RetainedPointLoad(string ChildElementTag, double XOverL, PlanarVector3 Force,
    int SourceMemberLoadId, string SourceMemberTag);

/// <summary>Источник узловой нагрузки: <c>node_load</c> (SourceId = FemNode.Id; после свёртки
/// выражения нагрузки суммированы по узлу) или <c>member_point_load</c> (SourceId = FemMemberLoad.Id).</summary>
public sealed record NodalLoadSource(string Kind, int SourceId, string? SourceTag);

/// <summary>Узловая нагрузка во внутреннем узле цепочки; глобальные компоненты.</summary>
public sealed record RetainedNodalLoad(string ChildNodeTag, Dof6 Load, NodalLoadSource Source);

/// <summary>Заданное перемещение во внутреннем узле цепочки. <see cref="Dof"/> — 0..5.</summary>
public sealed record RetainedKinematicLoad(string ChildNodeTag, int Dof, double Value, int SourceNodeId);

/// <summary>Учёт нагрузок родителя по принадлежности.</summary>
public sealed record LoadAccounting(int Retained, int Boundary, int Discarded);

/// <summary>Режим и значение одного граничного DOF. Value — сила/момент для Force,
/// перемещение/поворот для Kinematic, null для Fixed.</summary>
public sealed record DofAssignment(DofMode Mode, double? Value, DofSource Source);

/// <summary>Вклад в граничное действие на конце; <see cref="Action"/> — глобальные сила и момент.</summary>
public sealed record InterfaceActionContribution(Dof6 Action, string SourceType, string SourceTag,
    string SignConvention, ConversionQuality Quality);

/// <summary>Сверка граничного вектора с контрольным остатком выбранной части.</summary>
public sealed record ControlCheck(Dof6? PControl, double ForceMismatch, double MomentMismatch,
    bool Available, bool Passed);

/// <summary>Один конец цепочки: 6 DOF, граничный вектор, вклады и данные родителя.</summary>
public sealed record ScenarioEnd(
    bool AtStart,
    string ChildNodeTag,
    string ParentNodeTag,
    IReadOnlyList<DofAssignment> Dofs,
    Dof6? BoundaryVector,
    IReadOnlyList<InterfaceActionContribution> Contributions,
    Dof6? Reaction,
    Dof6? Displacement,
    ControlCheck Control);

/// <summary>Ручное переопределение режима DOF. <see cref="Dof"/> — 0..5.</summary>
public sealed record DofOverride(bool AtStart, int Dof, DofMode Mode);

/// <summary>
/// Неизменяемый граничный сценарий извлечённой цепочки при λ = ReferenceScale:
/// что осталось на цепочке, что действует на её концы и в каком режиме задан каждый DOF.
/// </summary>
public sealed record BoundaryScenario(
    int ExtractionId,
    double ReferenceScale,
    ScenarioStatus Status,
    LoadCompleteness LoadCompleteness,
    IReadOnlyList<RetainedDistributedLoad> RetainedDistributedLoads,
    IReadOnlyList<RetainedPointLoad> RetainedPointLoads,
    IReadOnlyList<RetainedNodalLoad> RetainedNodalLoads,
    IReadOnlyList<RetainedKinematicLoad> RetainedKinematicLoads,
    LoadAccounting LoadAccounting,
    IReadOnlyList<ScenarioEnd> Ends,
    IReadOnlyList<FemValidationDiagnostic> Diagnostics);

/// <summary>Сохранённый граничный сценарий извлечения.</summary>
public sealed record SubmodelBoundaryScenario(int Id, int ExtractionId, int Ordinal, ScenarioStatus Status,
    LoadCompleteness LoadCompleteness, BoundaryScenario Scenario, string Created);

/// <summary>Коды диагностик граничного сценария.</summary>
public static class BoundaryScenarioDiagnostics
{
    public const string ParentResultNotLinear = "submodel_boundary_parent_result_not_linear";
    public const string ParentResultInvalid = "submodel_boundary_parent_result_invalid";
    public const string LoadUnresolved = "submodel_boundary_load_unresolved";
    public const string MissingEndForces = "submodel_boundary_missing_end_forces";
    public const string UnsupportedJunction = "submodel_boundary_unsupported_junction";
    public const string DofUndetermined = "submodel_boundary_dof_undetermined";
    public const string OverrideInvalid = "submodel_boundary_override_invalid";
    public const string ExtractionStale = "submodel_boundary_extraction_stale";
    public const string ExtractionInvalid = "submodel_boundary_extraction_invalid";
    public const string LoadsUnknown = "submodel_boundary_loads_unknown";
    public const string ControlMismatch = "submodel_boundary_control_mismatch";
    public const string GaugeRequired = "submodel_boundary_gauge_required";
    public const string Info = "submodel_boundary_info";
}
