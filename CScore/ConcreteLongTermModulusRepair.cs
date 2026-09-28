using CScore.Sp63.CrackWidth;

namespace CScore
{
   /// <summary>
   /// Исправление характеристик бетона NL (продолжительное действие), сохранённых в проектах
   /// из ошибочного справочника <c>DataSource/*_NL_*.csv</c>. Модуль деформаций бетона при
   /// продолжительном действии нагрузки по п. 6.1.15 СП 63.13330 (формула 6.3):
   /// Eb,τ = Eb / (1 + φb,cr), φb,cr — по таблице 6.12 (п. 6.1.16, тяжёлый и мелкозернистый бетон).
   /// <para>Распознаются две ошибки справочника (каждая — по точному совпадению с ошибочным
   /// значением, поэтому правленные вручную характеристики не трогаются):</para>
   /// <list type="bullet">
   /// <item>тяжёлый бетон: E = Eb,τ / 0,6 (модуль посчитан как Rb/εb1 вместо 0,6·Rb/εb1);</item>
   /// <item>мелкозернистый бетон: φb,cr взят из строки таблицы 6.12 для противоположной
   /// влажности («ниже 40 %» ↔ «выше 75 %»).</item>
   /// </list>
   /// <para>Деформации εb1 и εbt1 (0,6·Rb/E, 0,6·Rbt/E) пересчитываются по исправленному
   /// модулю, если они были согласованы со старым или с правильным модулем.</para>
   /// </summary>
   public static class ConcreteLongTermModulusRepair
   {
      const double Tol = 1e-3;

      /// <summary>
      /// Ищет среди характеристик материала пару N/NL бетона и исправляет NL на месте.
      /// </summary>
      /// <returns><see langword="true"/>, если характеристики NL изменены.</returns>
      public static bool TryRepair(IEnumerable<MaterialChars> chars)
      {
         var list = chars as IList<MaterialChars> ?? chars.ToList();
         var n = list.FirstOrDefault(c => c.TypeCalc == CalcType.N);
         var nl = list.FirstOrDefault(c => c.TypeCalc == CalcType.NL);
         return n != null && nl != null && TryRepair(n, nl);
      }

      /// <summary>
      /// Исправляет характеристики <paramref name="nl"/> на месте, если модуль совпадает
      /// с одним из ошибочных значений справочника.
      /// </summary>
      /// <param name="n">Характеристики N того же материала (источник Eb).</param>
      /// <param name="nl">Характеристики NL.</param>
      /// <returns><see langword="true"/>, если характеристики изменены.</returns>
      public static bool TryRepair(MaterialChars n, MaterialChars nl)
      {
         if (nl.Type != MatType.Concrete || !(n.E > 0) || !(nl.E > 0) || !(nl.Class > 0))
            return false;
         if (ToHumidity(nl.Dampness) is not { } humidity)
            return false;

         double eb = n.E;
         double phi = Sp63Curvature.PhiBCr(nl.Class, humidity)!.Value;
         double correct = eb / (1.0 + phi);

         bool heavyBug = Near(nl.E, correct / 0.6);
         bool swappedBug = Opposite(humidity) is { } opposite
            && Near(nl.E, eb / (1.0 + Sp63Curvature.PhiBCr(nl.Class, opposite)!.Value));
         if (!heavyBug && !swappedBug)
            return false;

         double oldE = nl.E;
         nl.E = correct;
         nl.Ec1 = Recompute(nl.Ec1, nl.Fc, oldE, correct);
         nl.Et1 = Recompute(nl.Et1, nl.Ft, oldE, correct);
         return true;
      }

      /// <summary>Влажность среды характеристик материала → строка таблицы 6.12.</summary>
      public static Sp63Humidity? ToHumidity(Dampness dampness) => dampness switch
      {
         Dampness.ниже_40 => Sp63Humidity.Below40,
         Dampness.от40_до70 => Sp63Humidity.From40To75,
         Dampness.свыше_70 => Sp63Humidity.Above75,
         _ => null
      };

      static Sp63Humidity? Opposite(Sp63Humidity h) => h switch
      {
         Sp63Humidity.Below40 => Sp63Humidity.Above75,
         Sp63Humidity.Above75 => Sp63Humidity.Below40,
         _ => null
      };

      // Деформация 0,6·R/E: пересчитывается, только если была согласована со старым модулем
      // (порча при смене влажности в диалоге) или уже с правильным (исходный справочник).
      static double Recompute(double eps, double strength, double oldE, double newE)
      {
         double fresh = 0.6 * strength / newE;
         return Near(eps, 0.6 * strength / oldE) || Near(eps, fresh) ? fresh : eps;
      }

      static bool Near(double value, double expected)
         => expected != 0 && Math.Abs(value - expected) <= Tol * Math.Abs(expected);
   }
}
