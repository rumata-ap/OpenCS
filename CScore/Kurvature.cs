using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CScore
{
   [Serializable]
   public struct Kurvature
   {
      public double e0;
      public double ky;
      public double kz;

      public void SetStrain(IList<StressPoint> stressPoints, out StressPoint tenseled, out StressPoint compressed)
      {
         foreach (var sp in stressPoints)
         {
            sp.Eps = kz * sp.X + ky * sp.Y + e0;
         }

         var sort = from sp in stressPoints orderby sp.Eps select sp;
         tenseled = sort.Last();
         compressed = sort.First();
      }

      public static Kurvature operator +(Kurvature xy1, Kurvature xy2)
      {
         return new Kurvature()
         {
            e0 = xy1.e0 + xy2.e0,
            ky = xy1.ky + xy2.ky,
            kz = xy1.kz + xy2.kz
         };
      }

      public static Kurvature operator -(Kurvature xy1, Kurvature xy2)
      {
         return new Kurvature()
         {
            e0 = xy1.e0 - xy2.e0,
            ky = xy1.ky - xy2.ky,
            kz = xy1.kz - xy2.kz
         };
      }
   }
}
