using CScore;

namespace CScore.Fire;

/// <summary>Результат проверки шага тепловой сетки.</summary>
/// <param name="BelowRebarDiameter">Нарушено обязательное условие: шаг не больше максимального диаметра.</param>
/// <param name="OutOfRecommendedRange">Шаг вне рекомендуемого диапазона 0,01–0,03 м.</param>
/// <param name="MaxRebarDiameterM">Максимальный диаметр рабочей арматуры, м.</param>
/// <param name="UnknownDiameterCount">Число стержней, у которых диаметр определить не удалось.</param>
/// <param name="MinSideM">Меньшая сторона габарита сечения, м; 0, если контуров нет.</param>
/// <param name="TooCoarse">Меньше <see cref="FireMeshStepValidator.MinElementsAcross"/> элементов по меньшей стороне.</param>
public readonly record struct FireMeshStepCheck(
   bool BelowRebarDiameter,
   bool OutOfRecommendedRange,
   double MaxRebarDiameterM,
   int UnknownDiameterCount,
   double MinSideM = 0.0,
   bool TooCoarse = false)
{
   /// <summary>Тепловой расчёт запускать нельзя.</summary>
   public bool BlocksRun => BelowRebarDiameter || TooCoarse;
}

/// <summary>
/// Проверка шага тепловой сетки по п. 6.2 СП 468: рекомендуемый диапазон
/// 0,01–0,03 м, обязательное условие — шаг больше максимального диаметра
/// рабочей арматуры. Дополнительно запуск блокируется при явно грубой сетке — меньше
/// <see cref="MinElementsAcross"/> элементов по меньшей стороне габарита сечения:
/// распределение температуры по толщине при этом не определяется.
/// </summary>
/// <remarks>
/// Проверяются те же точечные волокна, которые попадут в тепловую сетку через
/// <see cref="FireMeshBuilder"/>, чтобы UI и расчёт видели одну и ту же арматуру.
/// Хомуты и поперечная арматура в тепловую сетку не попадают и здесь не участвуют.
/// </remarks>
public static class FireMeshStepValidator
{
   const double MinRecommendedM = 0.01;
   const double MaxRecommendedM = 0.03;
   const double RelTolerance = 1e-9;

   /// <summary>Минимальное число элементов по меньшей стороне сечения.</summary>
   public const int MinElementsAcross = 10;

   /// <summary>Проверить шаг сетки для сечения.</summary>
   public static FireMeshStepCheck Check(CrossSection section, double meshStepM)
   {
      ArgumentNullException.ThrowIfNull(section);

      double maxDiameter = 0.0;
      int unknown = 0;

      foreach (var area in section.Areas)
      {
         foreach (var fiber in area.Fibers)
         {
            if (fiber.TypeFiber != FiberType.point) continue;

            double d = fiber.Diameter;
            if (d <= 0.0 && fiber.Area > 0.0)
               d = 2.0 * Math.Sqrt(fiber.Area / Math.PI);

            if (d <= 0.0) { unknown++; continue; }
            if (d > maxDiameter) maxDiameter = d;
         }
      }

      bool belowDiameter = maxDiameter > 0.0
                 && meshStepM <= maxDiameter * (1.0 + RelTolerance);
      bool outOfRange = meshStepM < MinRecommendedM * (1.0 - RelTolerance)
                     || meshStepM > MaxRecommendedM * (1.0 + RelTolerance);
      double minSide = MinSide(section);
      bool tooCoarse = minSide > 0.0
                    && meshStepM * MinElementsAcross > minSide * (1.0 + RelTolerance);

      return new FireMeshStepCheck(belowDiameter, outOfRange, maxDiameter, unknown, minSide, tooCoarse);
   }

   /// <summary>Меньшая сторона габарита всех площадных областей сечения, м.</summary>
   static double MinSide(CrossSection section)
   {
      double xMin = double.PositiveInfinity, xMax = double.NegativeInfinity;
      double yMin = double.PositiveInfinity, yMax = double.NegativeInfinity;
      foreach (var area in section.Areas)
      {
         var hull = area.Hull;
         if (hull is null) continue;
         int n = Math.Min(hull.X.Count, hull.Y.Count);
         for (int i = 0; i < n; i++)
         {
            xMin = Math.Min(xMin, hull.X[i]); xMax = Math.Max(xMax, hull.X[i]);
            yMin = Math.Min(yMin, hull.Y[i]); yMax = Math.Max(yMax, hull.Y[i]);
         }
      }
      if (!double.IsFinite(xMin) || !double.IsFinite(yMin)) return 0.0;
      return Math.Min(xMax - xMin, yMax - yMin);
   }
}
