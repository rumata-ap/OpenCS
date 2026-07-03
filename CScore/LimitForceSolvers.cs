namespace CScore;

/// <summary>
/// Фабрика решателей предельного нагружения (бисекция / быстрый Ньютон).
/// </summary>
public static class LimitForceSolvers
{
   /// <summary>Создать решатель по параметрам задачи.</summary>
   public static ILimitForceSolver Create(
      CrossSection section,
      CalcType calc,
      LimitForceParams? parameters = null,
      double newtonTol = 0.5,
      int newtonMaxIter = 60,
      double bisectTol = 1e-4,
      int bisectMaxIter = 60)
   {
      parameters ??= new LimitForceParams();
      if (parameters.Solver == "fast")
      {
         return new LimitForceSolverFast(section, calc,
            newtonTol: newtonTol, newtonMaxIter: newtonMaxIter,
            bisectTol: bisectTol, bisectMaxIter: bisectMaxIter);
      }

      return LimitForceSolver.ForCrossSection(section, calc,
         solverTol: newtonTol, solverMaxIter: newtonMaxIter,
         bisectTol: bisectTol, bisectMaxIter: bisectMaxIter);
   }
}
