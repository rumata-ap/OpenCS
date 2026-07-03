namespace CScore.Combinations
{
   /// <summary>
   /// Коэффициенты сочетания нагрузок по категориям.
   /// Индивидуальные ψ₁/ψ₂ в Loading имеют приоритет для нагрузок одного типа;
   /// для нагрузок другого типа и для особого сочетания используются значения здесь.
   /// </summary>
   public class CombRules
   {
      /// <summary>Вид сочетания.</summary>
      public CombType CombType { get; }

      /// <summary>ψ для длительной переменной в роли сопровождающей (табл. 6.1 СП 20: 0.95).</summary>
      public double PsiLongAcc { get; }

      /// <summary>ψ для кратковременной переменной в роли сопровождающей (табл. 6.1 СП 20: 0.90).</summary>
      public double PsiShortAcc { get; }

      /// <summary>ψ для кратковременных нагрузок в особом сочетании (п. 6.5 СП 20: 0.80).</summary>
      public double PsiSpecialShort { get; }

      CombRules(CombType type, double psiLong, double psiShort, double psiSpec)
      {
         CombType = type;
         PsiLongAcc = psiLong;
         PsiShortAcc = psiShort;
         PsiSpecialShort = psiSpec;
      }

      /// <summary>Основное сочетание по табл. 6.1 СП 20.13330.2017.</summary>
      public static CombRules SP20Fundamental() =>
         new(CombType.Fundamental, psiLong: 0.95, psiShort: 0.90, psiSpec: 0.80);

      /// <summary>Особое сочетание по п. 6.5 СП 20.13330.2016.</summary>
      public static CombRules SP20Accidental() =>
         new(CombType.Accidental, psiLong: 0.95, psiShort: 0.90, psiSpec: 0.80);
   }
}
