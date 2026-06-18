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
}
