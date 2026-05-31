using NetTopologySuite.Geometries;

using System;

namespace CScore
{
   [Serializable]
   public class ReBarLayer : ReBar
   {
      public double Nd {  get; set; }
      public ReBarLayerPos Pos { get; set; }
      /// <summary>
      /// Площадь одного стержня (d²·π/4).
      /// </summary>
      public double As { get; set; }

      public ReBarLayer() { }

      public ReBarLayer(double d, int ns, double a, ReBarLayerPos pos, Region beton) : base(d)
      {
         Nd = ns;
         Diameter = d;
         Pos = pos;
         Area = d * d * Math.PI * 0.25 * ns;
         Polygon poly = beton.Polygon;
         double ymin = poly.Envelope.Coordinates[0].Y;
         double ymax = poly.Envelope.Coordinates[1].Y;
         if (pos == ReBarLayerPos.Bot) Y = ymax - a;
         else Y = ymin + a;
      }

      public ReBarLayer(double d, double As, double a, ReBarLayerPos pos, Region beton) : base(d)
      {
         double rebarArea = d * d * Math.PI * 0.25;
         Area = As;
         Diameter = d;
         Pos = pos;
         this.As = rebarArea;
         Nd = Area / this.As;
         Polygon poly = beton.Polygon;
         double ymin = poly.Envelope.Coordinates[0].Y;
         double ymax = poly.Envelope.Coordinates[1].Y;
         if (pos == ReBarLayerPos.Bot) Y = ymax - a;
         else Y = ymin + a;
      }

      public ReBarLayer(double x, double y, double d, int ns, ReBarLayerPos pos) : base(d)
      {
         Nd = ns;
         Diameter = d;
         Pos = pos;
         Area = d * d * Math.PI * 0.25 * ns;
         X = x; Y = y;
      }

      public ReBarLayer(double x, double y, double d, double area, ReBarLayerPos pos) : base(d)
      {
         Area = area;
         Diameter = d;
         Pos = pos;
         As = d * d * Math.PI * 0.25;
         Nd = Area / As;
         X = x; Y = y;
      }

      public override ReBarLayer Clone()
      {
         var res = new ReBarLayer(X, Y, Diameter * 1000, Area, Pos)
         {
            Eps_p = Eps_p,
         };
         return res;
      }

      public override string ToString()
      {
         if (Group == null)
            return $"{Num:D3}#rebarlayer : {Tag} | <No Group>";
         else return $"{Num:D3}#rebarlayer : {Tag} | <{Group.Tag}>";
         //return $"{Num:D3}#rebarlayer : {Tag} | <No Group>";
      }
   }

   public enum ReBarLayerPos
   {
      Top,
      Bot,
      Right,
      Left
   }
}
