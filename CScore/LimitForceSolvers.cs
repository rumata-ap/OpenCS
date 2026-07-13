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
      int bisectMaxIter = 60,
      bool ten = true)
   {
      parameters ??= new LimitForceParams();

      // Поправка η (п. 8.1.15) реализована только в бисекционном решателе
      // (см. LimitForceSolver.MomentFactor) — быстрый Ньютон не поддерживает
      // пересчёт η внутри итераций, поэтому при EtaEnabled используем бисекцию
      // независимо от выбранного пользователем метода.
      if (parameters.Solver == "fast" && !parameters.EtaEnabled)
      {
         return new LimitForceSolverFast(section, calc,
            newtonTol: newtonTol, newtonMaxIter: newtonMaxIter,
            bisectTol: bisectTol, bisectMaxIter: bisectMaxIter, ten: ten);
      }

      return LimitForceSolver.ForCrossSection(section, calc,
         solverTol: newtonTol, solverMaxIter: newtonMaxIter,
         bisectTol: bisectTol, bisectMaxIter: bisectMaxIter, ten: ten, etaParams: parameters);
   }
}
