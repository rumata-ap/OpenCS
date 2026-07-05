using System.Collections.Generic;

namespace CSTriangulation
{
   /// <summary>Тип контура: внешний (hull) или отверстие (hole) — §1.1.</summary>
   public enum LoopKind { Hull, Hole }

   /// <summary>Замкнутый контур из граней (§1.1).</summary>
   public sealed class ContourLoop
   {
      public LoopKind Kind { get; }
      public List<ContourFace> Faces { get; }

      public ContourLoop(LoopKind kind, List<ContourFace> faces)
      {
         Kind = kind;
         Faces = faces;
      }
   }
}
