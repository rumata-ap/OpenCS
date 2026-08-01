using System.Collections.Generic;

namespace OpenCS.Utilites
{
   /// <summary>Разбор строки диапазонов номеров элементов в формате ЛираСАПР.
   /// Разделители: пробел, запятая или табуляция. Пример: "101-103 106 116-118".</summary>
   public static class LiraRangeParser
   {
      /// <summary>Разбирает строку диапазонов и возвращает отсортированный список номеров.</summary>
      public static List<int> Parse(string s)
      {
         var ids = new SortedSet<int>();
         foreach (var part in s.Split(new[] { ' ', ',', '\t' },
                      StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
         {
            var dash = part.IndexOf('-');
            if (dash > 0 &&
                int.TryParse(part[..dash], out int from) &&
                int.TryParse(part[(dash + 1)..], out int to))
            {
               for (int i = from; i <= to; i++) ids.Add(i);
            }
            else if (int.TryParse(part, out int single))
            {
               ids.Add(single);
            }
         }
         return [.. ids];
      }
   }
}
