namespace CScore;

/// <summary>
/// Контракт сечения для проверки предельных деформаций.
/// </summary>
public interface ILimitSection
{
   /// <summary>Интеграл усилий при плоскости деформаций.</summary>
   Load Integral(Kurvature k, CalcType calc, bool ten = true);

   /// <summary>Вершины контура (внешний + отверстия) для проверки ε_cu.</summary>
   IEnumerable<(double X, double Y)> ContourVertices { get; }

   /// <summary>
   /// Арматура: координаты, предельная растягивающая деформация ε_su и начальная деформация
   /// преднапряжения ε_p. ε_su берётся у материала СВОЕГО стержня (физический предел текучести
   /// — 0.025, условный — 0.015), поэтому сравнивать деформацию можно только со «своим» ε_su.
   /// Проверять следует ПОЛНУЮ деформацию ε_плоскости + ε_p — именно её видит диаграмма.
   /// </summary>
   IEnumerable<(double X, double Y, double EpsSu, double EpsP)> RebarPoints { get; }

   /// <summary>Предельная сжимаемость бетона контура (отрицательное число).</summary>
   double EpsCu { get; }
}
