using CSmath.Geometry;

using System.Text.Json.Serialization;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CScore
{
   [Serializable]
   public class Basis
   {
      [JsonIgnore]
      public Plane Plane { get; set; }
      public StressPoint P1;
      public StressPoint P2;
      public StressPoint P3;
      public Diagramm Diagramm;
      public Kurvature K;
      public Load I;
      public double eps_ult;

      public void Update(double e1, double e2, double e3)
      {
         P1.Eps = e1;
         P2.Eps = e2;
         P3.Eps = e3;
         Vector3D ve = new Vector3D(e1, e2, e3);
         Plane.Update(ve);
         K = new Kurvature() { e0 = Plane.Kurvature.Z, ky = Plane.Kurvature.Y, kz = Plane.Kurvature.X };
      }

      //public void SetLimitStrainInConcrete(StressPoint tenseled, StressPoint compressed)
      //{
      //   double eb2 = Diagramm.Strain["eb2"];
      //   double eb0 = Diagramm.Strain["eb0"];
      //   if (tenseled.Eps < 0 && compressed.Eps < 0)
      //      eps_ult = eb2 - (eb2 - eb0) * (tenseled.Eps / compressed.Eps);
      //   else
      //      eps_ult = eb2;
      //}

   }
}
