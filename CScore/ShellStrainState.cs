using System;

namespace CScore
{
   /// <summary>
   /// Деформационное состояние оболочечного (плитного) сечения.
   /// Кинематика Кирхгофа–Ляве: линейное распределение по толщине z.
   ///   ε_x(z)  = ε₀x  + κx  · z
   ///   ε_y(z)  = ε₀y  + κy  · z
   ///   γ_xy(z) = γ₀xy + κxy · z
   /// </summary>
   public class ShellStrainState
   {
      /// <summary>Мембранная деформация вдоль x.</summary>
      public double Eps0x { get; set; }
      /// <summary>Мембранная деформация вдоль y.</summary>
      public double Eps0y { get; set; }
      /// <summary>Мембранная сдвиговая деформация γxy.</summary>
      public double Gamma0xy { get; set; }
      /// <summary>Кривизна κx, 1/м.</summary>
      public double Kx { get; set; }
      /// <summary>Кривизна κy, 1/м.</summary>
      public double Ky { get; set; }
      /// <summary>Кривизна кручения κxy, 1/м.</summary>
      public double Kxy { get; set; }

      public ShellStrainState() { }

      public ShellStrainState(double eps0x, double eps0y, double gamma0xy,
                              double kx, double ky, double kxy)
      {
         Eps0x    = eps0x;
         Eps0y    = eps0y;
         Gamma0xy = gamma0xy;
         Kx       = kx;
         Ky       = ky;
         Kxy      = kxy;
      }

      /// <summary>Деформация ε_x на отметке z (м).</summary>
      public double EpsX(double z) => Eps0x + Kx * z;
      /// <summary>Деформация ε_y на отметке z (м).</summary>
      public double EpsY(double z) => Eps0y + Ky * z;
      /// <summary>Сдвиговая деформация γ_xy на отметке z (м).</summary>
      public double GammaXY(double z) => Gamma0xy + Kxy * z;

      public double[] ToArray()
         => new[] { Eps0x, Eps0y, Gamma0xy, Kx, Ky, Kxy };

      public static ShellStrainState FromArray(double[] a)
         => new(a[0], a[1], a[2], a[3], a[4], a[5]);

      public static ShellStrainState Zero => new();
   }
}
