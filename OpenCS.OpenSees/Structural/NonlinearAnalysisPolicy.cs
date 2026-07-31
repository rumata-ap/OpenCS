namespace OpenCS.OpenSees.Structural;

/// <summary>Общая политика нелинейного Newton-анализа — количество итераций, критерий
/// сходимости, алгоритм решателя, адаптивное дробление шага при отказе. Используется и
/// стержневой (FemNonlinearModel), и объединённой стержнево-оболочечной (ShellOpenSeesModel)
/// моделью — единая точка настройки нелинейного решателя.</summary>
public sealed record NonlinearAnalysisPolicy
{
    public double Tolerance { get; init; } = 1e-6;
    public int MaxIterations { get; init; } = 50;
    /// <summary>"EnergyIncr" (по умолчанию) | "NormUnbalance" | "NormDispIncr".</summary>
    public string ConvergenceTest { get; init; } = "EnergyIncr";
    /// <summary>"Newton" | "NewtonLineSearch" (по умолчанию).</summary>
    public string Algorithm { get; init; } = "NewtonLineSearch";
    /// <summary>На сколько частей делится неудавшийся интервал шага при отказе сходимости.</summary>
    public int RefinementDivisions { get; init; } = 10;
    /// <summary>Максимальная глубина рекурсивного дробления шага.</summary>
    public int MaxRefinementDepth { get; init; } = 4;

    public void Validate()
    {
        if (Tolerance <= 0) throw new InvalidOperationException("Допуск невязки должен быть положительным.");
        if (MaxIterations <= 0) throw new InvalidOperationException("Максимальное число итераций должно быть положительным.");
        if (ConvergenceTest is not ("EnergyIncr" or "NormUnbalance" or "NormDispIncr"))
            throw new InvalidOperationException($"Неизвестный критерий сходимости «{ConvergenceTest}».");
        if (Algorithm is not ("Newton" or "NewtonLineSearch"))
            throw new InvalidOperationException($"Неизвестный алгоритм решателя «{Algorithm}».");
        if (RefinementDivisions <= 0)
            throw new InvalidOperationException("Количество делений шага должно быть положительным.");
        if (MaxRefinementDepth <= 0)
            throw new InvalidOperationException("Максимальная глубина дробления шага должна быть положительной.");
    }
}
