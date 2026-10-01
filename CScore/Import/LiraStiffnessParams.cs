using System.Globalization;

namespace CScore.Import;

/// <summary>Прямоугольное сечение стержня («Брус» ЛИРЫ).</summary>
/// <param name="WidthM">Ширина B — вдоль местной оси Y1, м.</param>
/// <param name="HeightM">Высота H — вдоль местной оси Z1, м.</param>
public sealed record LiraBarRect(double WidthM, double HeightM);

/// <summary>
/// Разбор параметров жёсткости ЛИРЫ (<see cref="LiraStiffnessRecord.Params"/>): строка токенов
/// «ключ:значение» через пробел, в конце — признак вида («BAR_END», «PLATE_END»).
/// </summary>
public static class LiraStiffnessParams
{
   /// <summary>Код вида жёсткости «Брус» (прямоугольное сечение стержня).</summary>
   public const int BarRectKind = 0;

   /// <summary>Жёсткость стержня стандартного сечения.</summary>
   public static bool IsBar(LiraStiffnessRecord s) => s.Params.Contains("BAR_END", StringComparison.Ordinal);

   /// <summary>Жёсткость пластины.</summary>
   public static bool IsPlate(LiraStiffnessRecord s) => s.Params.Contains("PLATE_END", StringComparison.Ordinal);

   /// <summary>Размеры бруса; null — жёсткость не «Брус» или размеры не заданы.</summary>
   public static LiraBarRect? BarRect(LiraStiffnessRecord s)
   {
      if (s.KindCode != BarRectKind || !IsBar(s) || !(s.SectionUnitM > 0)) return null;
      return Value(s.Params, "B") is double b && Value(s.Params, "H") is double h && b > 0 && h > 0
         ? new LiraBarRect(Math.Round(b * s.SectionUnitM, 6), Math.Round(h * s.SectionUnitM, 6))
         : null;
   }

   /// <summary>Толщина пластины, м; null — жёсткость не пластина или толщина не задана.</summary>
   public static double? PlateThicknessM(LiraStiffnessRecord s) =>
      IsPlate(s) && s.SectionUnitM > 0 && Value(s.Params, "H") is double h && h > 0
         ? Math.Round(h * s.SectionUnitM, 6) : null;

   /// <summary>Значение параметра по ключу; null — ключа нет или значение не число.</summary>
   public static double? Value(string parameters, string key)
   {
      foreach (string token in parameters.Split(' ', StringSplitOptions.RemoveEmptyEntries))
      {
         int colon = token.IndexOf(':');
         if (colon <= 0 || !token.AsSpan(0, colon).Equals(key, StringComparison.Ordinal)) continue;
         return double.TryParse(token.AsSpan(colon + 1).TrimEnd(','), NumberStyles.Float, CultureInfo.InvariantCulture, out double v)
            ? v : null;
      }
      return null;
   }
}
