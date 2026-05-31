using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CScore
{
   [Serializable]
   public struct LoadGroup
   {
      public IList<double> N;
      public IList<double> My;
      public IList<double> Mz;
      public IList<double> Qy;
      public IList<double> Qz;
      public IList<double> Mx;
      public CalcType Calc;
      public LoadType Type;
      public string Description;
      public IList<int> NoSect;
      public IList<int> NoElem;

      public Load[] GetLoadArray()
      {
         Load[] res = new Load[N.Count];
         for (int i = 0; i < N.Count; i++)
         {
            res[i] = new Load()
            { Calc = Calc,
               Description = Description,
               NoSect = NoSect == null ? 0 : NoSect[i],
               NoElem = NoElem == null ? 0 : NoElem[i],
               N = N == null ? 0 : N[i],
               My = My == null ? 0 : My[i],
               Mz = Mz == null ? 0 : Mz[i],
               Qy = Qy == null ? 0 : Qy[i],
               Qz = Qz == null ? 0 : Qz[i],
               Mx = Mx == null ? 0 : Mx[i],
               Type = Type
            };
         }

         return res;
      }
   }
}
