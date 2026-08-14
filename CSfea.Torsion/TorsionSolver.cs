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
        CancellationToken ct = default,
        double nu = 0.2)
    {
        return method switch
        {
            TorsionMethod.Bem => TorsionBemSolver.Solve(boundary, elementSize),
            TorsionMethod.Fem => TorsionFemSolver.Solve(boundary, elementSize, triangulation, femOrder, nu),
            _ => throw new ArgumentOutOfRangeException(nameof(method))
        };
    }
}
