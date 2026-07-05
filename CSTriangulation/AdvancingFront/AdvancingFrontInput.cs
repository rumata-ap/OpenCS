using System.Collections.Generic;

namespace CSTriangulation
{
   /// <summary>Верхнеуровневый вход метода продвижения фронта: внешний контур, отверстия, ограничения.</summary>
   public sealed class AdvancingFrontInput
   {
      public ContourLoop Outer { get; set; } = null!;
      public List<ContourLoop> Holes { get; set; } = new();
      public List<ConstrainedElement> Constraints { get; set; } = new();
   }
}
