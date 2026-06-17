namespace CScore;

/// <summary>
/// Поиск предельного коэффициента масштабирования нагружения.
/// </summary>
public interface ILimitForceSolver
{
   /// <summary>Предельный коэффициент k·(N, Mx, My).</summary>
   LimitForceResult AllFactor(double n, double mx, double my);

   /// <summary>Предельный коэффициент k·(Mx, My) при фиксированном N.</summary>
   LimitForceResult MomentFactor(double n, double mx, double my);

   /// <summary>Предельный коэффициент k·N при фиксированных Mx, My.</summary>
   LimitForceResult AxialFactor(double n, double mx, double my);
}
