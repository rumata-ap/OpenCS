namespace CScore.Combinations
{
   /// <summary>
   /// Роль загружения в сочетании: постоянная (γf или пониженный γf п. 7.3 СП 20), временная по рангу степени
   /// влияния (ψ по п. 6.3–6.5) или особая.
   /// </summary>
   public enum Sp20TermRole
   {
      Permanent,        // постоянная, γf
      PermanentReduced, // постоянная при уменьшении веса, γf по п. 7.3 (0,9)
      Leading,          // временная, основная по степени влияния (ранг 1)
      Second,           // временная, ранг 2
      Other,            // временная, ранг 3 и далее
      Accidental        // особая
   }

   /// <summary>
   /// Коэффициенты сочетания нагрузок по СП 20.13330.2016. ψ назначаются по рангу нагрузки (п. 6.3–6.5) функцией
   /// <see cref="Psi"/>; ψ₁/ψ₂ из Loading не используются.
   /// </summary>
   public class CombRules
   {
      /// <summary>γf кратковременных нагрузок в особом сочетании (п. 6.5 СП 20).</summary>
      public const double AccidentalShortTermGamma = 0.5;

      /// <summary>Вид сочетания.</summary>
      public CombType CombType { get; }

      /// <summary>ψ для длительной переменной в роли сопровождающей (п. 6.3 СП 20: 0.95).</summary>
      public double PsiLongAcc { get; }

      /// <summary>ψ для второй кратковременной (п. 6.4 СП 20: 0.90).</summary>
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

      /// <summary>Основное сочетание по п. 6.2а СП 20.13330.2016.</summary>
      public static CombRules SP20Fundamental() =>
         new(CombType.Fundamental, psiLong: 0.95, psiShort: 0.90, psiSpec: 0.80);

      /// <summary>Особое сочетание по п. 6.5 СП 20.13330.2016.</summary>
      public static CombRules SP20Accidental() =>
         new(CombType.Accidental, psiLong: 0.95, psiShort: 0.90, psiSpec: 0.80);

      /// <summary>Роль временной нагрузки по рангу степени влияния (0 — основная).</summary>
      public static Sp20TermRole RoleOfRank(int rank) => rank switch
      {
         0 => Sp20TermRole.Leading,
         1 => Sp20TermRole.Second,
         _ => Sp20TermRole.Other
      };

      /// <summary>
      /// Коэффициент сочетания ψ по СП 20.13330.2016: длительные 1,0 / 0,95 (п. 6.3); кратковременные в основном
      /// сочетании 1,0 / 0,9 / 0,7 (п. 6.4), в особом 0,8 (п. 6.5); постоянные и особая — 1.
      /// </summary>
      public static double Psi(NormLoadType type, Sp20TermRole role, CombType comb) => type switch
      {
         NormLoadType.LongTerm => role == Sp20TermRole.Leading ? 1.0 : 0.95,
         NormLoadType.ShortTerm when comb == CombType.Accidental => 0.8,
         NormLoadType.ShortTerm => role switch
         {
            Sp20TermRole.Leading => 1.0,
            Sp20TermRole.Second  => 0.9,
            _                    => 0.7
         },
         _ => 1.0
      };
   }
}
