using System.Text.RegularExpressions;

namespace CScore.Import
{
   internal enum LiraElementKind
   {
      Unknown,
      Bar,
      Shell,
   }

   internal readonly struct LiraUnitScales
   {
      public LiraUnitScales(double force, double moment, double shellForce, double shellMoment, double stress)
      {
         Force       = force;
         Moment      = moment;
         ShellForce  = shellForce;
         ShellMoment = shellMoment;
         Stress      = stress;
      }

      /// <summary>т → кN.</summary>
      public double Force { get; }

      /// <summary>т·м → кN·м.</summary>
      public double Moment { get; }

      /// <summary>т/м → кN/м.</summary>
      public double ShellForce { get; }

      /// <summary>(т·м)/м → кN·м/м.</summary>
      public double ShellMoment { get; }

      /// <summary>т/м² → кПа (кN/м²), масштаб для сырых импортированных напряжений σx/σy/τxy.</summary>
      public double Stress { get; }

      // Реальные HTML-экспорты ЛИРА: LiraHtmlParser.CleanText схлопывает переносы строк внутри
      // <pre> в одиночные пробелы (Regex.Replace(s, @"\s+", " ")), поэтому весь блок «Единицы
      // измерения ...» может прийти ОДНОЙ строкой с несколькими декларациями подряд («усилий: т
      // напряжений: кН/м2 моментов: т*м ...»). Построчный Contains+«взять всё после первого
      // двоеточия» в этом случае матчил только первую декларацию и терял остальные (в частности
      // «напряжений», что приводило к домножению σ на tonFactor вместо правильного кН/м2=1.0).
      // Поэтому вместо построчного разбора извлекаем все пары «метка: значение» регуляркой сразу
      // по всему тексту (работает одинаково и когда переносы строк сохранились, и когда нет).
      static readonly Regex DeclarationRx = new(@"измерения\s+([^:]+):\s*(\S+)", RegexOptions.Compiled);

      public static LiraUnitScales FromPreLines(IReadOnlyList<string> preLines, double tonFactor)
      {
         double force = tonFactor, moment = tonFactor, sForce = tonFactor, sMoment = tonFactor, stress = tonFactor;
         foreach (var raw in preLines)
         {
            var l = NormalizeHomoglyphs(raw.ToLowerInvariant());
            foreach (Match m in DeclarationRx.Matches(l))
            {
               string label = m.Groups[1].Value.Trim();
               string val   = m.Groups[2].Value.Trim();

               if (label.Contains("усилий"))
                  force = UnitTokens.ParseCompoundToKnBase(val, tonFactor) ?? force;
               else if (label.Contains("напряжений"))
                  stress = UnitTokens.ParseCompoundToKnBase(val, tonFactor) ?? stress;
               else if (label.Contains("моментов") && !label.Contains("расп") && !label.Contains("бимомент"))
                  moment = UnitTokens.ParseCompoundToKnBase(val, tonFactor) ?? moment;
               else if (label.Contains("расп") && label.Contains("момент"))
                  sMoment = UnitTokens.ParseCompoundToKnBase(val, tonFactor) ?? sMoment;
               else if (label.Contains("расп") && label.Contains("сил"))
                  sForce = UnitTokens.ParseCompoundToKnBase(val, tonFactor) ?? sForce;
            }
         }
         return new LiraUnitScales(force, moment, sForce, sMoment, stress);
      }

      /// <summary>ЛИРА-экспорт кодирует часть кириллических «р» латинской «p» (подтверждено
      /// побайтово на реальных файлах) — нормализуем перед сравнением ключевых слов.</summary>
      static string NormalizeHomoglyphs(string s) => s.Replace('p', 'р');
   }
}
