using System.Globalization;
using System.Text.RegularExpressions;

namespace CScore.Import;

/// <summary>
/// Разбор метки строки набора усилий в номер КЭ и номер сечения внешней расчётной схемы.
/// Нужен для наборов, импортированных до появления явных полей
/// <see cref="LoadItem.SourceElementNum"/> / <see cref="ShellLoadItem.SourceElementNum"/>.
/// </summary>
public static partial class ForceRowSourceParser
{
   // ЛИРА, импорт через API: «э.12 с1», «э.12 с1 к3 A2».
   [GeneratedRegex(@"^э\.(\d+)(?:\s+с(\d+))?(?:\s|$)")]
   private static partial Regex LiraApiLabel();

   // SCAD, XLS: «12_С1», «12_С1_К3», «12_С1 LS+SD», у пластин — «12_Центр», «12_Центр_К2».
   [GeneratedRegex(@"^(\d+)_(?:С(\d+)(?=_|\s|$))?")]
   private static partial Regex ScadLabel();

   // ЛИРА, HTML-отчёт: «127-1», «10825-C», «127» (элемент — сечение или узел).
   [GeneratedRegex(@"^(\d+)(?:-([\dA-Za-zА-Яа-я]+))?$")]
   private static partial Regex LiraHtmlLabel();

   /// <summary>Разобрать метку строки.</summary>
   /// <param name="label">Метка строки набора усилий.</param>
   /// <param name="elementNum">Номер КЭ.</param>
   /// <param name="sectionNum">Номер сечения; null — в метке нет или задан не числом.</param>
   /// <param name="liraHtml">Метка из HTML-отчёта ЛИРЫ («элемент-сечение»). Без этого признака
   /// форма «12-3» не разбирается: так же выглядят метки РСУ2 SCAD, где числа — не номер КЭ.</param>
   /// <returns>true, если номер КЭ найден.</returns>
   public static bool TryParse(string? label, out int elementNum, out int? sectionNum, bool liraHtml = false)
   {
      elementNum = 0;
      sectionNum = null;
      if (string.IsNullOrWhiteSpace(label)) return false;
      label = label.Trim();

      Match m = LiraApiLabel().Match(label);
      if (!m.Success) m = ScadLabel().Match(label);
      if (!m.Success && liraHtml) m = LiraHtmlLabel().Match(label);
      if (!m.Success) return false;

      if (!int.TryParse(m.Groups[1].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out elementNum)
          || elementNum <= 0)
      {
         elementNum = 0;
         return false;
      }
      if (m.Groups[2].Success
          && int.TryParse(m.Groups[2].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out int sec))
         sectionNum = sec;
      return true;
   }
}
