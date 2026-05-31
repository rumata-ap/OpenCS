namespace CSmath
{
   /// <summary>
   /// Представляет числовой диапазон [start, end] с возможностью проверки принадлежности значения диапазону.
   /// </summary>
   class Range
   {
      double s;
      double e;

      /// <summary>
      /// Создаёт экземпляр диапазона с заданными начальной и конечной границами.
      /// </summary>
      /// <param name="start">Начало диапазона (левая граница).</param>
      /// <param name="end">Конец диапазона (правая граница).</param>
      public Range(double start, double end)
      {
         s = start;
         e = end;
      }

      /// <summary>
      /// Определяет, содержится ли указанное значение в диапазоне, включая границы.
      /// </summary>
      /// <param name="arg">Проверяемое значение.</param>
      /// <returns><c>true</c>, если значение принадлежит диапазону [s, e]; иначе <c>false</c>.</returns>
      public bool Contains(double arg)
      {
         if (arg >= s && arg <= e) return true;
         else return false;
      }
   }
}