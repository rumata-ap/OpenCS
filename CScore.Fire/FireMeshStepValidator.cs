using CScore;

namespace CScore.Fire;

/// <summary>Результат проверки шага тепловой сетки.</summary>
/// <param name="BlocksRun">Нарушено обязательное условие: шаг не больше максимального диаметра.</param>
/// <param name="OutOfRecommendedRange">Шаг вне рекомендуемого диапазона 0,01–0,03 м.</param>
/// <param name="MaxRebarDiameterM">Максимальный диаметр рабочей арматуры, м.</param>
/// <param name="UnknownDiameterCount">Число стержней, у которых диаметр определить не удалось.</param>
public readonly record struct FireMeshStepCheck(
   bool BlocksRun,
   bool OutOfRecommendedRange,
   double MaxRebarDiameterM,
   int UnknownDiameterCount);

/// <summary>
/// Проверка шага тепловой сетки по п. 6.2 СП 468: рекомендуемый диапазон
/// 0,01–0,03 м, обязательное условие — шаг больше максимального диаметра
/// рабочей арматуры.
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

      bool blocks = maxDiameter > 0.0
                 && meshStepM <= maxDiameter * (1.0 + RelTolerance);
      bool outOfRange = meshStepM < MinRecommendedM * (1.0 - RelTolerance)
                     || meshStepM > MaxRecommendedM * (1.0 + RelTolerance);

      return new FireMeshStepCheck(blocks, outOfRange, maxDiameter, unknown);
   }
}
