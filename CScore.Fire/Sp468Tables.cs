namespace CScore.Fire;

/// <summary>
/// Табличные данные СП 468.1325800.2019 (с Изменением № 1).
/// Только данные и интерполяция по ним; расчётная логика — в вызывающем коде.
/// </summary>
/// <remarks>
/// За пределами табличного диапазона значение продлевается константой крайнего узла,
/// между узлами — линейная интерполяция.
/// </remarks>
public static class Sp468Tables
{
   // --- Таблица 5.1: γ_bt, нагретое состояние (расчёт на огнестойкость) ---
   static readonly double[] GammaBtT = [20, 200, 300, 400, 500, 600, 700, 800];
   static readonly double[] GammaBtSilicate    = [1.0, 0.98, 0.95, 0.85, 0.80, 0.60, 0.20, 0.00];
   static readonly double[] GammaBtCarbonate   = [1.0, 1.00, 0.95, 0.90, 0.85, 0.65, 0.30, 0.15];
   static readonly double[] GammaBtLightweight = [1.0, 1.00, 1.00, 0.95, 0.85, 0.70, 0.50, 0.25];

   // --- Таблица 5.6: γ_st и γ_st^e, нагретое состояние ---
   static readonly double[] GammaStT = [20, 200, 300, 400, 500, 600, 700, 800];

   static readonly double[][] GammaStRows =
   [
      [1.0, 1.00, 1.00, 0.85, 0.60, 0.37, 0.22, 0.10], // A240A500
      [1.0, 1.00, 0.96, 0.80, 0.55, 0.30, 0.12, 0.08], // A600A1000
      [1.0, 1.00, 0.90, 0.65, 0.35, 0.15, 0.05, 0.02], // WireRope
      [1.0, 1.00, 1.00, 0.92, 0.87, 0.76, 0.39, 0.18], // A500C25G2S
      [1.0, 0.92, 0.84, 0.76, 0.82, 0.69, 0.42, 0.13], // A600C18G2SF
      [1.0, 0.97, 0.94, 0.87, 0.85, 0.72, 0.43, 0.17], // A500CSt3Gps
      [1.0, 1.00, 1.00, 1.00, 1.00, 0.81, 0.33, 0.18]  // B500CSt3Gps
   ];

   static readonly double[][] GammaStERows =
   [
      [1.0, 0.92, 0.90, 0.85, 0.80, 0.77, 0.72, 0.65], // A240A500
      [1.0, 0.90, 0.85, 0.80, 0.76, 0.70, 0.66, 0.61], // A600A1000
      [1.0, 0.94, 0.86, 0.77, 0.64, 0.55, 0.45, 0.35], // WireRope
      [1.0, 1.00, 1.00, 0.99, 0.94, 0.93, 0.77, 0.60], // A500C25G2S
      [1.0, 0.99, 0.99, 0.91, 0.91, 0.83, 0.72, 0.65], // A600C18G2SF
      [1.0, 1.00, 1.00, 0.98, 0.93, 0.88, 0.82, 0.67], // A500CSt3Gps
      [1.0, 1.00, 1.00, 1.00, 1.00, 0.97, 0.91, 0.63]  // B500CSt3Gps
   ];

   // --- Таблица 5.3: α_bt, 1/°C. Узлы 20-50 / 100 / 300 / 500 / 700-1100 ---
   static readonly double[] AlphaBtT = [20, 100, 300, 500, 700];
   static readonly double[] AlphaBtSilicate    = [9.0e-6, 9.0e-6, 8.0e-6, 11.0e-6, 14.5e-6];
   static readonly double[] AlphaBtCarbonate   = [10.0e-6, 10.0e-6, 9.0e-6, 12.0e-6, 15.5e-6];
   static readonly double[] AlphaBtLightweight = [8.5e-6, 8.5e-6, 7.0e-6, 5.5e-6, 4.5e-6];

   // --- Таблица 5.7: α_st, 1/°C. Одна строка для всех классов ---
   static readonly double[] AlphaStT = [20, 100, 200, 300, 400, 500, 600, 700, 800];
   static readonly double[] AlphaStValues =
      [11.5e-6, 12.0e-6, 12.5e-6, 13.0e-6, 13.5e-6, 14.0e-6, 14.5e-6, 15.0e-6, 15.5e-6];

   // --- Таблица 5.5: ε_b2 при расчёте на огнестойкость, силикатный заполнитель ---
   static readonly double[] EpsB2T = [20, 100, 200, 300, 400, 500];
   static readonly double[] EpsB2Values = [0.0035, 0.0044, 0.0061, 0.0088, 0.0114, 0.0158];

   /// <summary>γ_bt по таблице 5.1: коэффициент условий работы бетона на сжатие.</summary>
   /// <param name="aggregateType">silicate, carbonate или lightweight; иное — как silicate.</param>
   /// <param name="tCelsius">Температура бетона, °C.</param>
   public static double GammaBt(string? aggregateType, double tCelsius)
      => Interp(tCelsius, GammaBtT, ResolveAggregateRow(aggregateType,
            GammaBtSilicate, GammaBtCarbonate, GammaBtLightweight));

   /// <summary>
   /// γ_st по таблице 5.6: коэффициент условий работы арматуры.
   /// Единый для растяжения и сжатия — формулы (5.5) и (5.6) применяют один и тот же коэффициент.
   /// </summary>
   public static double GammaSt(FireRebarClass group, double tCelsius)
      => Interp(tCelsius, GammaStT, GammaStRows[(int)group]);

   /// <summary>γ_st^e по таблице 5.6: коэффициент изменения модуля упругости арматуры.</summary>
   public static double GammaStE(FireRebarClass group, double tCelsius)
      => Interp(tCelsius, GammaStT, GammaStERows[(int)group]);

   /// <summary>α_bt по таблице 5.3: коэффициент температурного расширения бетона, 1/°C.</summary>
   public static double AlphaBt(string? aggregateType, double tCelsius)
      => Interp(tCelsius, AlphaBtT, ResolveAggregateRow(aggregateType,
            AlphaBtSilicate, AlphaBtCarbonate, AlphaBtLightweight));

   /// <summary>α_st по таблице 5.7: коэффициент температурного расширения арматуры, 1/°C.</summary>
   public static double AlphaSt(double tCelsius) => Interp(tCelsius, AlphaStT, AlphaStValues);

   /// <summary>
   /// ε_b2 по таблице 5.5 (расчёт на огнестойкость). Таблица дана только для тяжёлого
   /// бетона на силикатном заполнителе и обрывается на 500 °C.
   /// </summary>
   /// <param name="outOfRange">true, если температура вышла за табличный диапазон.</param>
   public static double EpsB2Silicate(double tCelsius, out bool outOfRange)
   {
      outOfRange = tCelsius < EpsB2T[0] || tCelsius > EpsB2T[^1];
      return Interp(tCelsius, EpsB2T, EpsB2Values);
   }

   static double[] ResolveAggregateRow(string? aggregateType,
      double[] silicate, double[] carbonate, double[] lightweight)
      => (aggregateType?.Trim().ToLowerInvariant()) switch
      {
         "carbonate" => carbonate,
         "lightweight" => lightweight,
         _ => silicate
      };

   /// <summary>Линейная интерполяция с продлением константой за границами диапазона.</summary>
   public static double Interp(double x, double[] xs, double[] ys)
   {
      if (xs.Length == 0) return 0.0;
      if (x <= xs[0]) return ys[0];
      if (x >= xs[^1]) return ys[^1];

      for (int i = 1; i < xs.Length; i++)
      {
         if (x > xs[i]) continue;
         double dx = xs[i] - xs[i - 1];
         if (dx <= 0.0) return ys[i];
         double w = (x - xs[i - 1]) / dx;
         return ys[i - 1] + w * (ys[i] - ys[i - 1]);
      }

      return ys[^1];
   }
}
