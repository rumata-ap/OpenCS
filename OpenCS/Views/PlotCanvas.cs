using OpenCS.Utilites;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace OpenCS.Views
{
   public class PlotCanvas : FrameworkElement
   {
      private IReadOnlyList<PlotElement>? _elements;
      private double _xMin, _xMax, _yMin, _yMax;
      private bool _hasBounds;
      private bool _squareAxes;
      private string? _title, _xLabel, _yLabel;
      private PlotSettings _settings = PlotSettings.Default;

      private (double x, double y, double px, double py)? _picked;

      public PlotCanvas()
      {
         ClipToBounds = true;
         IsHitTestVisible = true;
      }

      public void ApplySettings(PlotSettings s)
      {
         _settings = s;
         InvalidateVisual();
      }

      protected override void OnRender(DrawingContext dc)
      {
         base.OnRender(dc);

         double w = RenderSize.Width;
         double h = RenderSize.Height;
         if (w < 2 || h < 2) return;

         dc.DrawRectangle(ParseBrush(_settings.Background), null, new Rect(0, 0, w, h));

         double[]? gridTX = null, gridTY = null;
         if (_hasBounds)
         {
            gridTX = NiceTicks(_xMin, _xMax, _settings.TickCount);
            gridTY = NiceTicks(_yMin, _yMax, _settings.TickCount);
         }

         if (_settings.ShowGrid) DrawGrid(dc, w, h, gridTX, gridTY);

         if (_elements != null && _elements.Count > 0 && _hasBounds)
         {
            double xMin = _xMin, xMax = _xMax;
            double yMin = _yMin, yMax = _yMax;

            double padX = (xMax - xMin) * 0.05;
            double padY = (yMax - yMin) * 0.05;
            xMin -= padX; xMax += padX;
            yMin -= padY; yMax += padY;

            double margin = 40;
            double pw = w - 2 * margin;
            double ph = h - 2 * margin;
            double dataW = xMax - xMin;
            double dataH = yMax - yMin;

            if (_squareAxes)
            {
               double aspect = pw / ph;
               double dataAspect = dataW / dataH;
               if (dataAspect > aspect) { var c = (yMin + yMax) / 2; var nh = dataW / aspect; yMin = c - nh / 2; yMax = c + nh / 2; dataH = yMax - yMin; }
               else if (dataAspect < aspect) { var c = (xMin + xMax) / 2; var nw = dataH * aspect; xMin = c - nw / 2; xMax = c + nw / 2; dataW = xMax - xMin; }
            }

            double sx = pw / dataW;
            double sy = -ph / dataH;
            double ox = margin - xMin * sx;
            double oy = margin + ph - yMin * sy;

            Point ToPixel(double x, double y) => new Point(x * sx + ox, y * sy + oy);

            foreach (var el in _elements)
               el.Render(dc, ToPixel);

            if (_picked.HasValue)
            {
               var p = _picked.Value;
               var pt = new Point(p.px, p.py);
               var hlb = ParseBrush(_settings.Highlight);
               dc.DrawEllipse(hlb, new Pen(hlb, 1.5), pt, 6, 6);

               var scX = _settings.ScaleX; var scY = _settings.ScaleY;
            var ft = new FormattedText($"({p.x * scX:F4}; {p.y * scY:F2})",
                  CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                  new Typeface("Segoe UI"), 11, Brushes.Black, 1.0);
               var bg = new GeometryDrawing(Brushes.LightYellow, new Pen(Brushes.Gray, 0.5),
                  new RectangleGeometry(new Rect(pt.X + 7, pt.Y - ft.Height - 4, ft.Width + 6, ft.Height + 4), 3, 3));
               bg.Freeze();
               dc.DrawDrawing(bg);
               dc.DrawText(ft, new Point(pt.X + 10, pt.Y - ft.Height - 2));
            }

            if (_settings.ShowPointLabels)
               DrawPointLabels(dc, ToPixel);
         }

         if (_hasBounds)
            DrawAxes(dc, w, h, gridTX ?? [], gridTY ?? []);
         else
            DrawAxes(dc, w, h, [], []);

         if (_title != null) DrawTitle(dc, w);
      }

      private void DrawPointLabels(DrawingContext dc, Func<double, double, Point> toPixel)
      {
         if (_elements == null) return;
         var ftBrush = ParseBrush(_settings.Text);
         var typeface = new Typeface("Segoe UI");
         var scX = _settings.ScaleX; var scY = _settings.ScaleY;

         foreach (var el in _elements)
         {
            if (el is MarkerElement m)
            {
               int n = Math.Min(m.Xs.Length, m.Ys.Length);
               for (int i = 0; i < n; i++)
               {
                  var pt = toPixel(m.Xs[i], m.Ys[i]);
                  var ft = new FormattedText($"({m.Xs[i] * scX:F4}; {m.Ys[i] * scY:F2})",
                     CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                     typeface, _settings.FontSize, ftBrush, 1.0);
                  dc.DrawText(ft, new Point(pt.X + 5, pt.Y - ft.Height - 3));
               }
            }
            else if (el is ScatterElement s)
            {
               int n = Math.Min(s.Xs.Length, s.Ys.Length);
               if (n > 0)
               {
                  var pt = toPixel(s.Xs[0], s.Ys[0]);
                  var ft = new FormattedText($"({s.Xs[0] * scX:F4}; {s.Ys[0] * scY:F2})",
                     CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                     typeface, _settings.FontSize, ftBrush, 1.0);
                  dc.DrawText(ft, new Point(pt.X + 5, pt.Y - ft.Height - 3));
               }
            }
         }
      }

      private void DrawGrid(DrawingContext dc, double w, double h, double[]? ticksX, double[]? ticksY)
      {
         var margin = 40.0;
         var pen = new Pen(ParseBrush(_settings.Grid), _settings.GridThickness);
         pen.DashStyle = DashStyles.Dot;
         if (ticksX != null)
            foreach (var x in ticksX)
            {
               var px = margin + (x - _xMin + (_xMax - _xMin) * 0.05) / ((_xMax - _xMin) * 1.1) * (w - 2 * margin);
               if (px > margin && px < w - margin)
                  dc.DrawLine(pen, new Point(px, margin), new Point(px, h - margin));
            }
         if (ticksY != null)
            foreach (var y in ticksY)
            {
               var py = margin + (h - 2 * margin) - (y - _yMin + (_yMax - _yMin) * 0.05) / ((_yMax - _yMin) * 1.1) * (h - 2 * margin);
               if (py > margin && py < h - margin)
                  dc.DrawLine(pen, new Point(margin, py), new Point(w - margin, py));
            }
      }

      static double[] NiceTicks(double min, double max, int targetCount = 6)
      {
         if (max == min) return [min];
         double range = max - min;
         double roughStep = range / targetCount;
         double exponent = Math.Floor(Math.Log10(roughStep));
         double fraction = roughStep / Math.Pow(10, exponent);
         double niceStep = fraction <= 1.5 ? 1 : fraction <= 3 ? 2 : fraction <= 7 ? 5 : 10;
         niceStep *= Math.Pow(10, exponent);

         double first = Math.Ceiling(min / niceStep) * niceStep;
         var list = new List<double>();
         for (double v = first; v <= max + niceStep * 0.5; v += niceStep)
            list.Add(v);
         return list.ToArray();
      }

      public void Draw(
          IReadOnlyList<PlotElement> elements,
          double xMin, double xMax, double yMin, double yMax,
          bool squareAxes = false,
          string? xLabel = null,
          string? yLabel = null,
          string? title = null)
      {
         _elements = elements;
         _xMin = xMin; _xMax = xMax;
         _yMin = yMin; _yMax = yMax;
         _hasBounds = true;
         _squareAxes = squareAxes;
         _xLabel = xLabel;
         _yLabel = yLabel;
         _title = title;
         InvalidateVisual();
      }

      public void Clear()
      {
         _elements = null;
         _hasBounds = false;
         _title = _xLabel = _yLabel = null;
         _squareAxes = false;
         _picked = null;
         InvalidateVisual();
      }

      private void DrawAxes(DrawingContext dc, double w, double h, double[] ticksX, double[] ticksY)
      {
         var margin = 40.0;
         var padX = (_xMax - _xMin) * 0.05;
         var padY = (_yMax - _yMin) * 0.05;
         var xMinP = _xMin - padX;
         var xMaxP = _xMax + padX;
         var yMinP = _yMin - padY;
         var yMaxP = _yMax + padY;
         double pw = w - 2 * margin, ph = h - 2 * margin;

         double ToPxX(double x) => margin + (x - xMinP) / (xMaxP - xMinP) * pw;
         double ToPxY(double y) => margin + ph - (y - yMinP) / (yMaxP - yMinP) * ph;

         var axisPen = new Pen(ParseBrush(_settings.AxesColor), 1);
         var tickPen = new Pen(ParseBrush(_settings.AxesColor), 0.5);
         var axesBrush = ParseBrush(_settings.AxesColor);
         double fontSize = _settings.AxesFontSize;
         var typeface = new Typeface("Segoe UI");

         double axisX0 = _settings.AxesAtOrigin ? ToPxX(0).Clamp(margin, w - margin) : margin;
         double axisY0 = _settings.AxesAtOrigin ? ToPxY(0).Clamp(margin, h - margin) : h - margin;

         // РћСЃСЊ X
         dc.DrawLine(axisPen, new Point(margin, axisY0), new Point(w - margin, axisY0));
         if (_settings.ShowAxesValues && ticksX != null)
         {
            foreach (var t in ticksX)
            {
               double px = ToPxX(t);
               if (px < margin - 2 || px > w - margin + 2) continue;
               dc.DrawLine(tickPen, new Point(px, axisY0 - 4), new Point(px, axisY0 + 4));
               var ft = new FormattedText(FormatTick(t * _settings.ScaleX), CultureInfo.CurrentCulture,
                  FlowDirection.LeftToRight, typeface, fontSize, axesBrush, 1.0);
               double textY = _settings.AxesAtOrigin ? axisY0 + 5 : axisY0 + 3;
               dc.DrawText(ft, new Point(px - ft.Width / 2, textY));
            }
         }

         // РћСЃСЊ Y
         dc.DrawLine(axisPen, new Point(axisX0, margin), new Point(axisX0, h - margin));
         if (_settings.ShowAxesValues && ticksY != null)
         {
            foreach (var t in ticksY)
            {
               double py = ToPxY(t);
               if (py < margin - 2 || py > h - margin + 2) continue;
               dc.DrawLine(tickPen, new Point(axisX0 - 4, py), new Point(axisX0 + 4, py));
               var tv = t * _settings.ScaleY;
               var label = Math.Abs(tv) < 0.01 ? tv.ToString("F6") : Math.Abs(tv) < 1 ? tv.ToString("F4") : Math.Abs(tv) < 100 ? tv.ToString("F2") : tv.ToString("F0");
               var ft = new FormattedText(label, CultureInfo.CurrentCulture,
                  FlowDirection.LeftToRight, typeface, fontSize, axesBrush, 1.0);
               double textX = _settings.AxesAtOrigin ? axisX0 - ft.Width - 8 : margin - ft.Width - 5;
               dc.DrawText(ft, new Point(textX, py - ft.Height / 2));
            }
         }

         // РџРѕРґРїРёСЃРё РѕСЃРµР№ (X/Y label)
         if (_xLabel != null)
         {
            var ft = new FormattedText(_xLabel, CultureInfo.CurrentCulture,
               FlowDirection.LeftToRight, typeface, 11, axesBrush, 1.0);
            dc.DrawText(ft, new Point((w - ft.Width) / 2, h - ft.Height - 2));
         }
         if (_yLabel != null)
         {
            var ft = new FormattedText(_yLabel, CultureInfo.CurrentCulture,
               FlowDirection.LeftToRight, typeface, 11, axesBrush, 1.0);
            dc.PushTransform(new RotateTransform(-90));
            dc.DrawText(ft, new Point(-h / 2 - ft.Width / 2, 2));
            dc.Pop();
         }
      }

      private void DrawTitle(DrawingContext dc, double w)
      {
         var ft = new FormattedText(_title, CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, new Typeface("Segoe UI"), 13,
            ParseBrush(_settings.Text), 1.0);
         dc.DrawText(ft, new Point((w - ft.Width) / 2, 4));
      }

      protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
      {
         base.OnMouseLeftButtonDown(e);
         if (!_settings.ShowTooltips || !_hasBounds || _elements == null) return;

         var pos = e.GetPosition(this);
         var (sx, sy, ox, oy) = GetTransform();
         if (sx == 0 || sy == 0) return;

         double bestDist = 20 * 20;
         double bestX = 0, bestY = 0, bestPx = 0, bestPy = 0;

         foreach (var el in _elements)
         {
            if (el is MarkerElement m)
            {
               int n = Math.Min(m.Xs.Length, m.Ys.Length);
               for (int i = 0; i < n; i++)
               {
                  double px = m.Xs[i] * sx + ox;
                  double py = m.Ys[i] * sy + oy;
                  double d = (pos.X - px) * (pos.X - px) + (pos.Y - py) * (pos.Y - py);
                  if (d < bestDist) { bestDist = d; bestX = m.Xs[i]; bestY = m.Ys[i]; bestPx = px; bestPy = py; }
               }
            }
            else if (el is ScatterElement s)
            {
               int n = Math.Min(s.Xs.Length, s.Ys.Length);
               for (int i = 0; i < n; i++)
               {
                  double px = s.Xs[i] * sx + ox;
                  double py = s.Ys[i] * sy + oy;
                  double d = (pos.X - px) * (pos.X - px) + (pos.Y - py) * (pos.Y - py);
                  if (d < bestDist) { bestDist = d; bestX = s.Xs[i]; bestY = s.Ys[i]; bestPx = px; bestPy = py; }
               }
            }
         }

         if (bestDist < 20 * 20)
         {
            _picked = (bestX, bestY, bestPx, bestPy);
            InvalidateVisual();
         }
      }

      protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
      {
         base.OnMouseLeftButtonUp(e);
         if (_picked.HasValue)
         {
            _picked = null;
            InvalidateVisual();
         }
      }

      protected override void OnMouseLeave(MouseEventArgs e)
      {
         base.OnMouseLeave(e);
         if (_picked.HasValue)
         {
            _picked = null;
            InvalidateVisual();
         }
      }

      private (double sx, double sy, double ox, double oy) GetTransform()
      {
         double w = RenderSize.Width;
         double h = RenderSize.Height;
         if (w < 2 || h < 2) return (0, 0, 0, 0);

         double xMin = _xMin, xMax = _xMax;
         double yMin = _yMin, yMax = _yMax;
         double padX = (xMax - xMin) * 0.05;
         double padY = (yMax - yMin) * 0.05;
         xMin -= padX; xMax += padX;
         yMin -= padY; yMax += padY;

         double margin = 40;
         double pw = w - 2 * margin;
         double ph = h - 2 * margin;

         if (_squareAxes)
         {
            double aspect = pw / ph;
            double dataWp = xMax - xMin;
            double dataHp = yMax - yMin;
            double dataAspect = dataWp / dataHp;
            if (dataAspect > aspect) { var c = (yMin + yMax) / 2; var nh = dataWp / aspect; yMin = c - nh / 2; yMax = c + nh / 2; }
            else if (dataAspect < aspect) { var c = (xMin + xMax) / 2; var nw = dataHp * aspect; xMin = c - nw / 2; xMax = c + nw / 2; }
         }

         double sx = pw / (xMax - xMin);
         double sy = -ph / (yMax - yMin);
         double ox = margin - xMin * sx;
         double oy = margin + ph - yMin * sy;
         return (sx, sy, ox, oy);
      }

      static string FormatTick(double v)
      {
         var av = Math.Abs(v);
         if (av == 0) return "0";
         if (av < 0.001) return v.ToString("E2");
         if (av < 0.01) return v.ToString("F5");
         if (av < 1) return v.ToString("F4");
         if (av < 100) return v.ToString("F2");
         if (av < 10000) return v.ToString("F0");
         return v.ToString("E2");
      }

      private static Brush ParseBrush(string hex)
      {
         try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
         catch { return Brushes.White; }
      }
   }

   internal static class Extensions
   {
      public static double Clamp(this double v, double min, double max) => v < min ? min : v > max ? max : v;
   }
}
