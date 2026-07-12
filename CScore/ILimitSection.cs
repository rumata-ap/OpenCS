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

   /// <summary>Арматура: координаты и предельная растягивающая деформация ε_su.</summary>
   IEnumerable<(double X, double Y, double EpsSu)> RebarPoints { get; }

   /// <summary>Предельная сжимаемость бетона контура (отрицательное число).</summary>
   double EpsCu { get; }
}
