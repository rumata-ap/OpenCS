namespace OpenCS.OpenSees.Structural;

/// <summary>Перемещения shell-узла (м, рад).</summary>
public sealed record ShellNodeDisplacement(int NodeTag, double Ux, double Uy, double Uz, double Rx, double Ry, double Rz);

/// <summary>Реакции shell-узла (Н, Н·м).</summary>
public sealed record ShellNodeReaction(int NodeTag, double Fx, double Fy, double Fz, double Mx, double My, double Mz);

/// <summary>Глобальные узловые силы shell-элемента.</summary>
public sealed record ShellElementNodalForces(
    int ElementTag,
    ShellElementKind ElementKind,
    IReadOnlyList<double> Components);

/// <summary>Локальные section resultants shell-элемента в integration point.</summary>
public sealed record ShellSectionResultants(
    int ElementTag,
    int IntegrationPoint,
    double Nx,
    double Ny,
    double Nxy,
    double Mx,
    double My,
    double Mxy,
    double Qx,
    double Qy);

/// <summary>Результат одного шага нагрузки нелинейного shell-расчёта. Для несошедшегося шага
/// (Converged=false) списки пусты — состояние не было закоммичено.</summary>
public sealed record RCShellStepResult(
    int StepIndex,
    int StageIndex,
    double LoadFactor,
    bool Converged,
    IReadOnlyList<ShellNodeDisplacement> Displacements,
    IReadOnlyList<ShellNodeReaction> Reactions,
    IReadOnlyList<ShellElementNodalForces> ElementForces,
    IReadOnlyList<ShellSectionResultants> SectionResultants,
    IReadOnlyList<FemElementEndForces> BeamElementForces)
{
    /// <summary>Признак шага, выполненного при уточнении последнего неудачного интервала.</summary>
    public bool IsRefinement { get; init; }
}

/// <summary>Типизированный результат shell-расчёта OpenSees.</summary>
public sealed class ShellResult
{
    /// <summary>Статус по completion marker.</summary>
    public string Status { get; init; } = "created";

    /// <summary>Полная история шагов нагружения.</summary>
    public IReadOnlyList<RCShellStepResult> Steps { get; init; } = [];

    /// <summary>Перемещения узлов.</summary>
    public IReadOnlyList<ShellNodeDisplacement> Displacements { get; init; } = [];

    /// <summary>Реакции узлов.</summary>
    public IReadOnlyList<ShellNodeReaction> Reactions { get; init; } = [];

    /// <summary>Глобальные узловые силы shell-элементов.</summary>
    public IReadOnlyList<ShellElementNodalForces> ElementForces { get; init; } = [];

    /// <summary>Локальные resultants по integration points.</summary>
    public IReadOnlyList<ShellSectionResultants> SectionResultants { get; init; } = [];

    /// <summary>Диагностика parser-а.</summary>
    public IReadOnlyList<string> Diagnostics { get; init; } = [];

    /// <summary>Каталог выборочных material-state recorder-файлов.</summary>
    public ShellStateCatalog? StateCatalog { get; init; }

    /// <summary>Каталог артефактов расчёта.</summary>
    public string? ArtifactDirectory { get; init; }
}
