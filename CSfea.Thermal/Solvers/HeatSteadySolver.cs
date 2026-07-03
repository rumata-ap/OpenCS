using CSfea.Sparse;

namespace CSfea.Thermal.Solvers;

/// <summary>
/// Статический решатель теплопроводности: K·T = F с граничными условиями Дирихле.
/// </summary>
public static class HeatSteadySolver
{
    /// <summary>Решить K·T = F с Dirichlet BC. fixedDofs[i] maps to uFixed[i].</summary>
    /// <param name="mesh">Сетка (для числа DOF).</param>
    /// <param name="K">Глобальная матрица теплопроводности.</param>
    /// <param name="F">Вектор нагрузки (источники, тепловые потоки).</param>
    /// <param name="fixedDofs">Закреплённые DOF (индексы узлов).</param>
    /// <param name="uFixed">Предписанные температуры на закреплённых DOF.</param>
    public static double[] Solve(
        HeatMesh mesh,
        CooMatrix K,
        double[] F,
        int[] fixedDofs,
        double[] uFixed)
    {
        var reduced = DirichletReducer.Reduce(K, F, fixedDofs, uFixed);
        double[] uFree = SparseLuSolver.SolveOnce(reduced.Kff, reduced.Fmod);
        return DirichletReducer.Expand(mesh.NDof, reduced.Free, uFree, fixedDofs, uFixed);
    }
}
