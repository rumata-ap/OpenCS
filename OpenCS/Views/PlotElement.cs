using System;
using System.Windows;
using System.Windows.Media;
using CScore;

namespace OpenCS.Views
{
   public abstract record PlotElement
   {
      public abstract void Render(DrawingContext dc, Func<double, double, Point> toPixel);
   }

   public record ScatterElement : PlotElement
   {
      public double[] Xs { get; init; } = [];
      public double[] Ys { get; init; } = [];
      public Brush Stroke { get; init; } = Brushes.Black;
      public double StrokeThickness { get; init; } = 1;
      public string? Label { get; init; }

      public override void Render(DrawingContext dc, Func<double, double, Point> toPixel)
      {
         int n = Math.Min(Xs.Length, Ys.Length);
         if (n < 2) return;
         var pen = new Pen(Stroke, StrokeThickness);
         pen.LineJoin = PenLineJoin.Round;
         pen.StartLineCap = PenLineCap.Round;
         pen.EndLineCap = PenLineCap.Round;

         var prev = toPixel(Xs[0], Ys[0]);
         for (int i = 1; i < n; i++)
         {
            var curr = toPixel(Xs[i], Ys[i]);
            dc.DrawLine(pen, prev, curr);
            prev = curr;
         }
      }
   }

   public record PolygonElement : PlotElement
   {
      public double[] Xs { get; init; } = [];
      public double[] Ys { get; init; } = [];
      public Brush? Fill { get; init; }
      public Brush Stroke { get; init; } = Brushes.Black;
      public double StrokeThickness { get; init; } = 1;

      public override void Render(DrawingContext dc, Func<double, double, Point> toPixel)
      {
         int n = Math.Min(Xs.Length, Ys.Length);
         if (n < 3) return;
         var stream = new StreamGeometry();
         using var ctx = stream.Open();
         ctx.BeginFigure(toPixel(Xs[0], Ys[0]), true, true);
         for (int i = 1; i < n; i++)
            ctx.LineTo(toPixel(Xs[i], Ys[i]), true, true);
         stream.Freeze();
         if (Fill != null)
            dc.DrawGeometry(Fill, new Pen(Stroke, StrokeThickness), stream);
         else
            dc.DrawGeometry(null, new Pen(Stroke, StrokeThickness), stream);
      }
   }

   public record CircleElement : PlotElement
   {
      public double X { get; init; }
      public double Y { get; init; }
      public double Radius { get; init; }
      public Brush? Fill { get; init; }
      public Brush Stroke { get; init; } = Brushes.Black;
      public double StrokeThickness { get; init; } = 1;

      public override void Render(DrawingContext dc, Func<double, double, Point> toPixel)
      {
         if (Radius <= 0) return;
         var center = toPixel(X, Y);
         var right = toPixel(X + Radius, Y);
         double r = Math.Abs(right.X - center.X);
         var pen = new Pen(Stroke, StrokeThickness);
         dc.DrawEllipse(Fill, pen, center, r, r);
      }
   }

   public record MarkerElement : PlotElement
   {
      public double[] Xs { get; init; } = [];
      public double[] Ys { get; init; } = [];
      public Brush Fill { get; init; } = Brushes.Black;
      public double MarkerSize { get; init; } = 4;
      public string? Label { get; init; }

      public override void Render(DrawingContext dc, Func<double, double, Point> toPixel)
      {
         double half = MarkerSize / 2.0;
         int n = Math.Min(Xs.Length, Ys.Length);
         for (int i = 0; i < n; i++)
         {
            var pt = toPixel(Xs[i], Ys[i]);
            dc.DrawEllipse(Fill, null, pt, half, half);
         }
      }
   }

   /// <summary>
   /// Элемент рендеринга сетки фибр. Все фибры рисуются одним StreamGeometry
   /// (режим Elements) или как маркеры-точки (режим Centroids).
   /// </summary>
   public record FiberMeshElement : PlotElement
   {
      public Fiber[] Fibers          { get; init; } = [];
      public bool ShowCentroids      { get; init; } = false;
      /// <summary>Заливка ячеек сетки. null — только рёбра (Stroke), без заливки.</summary>
      public Brush? Fill             { get; init; } = Brushes.LightSteelBlue;
      public Brush CentroidFill      { get; init; } = Brushes.RoyalBlue;
      public Brush Stroke            { get; init; } = Brushes.SteelBlue;
      public double StrokeThickness  { get; init; } = 0.5;
      public double MarkerSize       { get; init; } = 4;

      public override void Render(DrawingContext dc, Func<double, double, Point> toPixel)
      {
         if (Fibers.Length == 0) return;

         if (ShowCentroids)
         {
            double half = MarkerSize / 2.0;
            foreach (var f in Fibers)
            {
               var pt = toPixel(f.X, f.Y);
               dc.DrawEllipse(CentroidFill, null, pt, half, half);
            }
         }
         else
         {
            var stream = new StreamGeometry { FillRule = FillRule.EvenOdd };
            using (var ctx = stream.Open())
            {
               foreach (var f in Fibers)
               {
                  if (f.WKT == null) continue;
                  WktHelper.ParseWKTPolygon(f.WKT, out var xs, out var ys, out var holeXs, out var holeYs);
                  if (xs.Count < 3) continue;
                  ctx.BeginFigure(toPixel(xs[0], ys[0]), isFilled: true, isClosed: true);
                  for (int i = 1; i < xs.Count; i++)
                     ctx.LineTo(toPixel(xs[i], ys[i]), isStroked: true, isSmoothJoin: false);
                  // Отверстия добавляются отдельными фигурами; EvenOdd превращает их в вырезы
                  for (int h = 0; h < holeXs.Count; h++)
                  {
                     var hx = holeXs[h]; var hy = holeYs[h];
                     if (hx.Count < 3) continue;
                     ctx.BeginFigure(toPixel(hx[0], hy[0]), isFilled: true, isClosed: true);
                     for (int i = 1; i < hx.Count; i++)
                        ctx.LineTo(toPixel(hx[i], hy[i]), isStroked: false, isSmoothJoin: false);
                  }
               }
            }
            stream.Freeze();
            dc.DrawGeometry(Fill, new Pen(Stroke, StrokeThickness), stream);
         }
      }
   }
}
