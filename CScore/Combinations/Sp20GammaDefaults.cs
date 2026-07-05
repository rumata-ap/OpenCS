namespace CScore.Combinations
{
   /// <summary>
   /// Коэффициенты надёжности по нагрузке γf по умолчанию для комбинаторики СП 20,
   /// если в имени набора усилий γf не задан явно.
   /// </summary>
   public sealed class Sp20GammaDefaults
   {
      /// <summary>γf неблагоприятно для постоянной нагрузки (G).</summary>
      public double PermanentUnfav { get; init; } = 1.1;

      /// <summary>γf благоприятно для постоянной нагрузки (G).</summary>
      public double PermanentFav { get; init; } = 0.9;

      /// <summary>γf для длительной переменной нагрузки (L).</summary>
      public double LongTermUnfav { get; init; } = 1.2;

      /// <summary>γf для кратковременной переменной нагрузки (Q).</summary>
      public double ShortTermUnfav { get; init; } = 1.4;

      /// <summary>γf для особой нагрузки (A).</summary>
      public double AccidentalUnfav { get; init; } = 1.0;

      /// <summary>Значения по умолчанию (СП 20, типовые).</summary>
      public static Sp20GammaDefaults Standard { get; } = new();
   }
}
