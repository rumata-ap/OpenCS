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
    /// <summary>Крупный шаг коэффициента нагрузки.</summary>
    public double LoadFactorStep { get; init; }
    /// <summary>Защитный максимум коэффициента нагрузки.</summary>
    public double MaxLoadFactor { get; init; }
    /// <summary>Количество частей для уточнения неудачного шага.</summary>
    public int RefinementDivisions { get; init; }
    /// <summary>Имя CalcType, нужное для выбора диаграмм при визуализации сечения.</summary>
    public string CalcTypeName { get; init; } = "C";
    /// <summary>Имя файла состояний фибр в каталоге артефактов.</summary>
    public string? FiberStateFileName { get; init; }
    /// <summary>Имя файла порядка сечений и точек интегрирования.</summary>
    public string? SectionOrderFileName { get; init; }
}
