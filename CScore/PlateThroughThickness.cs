using System.Collections.Generic;

namespace CScore
{
   /// <summary>Точка арматурной эпюры: отметка z (м), напряжение σ (МПа), направление.</summary>
   public readonly record struct RebarStressPoint(double Z, double Sigma, bool AlongX);

   /// <summary>
   /// Выборка деформаций и напряжений по толщине пластины (для эпюр ε(z)/σ(z)).
   /// Бетон — в nPoints отметках; арматура — по точкам слоёв.
   /// </summary>
   public sealed class PlateThroughThickness
   {
      public double[] Z = [];
      public double[] EpsX = [];
      public double[] EpsY = [];
      public double[] GammaXY = [];
      public double[] SigX = [];
      public double[] SigY = [];
      public double[] TauXY = [];
      public List<RebarStressPoint> Rebar = [];
   }

   /// <summary>
   /// Профили главных деформаций/напряжений по толщине пластины (вкладка «Главные оси»).
   /// Напряжения σ₁, σ₂ — в кПа; угол θ — в градусах.
   /// </summary>
   public sealed class PlatePrincipalAxes
   {
      public double[] Z        = [];
      public double[] Eps1     = [];
      public double[] Eps2     = [];
      public double[] Sig1     = [];   // кПа
      public double[] Sig2     = [];   // кПа
      public double[] Beta     = [];   // коэфф. снижения Vecchio–Collins (0..1)
      public double[] ThetaDeg = [];   // угол главных осей от x, градусы
   }
}
