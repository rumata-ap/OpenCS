using CSTriangulation;

namespace CSfea.Torsion;

/// <summary>Фасад диспетчеризации решателей кручения по методу.</summary>
public static class TorsionSolver
{
    /// <summary>
    /// Решает задачу кручения выбранным методом. Решатель нейтрален к единицам
    /// (работает в единицах входного контура). <paramref name="femOrder"/> игнорируется для МГЭ.
    /// </summary>
    public static TorsionProps Solve(TorsionBoundary boundary, TorsionMethod method,
        double elementSize, TriangulationMethod triangulation = TriangulationMethod.AdvancingFront,
        FemElementOrder femOrder = FemElementOrder.Linear,
        CancellationToken ct = default)
    {
        return method switch
        {
            TorsionMethod.Bem => TorsionBemSolver.Solve(boundary, elementSize),
            TorsionMethod.Fem => TorsionFemSolver.Solve(boundary, elementSize, triangulation, femOrder),
            _ => throw new ArgumentOutOfRangeException(nameof(method))
        };
    }
}
