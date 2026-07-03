namespace CSfea.Torsion;

/// <summary>Фасад диспетчеризации решателей кручения по методу.</summary>
public static class TorsionSolver
{
    /// <summary>
    /// Решает задачу кручения выбранным методом. Решатель нейтрален к единицам
    /// (работает в единицах входного контура).
    /// </summary>
    public static TorsionProps Solve(TorsionBoundary boundary, TorsionMethod method,
        double elementSize, CancellationToken ct = default)
    {
        return method switch
        {
            TorsionMethod.Bem => TorsionBemSolver.Solve(boundary, elementSize),
            TorsionMethod.Fem => TorsionFemSolver.Solve(boundary, elementSize),
            _ => throw new ArgumentOutOfRangeException(nameof(method))
        };
    }
}
