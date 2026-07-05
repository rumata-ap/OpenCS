using System;

namespace CSTriangulation
{
   /// <summary>
   /// Тип грани контура (§1.2 спецификации).
   /// </summary>
   public enum FaceKind { Linear, Arc }

   /// <summary>
   /// Грань контура — линейная или дуговая (§1.2). Дуговая грань хранится
   /// в нормализованном виде (центр, радиус, начальный/конечный угол)
   /// независимо от исходного способа задания.
   /// </summary>
   public sealed class ContourFace
   {
      public FaceKind Kind { get; }
      public ContourNode A { get; }
      public ContourNode B { get; }
      public double CenterX { get; private set; }
      public double CenterY { get; private set; }
      public double Radius { get; private set; }
      public double Phi1 { get; private set; }
      public double Phi2 { get; private set; }

      /// <summary>Промежуточная точка, если грань задана тремя точками — используется только для валидации.</summary>
      public ContourNode? Mid { get; private set; }

      ContourFace(FaceKind kind, ContourNode a, ContourNode b)
      {
         Kind = kind;
         A = a;
         B = b;
      }

      public static ContourFace Linear(ContourNode a, ContourNode b) => new(FaceKind.Linear, a, b);

      public static ContourFace ArcByCenter(ContourNode a, ContourNode b,
         double cx, double cy, double radius, double phi1, double phi2)
      {
         if (radius <= 1e-9)
            throw new TriangulationException("Радиус дуговой грани должен быть положительным.");
         return new ContourFace(FaceKind.Arc, a, b) { CenterX = cx, CenterY = cy, Radius = radius, Phi1 = phi1, Phi2 = phi2 };
      }

      /// <summary>
      /// Дуга по трём точкам (§2.2, примечание): центр и радиус — из системы
      /// уравнений на перпендикуляры к серединам P1P2 и P2P3.
      /// </summary>
      public static ContourFace ArcByThreePoints(ContourNode a, ContourNode mid, ContourNode b)
      {
         double x1 = a.X, y1 = a.Y, x2 = mid.X, y2 = mid.Y, x3 = b.X, y3 = b.Y;
         double a1 = x2 - x1, b1 = y2 - y1, c1 = (x2 * x2 - x1 * x1 + y2 * y2 - y1 * y1) / 2.0;
         double a2 = x3 - x2, b2 = y3 - y2, c2 = (x3 * x3 - x2 * x2 + y3 * y3 - y2 * y2) / 2.0;
         double det = a1 * b2 - a2 * b1;
         if (Math.Abs(det) < 1e-9)
            throw new TriangulationException("Три точки дуговой грани коллинеарны — окружность не определена.");

         double cx = (c1 * b2 - c2 * b1) / det;
         double cy = (a1 * c2 - a2 * c1) / det;
         double radius = Math.Sqrt((x1 - cx) * (x1 - cx) + (y1 - cy) * (y1 - cy));
         double phi1 = Math.Atan2(y1 - cy, x1 - cx);
         double phi2 = Math.Atan2(y3 - cy, x3 - cx);

         var face = ArcByCenter(a, b, cx, cy, radius, phi1, phi2);
         face.Mid = mid;
         return face;
      }

      /// <summary>Окружность как единственная дуговая грань контура (§1.3).</summary>
      public static ContourFace FullCircle(double cx, double cy, double radius, double h, LoopKind loopKind)
      {
         var a = new ContourNode(cx + radius, cy, h);
         var b = new ContourNode(cx + radius, cy, h);
         double phi2 = loopKind == LoopKind.Hull ? 2.0 * Math.PI : -2.0 * Math.PI;
         return ArcByCenter(a, b, cx, cy, radius, 0.0, phi2);
      }

      /// <summary>
      /// Знаковой угол поворота дуги по направлению обхода контура (§2.2, шаг 1).
      /// Полный оборот (окружность, phi2 = phi1 ± 2π) не нормализуется.
      /// </summary>
      public static double SignedDeltaPhi(LoopKind loopKind, double phi1, double phi2)
      {
         const double twoPi = 2.0 * Math.PI;
         double raw = phi2 - phi1;
         if (Math.Abs(Math.Abs(raw) - twoPi) < 1e-7)
            return raw;
         if (loopKind == LoopKind.Hull)
            return raw >= 0 ? raw : raw + twoPi;
         return raw <= 0 ? raw : raw - twoPi;
      }

      /// <summary>Грань с противоположным направлением обхода (для авто-разворота контура, §1.1).</summary>
      public ContourFace Reversed()
      {
         if (Kind == FaceKind.Linear)
            return Linear(B, A);
         var rev = ArcByCenter(B, A, CenterX, CenterY, Radius, Phi2, Phi1);
         rev.Mid = Mid;
         return rev;
      }
   }
}
