namespace CScore.PlateRebar;

/// <summary>Компонента армирования пластинчатого КЭ для мозаики (площадь на 1 п. м).</summary>
public enum PlateRebarMosaicComponent
{
   /// <summary>Нижняя (Z−) по X1 — As1 в ЛИРЕ.</summary>
   BottomX,
   /// <summary>Верхняя (Z+) по X1 — As2 в ЛИРЕ.</summary>
   TopX,
   /// <summary>Нижняя (Z−) по Y1 — As3 в ЛИРЕ.</summary>
   BottomY,
   /// <summary>Верхняя (Z+) по Y1 — As4 в ЛИРЕ.</summary>
   TopY,
   /// <summary>Поперечная (в единицах программы-источника).</summary>
   Transverse,
}

/// <summary>Значение армирования КЭ для мозаики: число, «нет данных» или отказ подбора.</summary>
public readonly record struct PlateRebarValue
{
   /// <summary>Площадь (см²/м для продольной); null — нет числа.</summary>
   public double? Value { get; init; }
   /// <summary>Код отказа подбора программы-источника; null — отказа нет.</summary>
   public int? FailureCode { get; init; }

   /// <summary>Нет данных по КЭ.</summary>
   public bool IsMissing => Value == null && FailureCode == null;

   /// <summary>Нет данных.</summary>
   public static PlateRebarValue Missing => default;
   /// <summary>Число.</summary>
   public static PlateRebarValue Of(double value) => new() { Value = value };
   /// <summary>Подбор не выполнен.</summary>
   public static PlateRebarValue Failure(int code) => new() { FailureCode = code };
}

/// <summary>
/// Источник армирования пластинчатых КЭ по тегу КЭ (подобранное или заданное армирование,
/// прочитанное из файла программы-источника).
/// </summary>
public interface IPlateRebarFieldSource
{
   /// <summary>Есть ли в источнике данная компонента.</summary>
   bool Supports(PlateRebarMosaicComponent component);

   /// <summary>Значение компоненты на КЭ.</summary>
   PlateRebarValue Get(string elemTag, PlateRebarMosaicComponent component);
}

/// <summary>Разность «заданное − требуемое»: минус — дефицит, плюс — запас.</summary>
public sealed class PlateRebarDifferenceSource(IPlateRebarFieldSource assigned, IPlateRebarFieldSource required)
   : IPlateRebarFieldSource
{
   /// <inheritdoc/>
   public bool Supports(PlateRebarMosaicComponent component) =>
      assigned.Supports(component) && required.Supports(component);

   /// <inheritdoc/>
   public PlateRebarValue Get(string elemTag, PlateRebarMosaicComponent component)
   {
      var r = required.Get(elemTag, component);
      if (r.FailureCode != null) return r;
      var a = assigned.Get(elemTag, component);
      if (a.FailureCode != null) return a;
      if (a.Value is not { } av || r.Value is not { } rv) return PlateRebarValue.Missing;
      return PlateRebarValue.Of(av - rv);
   }
}

/// <summary>Полоса дискретной шкалы мозаики: <c>[Lower, Upper)</c>; открытые концы — бесконечности.</summary>
/// <param name="Lower">Нижняя граница (включительно).</param>
/// <param name="Upper">Верхняя граница (исключительно).</param>
/// <param name="IsZero">Особая полоса «ровно 0» (арматура не требуется / не задана).</param>
public sealed record PlateRebarMosaicBand(double Lower, double Upper, bool IsZero = false)
{
   /// <summary>Полоса целиком в отрицательной области (дефицит для разности).</summary>
   public bool IsNegative => !IsZero && Upper <= 0;
   /// <summary>Полоса целиком в неотрицательной области.</summary>
   public bool IsPositive => !IsZero && Lower >= 0;
}

/// <summary>
/// Дискретная шкала мозаики. Обычная — отдельная полоса «0», далее полосы по порогам;
/// знакопеременная (разность) — полосы по порогам по обе стороны от нуля, без особой полосы «0».
/// </summary>
public sealed class PlateRebarMosaicScale
{
   /// <summary>Значения по модулю не больше этого считаются нулём.</summary>
   public const double ZeroTolerance = 1e-9;

   /// <summary>Полосы в порядке возрастания.</summary>
   public IReadOnlyList<PlateRebarMosaicBand> Bands { get; }
   /// <summary>Знакопеременная шкала (для разности).</summary>
   public bool IsDiverging { get; }
   /// <summary>Пороги между полосами (без особой полосы «0»).</summary>
   public IReadOnlyList<double> Thresholds { get; }

   PlateRebarMosaicScale(IReadOnlyList<double> thresholds, bool diverging)
   {
      Thresholds = thresholds;
      IsDiverging = diverging;
      var bands = new List<PlateRebarMosaicBand>();
      if (!diverging) bands.Add(new PlateRebarMosaicBand(0, 0, IsZero: true));
      double lower = diverging ? double.NegativeInfinity : 0;
      foreach (double t in thresholds)
      {
         bands.Add(new PlateRebarMosaicBand(lower, t));
         lower = t;
      }
      bands.Add(new PlateRebarMosaicBand(lower, double.PositiveInfinity));
      Bands = bands;
   }

   /// <summary>Шкала по заданным порогам. Для обычной шкалы неположительные пороги отбрасываются.</summary>
   public static PlateRebarMosaicScale Manual(IEnumerable<double> thresholds, bool diverging)
   {
      var t = thresholds.Where(double.IsFinite)
         .Where(v => diverging || v > ZeroTolerance)
         .Distinct().Order().ToList();
      return new PlateRebarMosaicScale(t, diverging);
   }

   /// <summary>
   /// Автошкала по значениям: обычная — до 8 полос шагом «круглого» числа от 0 до максимума;
   /// знакопеременная — до 4 полос в каждую сторону от нуля симметрично по max|v|.
   /// Полос ровно столько, чтобы максимум попал в последнюю.
   /// </summary>
   public static PlateRebarMosaicScale Auto(IEnumerable<double> values, bool diverging, int bands = 8)
   {
      if (bands < 2) throw new ArgumentOutOfRangeException(nameof(bands));
      var finite = values.Where(double.IsFinite).ToList();
      if (diverging)
      {
         int half = Math.Max(1, bands / 2);
         double amax = finite.Count > 0 ? finite.Max(Math.Abs) : 0;
         double step = NiceStep(amax / half);
         half = Math.Clamp(BandsToReach(amax, step), 1, half);
         var t = new List<double>();
         for (int i = -(half - 1); i <= half - 1; i++) t.Add(i * step);
         return new PlateRebarMosaicScale(t, true);
      }
      else
      {
         double max = finite.Count > 0 ? finite.Max() : 0;
         double step = NiceStep(max / bands);
         int n = Math.Clamp(BandsToReach(max, step), 1, bands);
         var t = new List<double>();
         for (int i = 1; i < n; i++) t.Add(i * step);
         return new PlateRebarMosaicScale(t, false);
      }
   }

   // Шаг округлён вверх, поэтому полос может понадобиться меньше запрошенных: иначе верхние пустуют.
   static int BandsToReach(double max, double step) => (int)Math.Ceiling(max / step - 1e-9);

   /// <summary>Индекс полосы значения.</summary>
   public int BandOf(double value)
   {
      if (!IsDiverging && Math.Abs(value) <= ZeroTolerance) return 0;
      int offset = IsDiverging ? 0 : 1;
      for (int i = 0; i < Thresholds.Count; i++)
         if (value < Thresholds[i]) return offset + i;
      return offset + Thresholds.Count;
   }

   /// <summary>Ближайший сверху к <paramref name="raw"/> «круглый» шаг 1, 2, 2,5, 5 × 10^k; для 0 — 1.</summary>
   public static double NiceStep(double raw)
   {
      if (!(raw > 0) || !double.IsFinite(raw)) return 1;
      double pow = Math.Pow(10, Math.Floor(Math.Log10(raw)));
      foreach (double m in (double[])[1, 2, 2.5, 5, 10])
         if (raw <= m * pow * (1 + 1e-12)) return m * pow;
      return 10 * pow;
   }
}

/// <summary>Мозаика: значения источника на наборе КЭ.</summary>
public static class PlateRebarMosaic
{
   /// <summary>Значения компоненты на КЭ (по тегу).</summary>
   public static Dictionary<string, PlateRebarValue> Evaluate(
      IPlateRebarFieldSource source, PlateRebarMosaicComponent component, IEnumerable<string> elemTags)
   {
      var result = new Dictionary<string, PlateRebarValue>(StringComparer.Ordinal);
      foreach (string tag in elemTags)
         result[tag] = source.Get(tag, component);
      return result;
   }
}
