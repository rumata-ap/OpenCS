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
      /// <summary>Момент относительно оси X: ∫σ·y·dA, управляется кривизной ky (dε/dy).</summary>
      public double Mx;
      /// <summary>Момент относительно оси Y: ∫σ·x·dA, управляется кривизной kz (dε/dx).</summary>
      public double My;
      public double N_ps;
      public double Mx_ps;
      public double My_ps;
      public double Qy;
      public double Qz;
      public double T;
      public CalcType Calc;
      public LoadType Type;
      public string Description;
      public string Loading;
      public int NoSect;
      public int NoElem;

      public bool Equals(Load other, double error = 1e-8)
      {
         return Math.Abs(N - other.N) <= error && Math.Abs(Mx - other.Mx) <= error &&
            Math.Abs(My - other.My) <= error && Math.Abs(Qy - other.Qy) <= error &&
            Math.Abs(Qz - other.Qz) <= error && Math.Abs(T - other.T) <= error;
      }

      public bool IsNull()
      {
         if (N != 0 || Mx != 0 || My != 0) return false;
         return true;
      }

      public double Norma()
      {
         return Math.Sqrt(N * N + Mx * Mx + My * My + Qy * Qy + Qz * Qz);
      }

      public double DeltaM(Load other)
      {
         Load l = this - other;
         return Math.Sqrt(l.N * l.N + l.Mx * l.Mx + l.My * l.My + l.Qy * l.Qy + l.Qz * l.Qz);
      }

      public static Load operator +(Load l1, Load l2)
      {
         return new Load()
         {
            N = l1.N + l2.N,
            N_ps = l1.N_ps + l2.N_ps,
            Mx = l1.Mx + l2.Mx,
            Mx_ps = l1.Mx_ps + l2.Mx_ps,
            My = l1.My + l2.My,
            My_ps = l1.My_ps + l2.My_ps,
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
            Mx = l1.Mx - l2.Mx,
            Mx_ps = l1.Mx_ps - l2.Mx_ps,
            My = l1.My - l2.My,
            My_ps = l1.My_ps - l2.My_ps,
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
            Mx = l1.Mx * l2.Mx,
            My = l1.My * l2.My,
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
            Mx = l1.Mx * l2,
            My = l1.My * l2,
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
            Mx = l1.Mx / l2.Mx,
            My = l1.My / l2.My,
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
            Mx = l1.Mx / l2,
            My = l1.My / l2,
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
            Mx = l1.Mx * l2,
            My = l1.My * l2,
            Qy = l1.Qy * l2,
            Qz = l1.Qz * l2,
            Calc = l1.Calc,
            NoElem = l1.NoElem
         };
      }

      public static bool operator !=(Load xy1, Load xy2)
      {
         return ((xy1.N - xy2.N) != 0) || ((xy1.Mx - xy2.Mx) != 0) ||
            ((xy1.My - xy2.My) != 0) || ((xy1.Qy - xy2.Qy) != 0) ||
            ((xy1.Qz - xy2.Qz) != 0) || ((xy1.T - xy2.T) != 0);
      }

      public static bool operator ==(Load xy1, Load xy2)
      {
         return ((xy1.N - xy2.N) == 0) && ((xy1.Mx - xy2.Mx) == 0) &&
            ((xy1.My - xy2.My) == 0) && ((xy1.Qy - xy2.Qy) == 0) &&
            ((xy1.Qz - xy2.Qz) == 0) && ((xy1.T - xy2.T) == 0);
      }

      public override bool Equals(object? obj) => obj is Load other && this == other;
      public override int GetHashCode() => HashCode.Combine(N, Mx, My, Qy, Qz, T);

   }

   public enum LoadType { Load, РСН, РСУ, Case}
}
