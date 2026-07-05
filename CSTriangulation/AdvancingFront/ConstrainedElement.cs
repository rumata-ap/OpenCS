using System.Collections.Generic;

namespace CSTriangulation
{
   /// <summary>Тип внутреннего ограничения (§1.6).</summary>
   public enum ConstraintKind { Point, Segment, Polyline, Arc }

   /// <summary>
   /// Внутреннее ограничение области — точка, отрезок, полилиния или дуга,
   /// которые должны точно войти в результирующую сетку (§1.6).
   /// </summary>
   public sealed class ConstrainedElement
   {
      public ConstraintKind Kind { get; }
      public ContourNode? Point { get; }
      public List<ContourFace>? Faces { get; }

      ConstrainedElement(ConstraintKind kind, ContourNode? point, List<ContourFace>? faces)
      {
         Kind = kind;
         Point = point;
         Faces = faces;
      }

      public static ConstrainedElement OfPoint(ContourNode point) => new(ConstraintKind.Point, point, null);

      public static ConstrainedElement OfSegment(ContourNode a, ContourNode b) =>
         new(ConstraintKind.Segment, null, new List<ContourFace> { ContourFace.Linear(a, b) });

      public static ConstrainedElement OfPolyline(List<ContourNode> nodes)
      {
         if (nodes.Count < 2)
            throw new TriangulationException("Constrained polyline должна содержать не менее 2 узлов.");
         var faces = new List<ContourFace>(nodes.Count - 1);
         for (int i = 0; i < nodes.Count - 1; i++)
            faces.Add(ContourFace.Linear(nodes[i], nodes[i + 1]));
         return new ConstrainedElement(ConstraintKind.Polyline, null, faces);
      }

      public static ConstrainedElement OfArc(ContourFace arcFace)
      {
         if (arcFace.Kind != FaceKind.Arc)
            throw new TriangulationException("ConstrainedElement.OfArc требует дуговую грань.");
         return new ConstrainedElement(ConstraintKind.Arc, null, new List<ContourFace> { arcFace });
      }
   }
}
