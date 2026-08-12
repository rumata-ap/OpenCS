namespace OpenCS.OpenSees.Structural;

/// <summary>Результат одного шага нагрузки нелинейного расчёта. Для несошедшегося шага
/// (Converged=false) списки перемещений/реакций/усилий пусты — состояние не было закоммичено.</summary>
public sealed record FemNonlinearStepResult(
    int StepIndex,
    double LoadFactor,
    bool Converged,
    IReadOnlyList<FemNodeDisplacement> Displacements,
    IReadOnlyList<FemNodeReaction> Reactions,
    IReadOnlyList<FemElementEndForces> ElementForces)
{
    /// <summary>Признак шага, выполненного при уточнении последнего неудачного интервала.</summary>
    public bool IsRefinement { get; init; }
    /// <summary>Индекс стадии нагружения (0-based), к которой относится этот шаг.</summary>
    public int StageIndex { get; init; }
    /// <summary>Причина остановки — заполняется только когда Converged=false (настоящий
    /// отказ analyze() после исчерпания backoff). Известные значения: "no_convergence",
    /// "min_increment_reached", "min_arclength_reached"; незнакомые строки (будущие версии
    /// генератора) не отклоняются — см. FemNonlinearResultParser.</summary>
    public string? StopReason { get; init; }
}

/// <summary>Типизированный результат нелинейного расчёта FEM-схемы — полная история шагов.</summary>
public sealed class FemNonlinearResult
{
    public string Status { get; init; } = "created";
    public IReadOnlyList<FemNonlinearStepResult> Steps { get; init; } = [];
    public IReadOnlyList<string> Diagnostics { get; init; } = [];
    public string? ArtifactDirectory { get; init; }
    /// <summary>Была ли обнаружена несходимость после уточнения шага.</summary>
    public bool LimitReached { get; init; }
    /// <summary>Коэффициент последнего успешно сошедшегося шага.</summary>
    public double LastConvergedLoadFactor { get; init; }
    /// <summary>Коэффициент первой неуспешной попытки.</summary>
    public double? FailedLoadFactor { get; init; }
    /// <summary>Количество частей для уточнения неудачного шага.</summary>
    public int RefinementDivisions { get; init; }
    /// <summary>Имя CalcType, нужное для выбора диаграмм при визуализации сечения.</summary>
    public string CalcTypeName { get; init; } = "C";
    /// <summary>Имя файла состояний фибр в каталоге артефактов.</summary>
    public string? FiberStateFileName { get; init; }
    /// <summary>Имя файла порядка сечений и точек интегрирования.</summary>
    public string? SectionOrderFileName { get; init; }
    /// <summary>Имена стадий нагружения в порядке приложения — по индексу совпадает с
    /// FemNonlinearStepResult.StageIndex. Пусто для результатов, сериализованных до появления
    /// стадийного нагружения.</summary>
    public IReadOnlyList<string> StageTags { get; init; } = [];

    /// <summary>Настройки способа управления траекторией по стадиям — по индексу совпадает
    /// со StageTags. Пусто для результатов, сериализованных до появления этой фичи (см.
    /// FemAnalysisResultVM — код читающий это поле обязан считать пустой/короткий список
    /// эквивалентом "неизвестно", не бросать IndexOutOfRangeException).</summary>
    public IReadOnlyList<FemPathControlSettings?> StagePathControls { get; init; } = [];

    /// <summary>Моменты фактического переключения LoadControl → continuation-режим.
    /// Пусто, если ни одна стадия не переключалась (или результат сериализован до
    /// появления фичи).</summary>
    public IReadOnlyList<FemPathControlSwitch> PathControlSwitches { get; init; } = [];

    /// <summary>Типизированная причина завершения каждой стадии — ровно по одной записи
    /// на каждый индекс стадии модели при штатном завершении расчёта.</summary>
    public IReadOnlyList<FemStageCompletion> StageCompletions { get; init; } = [];
}

/// <summary>Момент фактического переключения LoadControl → continuation-режим внутри
/// стадии. Не хранит узел/DOF — они полностью определяются
/// FemNonlinearResult.StagePathControls[StageIndex].ContinueWith...; AtStepIndex —
/// глобальный 1-based индекс первого шага, фактически выполненного continuation-
/// процедурой (значение stepIndex+1 в момент непосредственно перед вызовом). В редком
/// edge-case (continuation-узел уже на целевом перемещении в момент переключения) под
/// этим индексом строки в step_status.out может не быть — не ошибка, см.
/// FemAnalysisResultVM.</summary>
public sealed record FemPathControlSwitch(int StageIndex, int AtStepIndex);

/// <summary>Типизированная причина завершения одной стадии. Известные "успешные" значения
/// перечислены в FemNonlinearAnalysisService.SuccessfulStageReasons; всё остальное
/// (включая "failed", "not_run_due_to_previous_failure" и незнакомые будущие строки) —
/// не считается успехом.</summary>
public sealed record FemStageCompletion(int StageIndex, string Reason);
