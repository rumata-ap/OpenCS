namespace CScore.Import;

/// <summary>
/// Пересчёт единиц документа ЛИРЫ (LiraMeasurementUnits API) в единицы OpenCS: м, кН, кПа.
/// Единицы задаются в ЛИРЕ раздельно: координаты (Geometry), размеры сечений (Sections),
/// усилия — сила (Forces1) и длина в моментах и погонных усилиях (Forces2).
/// API результатов отдаёт напряжения пластин (Nx, Ny, Txy) тоже в единицах усилий — сила/длина²
/// (Forces1/Forces2²), а не в единицах Stresses: те — только единицы показа в таблицах ЛИРЫ.
/// Сверено 01.10.2026 (ЛИРА-САПР 2024, усилия в т·м, напряжения в кН/м²): σ из API × 9,81 = σ таблицы.
/// </summary>
/// <param name="ForceToKn">Единица силы усилий → кН.</param>
/// <param name="ForceLengthToM">Единица длины в составе усилий (момент = сила·длина, погонное = сила/длина) → м.</param>
/// <param name="StressToKpa">Единица напряжений из API (сила/длина² усилий) → кПа (кН/м²).</param>
public sealed record LiraApiUnits(double ForceToKn, double ForceLengthToM, double StressToKpa)
{
   /// <summary>Единицы OpenCS: пересчёт не нужен.</summary>
   public static LiraApiUnits Identity { get; } = new(1, 1, 1);

   /// <summary>
   /// Длина LiraUnitsGeometryEnum → м: 0 = м, 1 = см, 2 = мм, 3 = фут, 4 = дюйм; иное (в т. ч. −1 «нет») —
   /// <paramref name="fallback"/>.
   /// </summary>
   public static double LengthToM(int code, double fallback) => code switch
   {
      0 => 1.0,
      1 => 0.01,
      2 => 0.001,
      3 => 0.3048,
      4 => 0.0254,
      _ => fallback,
   };

   /// <summary>
   /// Сила LiraUnitsForceEnum → кН: 0 = г, 1 = кг, 2 = т, 3 = Н, 4 = кН, 5 = МН, 6 = фунт, 7 = kips;
   /// иное — <paramref name="fallback"/>.
   /// </summary>
   /// <param name="tonToKn">Сколько кН в тонне-силе (настройка импорта, обычно 9,81).</param>
   public static double ForceToKnOf(int code, double tonToKn, double fallback) => code switch
   {
      0 => tonToKn / 1e6,
      1 => tonToKn / 1e3,
      2 => tonToKn,
      3 => 1e-3,
      4 => 1.0,
      5 => 1000.0,
      6 => 0.0044482216,
      7 => 4.4482216,
      _ => fallback,
   };

   /// <summary>Единицы по кодам LiraMeasurementUnits (Forces1, Forces2): напряжения — сила/длина² усилий.</summary>
   public static LiraApiUnits FromCodes(int forces1, int forces2, double tonToKn)
   {
      double force = ForceToKnOf(forces1, tonToKn, 1.0), length = LengthToM(forces2, 1.0);
      return new(force, length, force / (length * length));
   }

   /// <summary>Сила → кН.</summary>
   public double Force(double v) => v * ForceToKn;

   /// <summary>Момент (сила·длина) → кН·м.</summary>
   public double Moment(double v) => v * ForceToKn * ForceLengthToM;

   /// <summary>Погонное усилие (сила/длина) → кН/м.</summary>
   public double PerLength(double v) => v * ForceToKn / ForceLengthToM;

   /// <summary>Погонный момент (сила·длина/длина) → кН·м/м: длины сокращаются.</summary>
   public double MomentPerLength(double v) => v * ForceToKn;

   /// <summary>Напряжение → кПа.</summary>
   public double Stress(double v) => v * StressToKpa;
}
