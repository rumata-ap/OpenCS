using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CScore
{
   [Serializable]
   public struct Load
   {
      public double N;  
      public double My;  
      public double Mz;   
      public double N_ps;   
      public double My_ps;  
      public double Mz_ps;  
      public double Qy;  
      public double Qz;
      public double Mx;
      public CalcType Calc;
      public LoadType Type;
      public string Description;
      public string Loading;
      public int NoSect;
      public int NoElem;

      public bool Equals(Load other, double error = 1e-8)
      {
         return Math.Abs(N - other.N) <= error && Math.Abs(My - other.My) <= error && 
            Math.Abs(Mz - other.Mz) <= error && Math.Abs(Qy - other.Qy) <= error && 
            Math.Abs(Qz - other.Qz) <= error && Math.Abs(Mx - other.Mx) <= error;
      }

      public bool IsNull()
      {
         if (N != 0 || My != 0 || Mz != 0) return false;
         return true;
      }
      
      public double Norma()
      {
         return Math.Sqrt(N * N + My * My + Mz * Mz + Qy * Qy + Qz * Qz);
      }
      
      public double DeltaM(Load other)
      {
         Load l = this - other;
         return Math.Sqrt(l.N * l.N + l.My * l.My + l.Mz * l.Mz + l.Qy * l.Qy + l.Qz * l.Qz);
      }

      public static Load operator +(Load l1, Load l2)
      {
         return new Load()
         {
            N = l1.N + l2.N,
            N_ps = l1.N_ps + l2.N_ps,
            My = l1.My + l2.My,
            My_ps = l1.My_ps + l2.My_ps,
            Mz = l1.Mz + l2.Mz,
            Mz_ps = l1.Mz_ps + l2.Mz_ps,
            Qy = l1.Qy + l2.Qy,
            Qz = l1.Qz + l2.Qz,
            Calc = l1.Calc,
            NoElem = l1.NoElem
         };
      }

      public static Load operator -(Load l1, Load l2)
      {
         return new Load()
         {
            N = l1.N - l2.N,
            N_ps = l1.N_ps - l2.N_ps,
            My = l1.My - l2.My,
            My_ps = l1.My_ps - l2.My_ps,
            Mz = l1.Mz - l2.Mz,
            Mz_ps = l1.Mz_ps - l2.Mz_ps,
            Qy = l1.Qy - l2.Qy,
            Qz = l1.Qz - l2.Qz,
            Calc = l1.Calc,
            NoElem = l1.NoElem
         };
      }

      public static Load operator *(Load l1, Load l2)
      {
         return new Load()
         {
            N = l1.N * l2.N,
            My = l1.My * l2.My,
            Mz = l1.Mz * l2.Mz,
            Qy = l1.Qy * l2.Qy,
            Qz = l1.Qz * l2.Qz,
            Calc = l1.Calc,
            NoElem = l1.NoElem
         };
      }

      public static Load operator *(Load l1, double l2)
      {
         return new Load()
         {
            N = l1.N * l2,
            My = l1.My * l2,
            Mz = l1.Mz * l2,
            Qy = l1.Qy * l2,
            Qz = l1.Qz * l2,
            Calc = l1.Calc,
            NoElem = l1.NoElem
         };
      }
      
      public static Load operator /(Load l1, Load l2)
      {
         return new Load()
         {
            N = l1.N / l2.N,
            My = l1.My / l2.My,
            Mz = l1.Mz / l2.Mz,
            Qy = l1.Qy / l2.Qy,
            Qz = l1.Qz / l2.Qz,
            Calc = l1.Calc,
            NoElem = l1.NoElem
         };
      }

      public static Load operator /(Load l1, double l2)
      {
         return new Load()
         {
            N = l1.N / l2,
            My = l1.My / l2,
            Mz = l1.Mz / l2,
            Qy = l1.Qy / l2,
            Qz = l1.Qz / l2,
            Calc = l1.Calc,
            NoElem = l1.NoElem
         };
      }
      
      public static Load operator *(double l2, Load l1)
      {
         return new Load()
         {
            N = l1.N * l2,
            My = l1.My * l2,
            Mz = l1.Mz * l2,
            Qy = l1.Qy * l2,
            Qz = l1.Qz * l2,
            Calc = l1.Calc,
            NoElem = l1.NoElem
         };
      }

      public static bool operator !=(Load xy1, Load xy2)
      {
         return ((xy1.N - xy2.N) != 0) || ((xy1.My - xy2.My) != 0) || 
            ((xy1.Mz - xy2.Mz) != 0) || ((xy1.Qy - xy2.Qy) != 0) || 
            ((xy1.Qz - xy2.Qz) != 0) || ((xy1.Mx - xy2.Mx) != 0);
      }

      public static bool operator ==(Load xy1, Load xy2)
      {
         return ((xy1.N - xy2.N) == 0) && ((xy1.My - xy2.My) == 0) &&
            ((xy1.Mz - xy2.Mz) == 0) && ((xy1.Qy - xy2.Qy) == 0) &&
            ((xy1.Qz - xy2.Qz) == 0) && ((xy1.Mx - xy2.Mx) == 0);
      }

   }

   public enum LoadType { Load, РСН, РСУ, Case}
}
