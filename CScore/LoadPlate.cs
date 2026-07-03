using CSmath;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CScore
{
   [Serializable]
   public class LoadPlate
   {
      public double Nxx {  get; set; }
      public double Nyy {  get; set; }
      public double Nxy {  get; set; }
      public double Mxx {  get; set; }
      public double Qxx {  get; set; }
      public double Myy {  get; set; }
      public double Qyy {  get; set; }
      public double Mxy {  get; set; }
      public CalcType Calc { get; set; }
      public LoadType Type { get; set; }
      public string Description { get; set; } = "";
      public string Loading { get; set; } = "";
      public int NoSect { get; set; }
      public int NoElem { get; set; }

      public LoadGroup ToLoadGroup(int n, double t)
      {
         var group = new LoadGroup() { Calc = Calc, Type = Type };
         group.N = new double[n];
         group.My = new double[n];
         Vector alfa = Vector.Arange(0, Math.PI, n);

         for (int i = 0; i < alfa.N; i++)
         {
            double cos = Math.Cos(alfa[i]);
            double cos2 = cos * cos;
            double sin = Math.Sin(alfa[i]);
            double sin2a = Math.Sin(2 * alfa[i]);
            double sin2 = sin * sin;
            group.N[i] = (Nxx * cos2 + Nyy * sin2 - Nxy * sin2a) * t;
            group.My[i] = Mxx * cos2 + Myy * sin2 - Mxy * sin2a;
         }

         return group;
      }
      public LoadGroup ToLoadGroup(int n, double t, out Vector alfa)
      {
         var group = new LoadGroup() { Calc = Calc, Type = Type };
         group.N = new double[n+1];
         group.My = new double[n+1];
         alfa = Vector.Arange(0, Math.PI, n);

         for (int i = 0; i < alfa.N; i++)
         {
            double cos = Math.Cos(alfa[i]);
            double cos2 = cos * cos;
            double sin = Math.Sin(alfa[i]);
            double sin2a = Math.Sin(2 * alfa[i]);
            double sin2 = sin * sin;
            group.N[i] = (Nxx * cos2 + Nyy * sin2 - Nxy * sin2a) * t;
            group.My[i] = Mxx * cos2 + Myy * sin2 - Mxy * sin2a;
         }

         return group;
      }

      public bool IsNull() 
      { 
         if(Nxx != 0 || Nyy != 0 || Mxx != 0 || Myy != 0) return false;
         else return true; 
      }


      public static LoadPlate operator +(LoadPlate l1, LoadPlate l2)
      {
         return new LoadPlate()
         {
            Nxx = l1.Nxx + l2.Nxx,
            Nyy = l1.Nyy + l2.Nyy,
            Nxy = l1.Nxy + l2.Nxy,
            Myy = l1.Myy + l2.Myy,
            Mxx = l1.Mxx + l2.Mxx,
            Mxy = l1.Mxy + l2.Mxy,
            Qyy = l1.Qyy + l2.Qyy,
            Qxx = l1.Qxx + l2.Qxx,
            Calc = l1.Calc,
            NoElem = l1.NoElem
         };
      }

   }
}
