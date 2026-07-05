using System;

namespace CSTriangulation
{
   /// <summary>
   /// Ошибка триангуляции методом продвижения фронта: провал валидации входных
   /// данных (§1.1 спецификации) или превышение лимита итераций (§3.3).
   /// </summary>
   public sealed class TriangulationException : Exception
   {
      public TriangulationException(string message) : base(message) { }
   }
}
