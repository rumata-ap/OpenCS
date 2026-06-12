namespace CScore.Combinations
{
   /// <summary>Вид нагрузки по СП 20.13330 (для имён наборов: G/L/Q/A).</summary>
   public enum NormLoadType
   {
      Permanent,    // G — постоянная
      LongTerm,     // L — длительная переменная
      ShortTerm,    // Q — кратковременная переменная
      Accidental    // A — особая
   }

   /// <summary>Вид расчётного сочетания нагрузок.</summary>
   public enum CombType
   {
      Fundamental,  // основное сочетание (п. 6.2 СП 20)
      Accidental    // особое сочетание  (п. 6.5 СП 20)
   }
}
