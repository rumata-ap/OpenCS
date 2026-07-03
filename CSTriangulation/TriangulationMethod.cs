namespace CSTriangulation
{
   /// <summary>
   /// Метод триангуляции.
   /// </summary>
   public enum TriangulationMethod
   {
      /// <summary>
      /// Метод продвижения фронта (SETKA-4N-2D).
      /// </summary>
      AdvancingFront = 0,
      /// <summary>
      /// Метод Рупперта (CDT + рефайнмент).
      /// </summary>
      Ruppert = 1
   }
}