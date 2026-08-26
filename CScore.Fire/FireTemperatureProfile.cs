using CScore.Fire.Entities;

namespace CScore.Fire;

/// <summary>Полоса профиля температуры по высоте сечения.</summary>
/// <param name="S">Координата вдоль оси высоты от нагреваемой грани, м.</param>
/// <param name="TActual">Фактическая осреднённая температура полосы, °C.</param>
/// <param name="TLinear">Температура приведённой линейной эпюры, °C.</param>
public sealed record FireProfileBand(double S, double TActual, double TLinear);

/// <summary>Результат приведения температурного поля к линейной эпюре.</summary>
/// <param name="Height">Высота сечения вдоль оси приведения, м.</param>
/// <param name="THot">Температура более нагретой грани приведённой эпюры, °C.</param>
/// <param name="TCold">Температура менее нагретой грани приведённой эпюры, °C.</param>
/// <param name="AxisX">X-компонента единичной оси высоты.</param>
/// <param name="AxisY">Y-компонента единичной оси высоты.</param>
/// <param name="AxisFromInertia">Ось взята из главной оси инерции.</param>
/// <param name="UniformHeating">Градиент вырожден: |THot − TCold| меньше порога.</param>
/// <param name="Quality">Максимальное отклонение фактической эпюры от приведённой, °C.</param>
/// <param name="Bands">Полосы профиля для графика.</param>
public sealed record FireTemperatureProfileResult(
   double Height, double THot, double TCold,
   double AxisX, double AxisY,
   bool AxisFromInertia, bool UniformHeating,
   double Quality, IReadOnlyList<FireProfileBand> Bands);

/// <summary>
/// Построение профиля температуры по высоте сечения и приведение криволинейной
/// эпюры к линейной по п. 8.44а СП 468 из равенства площади и статического момента.
/// </summary>
public static class FireTemperatureProfile
{
   /// <summary>Порог, ниже которого градиент считается вырожденным, °C.</summary>
   public const double UniformThresholdCelsius = 5.0;

   /// <summary>Минимальное число полос профиля.</summary>
   public const int MinBands = 20;

   /// <summary>Приведение произвольной эпюры T(s) к линейной.</summary>
   public static (double THot, double TCold) ReduceToLinear(double[] s, double[] t)
   {
      ArgumentNullException.ThrowIfNull(s);
      ArgumentNullException.ThrowIfNull(t);
      if (s.Length != t.Length)
         throw new ArgumentException("Массивы координат и температуры должны иметь одинаковую длину.");
      if (s.Length == 0)
         throw new ArgumentException("Профиль температуры не должен быть пустым.", nameof(s));
      if (s.Length < 2) return (t[0], t[0]);

      double h = s[^1] - s[0];
      if (h <= 0.0) return (t[0], t[0]);

      double area = 0.0;
      double moment = 0.0;
      bool linear = true;
      double firstSlope = (t[1] - t[0]) / (s[1] - s[0]);
      for (int i = 2; i < s.Length; i++)
      {
         double slope = (t[i] - t[i - 1]) / (s[i] - s[i - 1]);
         double tol = 1e-10 * Math.Max(1.0, Math.Abs(firstSlope));
         if (Math.Abs(slope - firstSlope) > tol)
         {
            linear = false;
            break;
         }
      }

      for (int i = 1; i < s.Length; i++)
      {
         double ds = s[i] - s[i - 1];
         area += 0.5 * (t[i] + t[i - 1]) * ds;
         double x0 = s[i - 1] - s[0];
         if (linear)
         {
            // Точная интеграция произведения линейной интерполяции T(x) на x.
            moment += ds * (t[i - 1] * (x0 / 2.0 + ds / 6.0)
                          + t[i] * (x0 / 2.0 + ds / 3.0));
         }
         else
         {
            moment += 0.5 * (t[i] * (s[i] - s[0]) + t[i - 1] * x0) * ds;
         }
      }

      // A = h*(Thot + Tcold)/2, S = h^2*(Thot/6 + Tcold/3).
      double tCold = (6.0 * moment - 2.0 * area * h) / (h * h);
      double tHot = (4.0 * area * h - 6.0 * moment) / (h * h);
      return (tHot, tCold);
   }

   /// <summary>Построить профиль по огневому сечению и граничным условиям.</summary>
   public static FireTemperatureProfileResult Build(
      FireFiberSection fiber, FireSectionDef def, double meshStepM)
   {
      ArgumentNullException.ThrowIfNull(fiber);
      ArgumentNullException.ThrowIfNull(def);
      if (!double.IsFinite(meshStepM) || meshStepM <= 0.0)
         throw new ArgumentOutOfRangeException(nameof(meshStepM), "Шаг профиля должен быть положительным.");

      var (axisX, axisY, fromInertia) = ResolveAxis(fiber, def);

      double totalArea = 0.0, cx = 0.0, cy = 0.0;
      foreach (var c in fiber.ConcreteElements)
      {
         totalArea += c.Area;
         cx += c.Area * c.Cx;
         cy += c.Area * c.Cy;
      }
      if (totalArea <= 0.0)
         throw new InvalidOperationException("В огневом сечении нет бетонных элементов.");
      cx /= totalArea;
      cy /= totalArea;

      // Проекция каждого элемента на ось высоты.
      double sMin = double.MaxValue, sMax = double.MinValue;
      var proj = new double[fiber.ConcreteElements.Count];
      for (int i = 0; i < fiber.ConcreteElements.Count; i++)
      {
         var c = fiber.ConcreteElements[i];
         double s = (c.Cx - cx) * axisX + (c.Cy - cy) * axisY;
         proj[i] = s;
         if (s < sMin) sMin = s;
         if (s > sMax) sMax = s;
      }

      double height = sMax - sMin;
      if (height <= 0.0)
         throw new InvalidOperationException("Высота сечения вдоль оси приведения равна нулю.");

      int bandCount = Math.Max(MinBands, (int)Math.Ceiling(height / meshStepM));
      var sumT = new double[bandCount];
      var sumA = new double[bandCount];

      for (int i = 0; i < proj.Length; i++)
      {
         var c = fiber.ConcreteElements[i];
         int band = (int)((proj[i] - sMin) / height * bandCount);
         band = Math.Clamp(band, 0, bandCount - 1);
         sumT[band] += c.Temperature * c.Area;
         sumA[band] += c.Area;
      }

      var sCoord = new double[bandCount];
      var tActual = new double[bandCount];
      for (int b = 0; b < bandCount; b++)
      {
         sCoord[b] = height * (b + 0.5) / bandCount;
         tActual[b] = sumA[b] > 0.0 ? sumT[b] / sumA[b] : double.NaN;
      }

      FillGaps(tActual);
      var (tHot, tCold) = ReduceToLinear(sCoord, tActual);

      var bands = new List<FireProfileBand>(bandCount);
      double quality = 0.0;
      for (int b = 0; b < bandCount; b++)
      {
         double tLin = tHot + (tCold - tHot) * sCoord[b] / height;
         quality = Math.Max(quality, Math.Abs(tActual[b] - tLin));
         bands.Add(new FireProfileBand(sCoord[b], tActual[b], tLin));
      }

      bool uniform = Math.Abs(tHot - tCold) < UniformThresholdCelsius;
      return new FireTemperatureProfileResult(
         height, tHot, tCold, axisX, axisY, fromInertia, uniform, quality, bands);
   }

   /// <summary>Ось высоты: от нагреваемой грани внутрь сечения либо главная ось инерции.</summary>
   static (double X, double Y, bool FromInertia) ResolveAxis(FireFiberSection fiber, FireSectionDef def)
   {
      double fireX = 0.0, fireY = 0.0, fireLen = 0.0;
      foreach (var edge in fiber.FireBoundaryMidpoints(def))
      {
         fireX += edge.X * edge.Length;
         fireY += edge.Y * edge.Length;
         fireLen += edge.Length;
      }

      double totalArea = 0.0, cx = 0.0, cy = 0.0;
      foreach (var c in fiber.ConcreteElements)
      {
         totalArea += c.Area;
         cx += c.Area * c.Cx;
         cy += c.Area * c.Cy;
      }
      if (totalArea > 0.0) { cx /= totalArea; cy /= totalArea; }

      if (fireLen > 0.0)
      {
         double nx = fireX / fireLen - cx;
         double ny = fireY / fireLen - cy;
         double len = Math.Sqrt(nx * nx + ny * ny);
         if (len > 1e-9)
            return (-nx / len, -ny / len, false);
      }

      // Нагрев симметричен или огневых рёбер нет: берём главную ось инерции.
      double ixx = 0.0, iyy = 0.0, ixy = 0.0;
      foreach (var c in fiber.ConcreteElements)
      {
         double dx = c.Cx - cx, dy = c.Cy - cy;
         ixx += c.Area * dy * dy;
         iyy += c.Area * dx * dx;
         ixy += c.Area * dx * dy;
      }

      double angle = 0.5 * Math.Atan2(2.0 * ixy, iyy - ixx);
      return (Math.Cos(angle), Math.Sin(angle), true);
   }

   /// <summary>Заполнить пустые полосы линейной интерполяцией соседних.</summary>
   static void FillGaps(double[] values)
   {
      int n = values.Length;
      int firstKnown = Array.FindIndex(values, v => !double.IsNaN(v));
      if (firstKnown < 0) throw new InvalidOperationException("Профиль температуры пуст.");

      for (int i = 0; i < firstKnown; i++) values[i] = values[firstKnown];

      int lastKnown = Array.FindLastIndex(values, v => !double.IsNaN(v));
      for (int i = lastKnown + 1; i < n; i++) values[i] = values[lastKnown];

      int prev = firstKnown;
      for (int i = firstKnown + 1; i <= lastKnown; i++)
      {
         if (double.IsNaN(values[i])) continue;
         int gap = i - prev;
         for (int k = 1; k < gap; k++)
            values[prev + k] = values[prev] + (values[i] - values[prev]) * k / gap;
         prev = i;
      }
   }
}
