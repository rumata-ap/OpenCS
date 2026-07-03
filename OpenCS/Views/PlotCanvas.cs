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
      private string? _title, _xLabel, _yLabel;
      private PlotSettings _settings = PlotSettings.Default;

      private double _scale   = 200;
      private double _originX = 0;
      private double _originY = 0;

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

         if (_hasBounds)
            ComputeFit(w, h);

         if (_settings.ShowGrid && _hasBounds)
            DrawGrid(dc, w, h);

         if (_elements != null && _elements.Count > 0 && _hasBounds)
         {
            foreach (var el in _elements)
               el.Render(dc, ToScreen);

            if (_picked.HasValue)
            {
               var p = _picked.Value;
               var pt = new Point(p.px, p.py);
               var hlb = ParseBrush(_settings.Highlight);
               dc.DrawEllipse(hlb, new Pen(hlb, 1.5), pt, 6, 6);

               var ft = new FormattedText($"({p.x:F4}; {p.y:F2})",
                  CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                  new Typeface("Segoe UI"), 11, Brushes.Black, 1.0);
               var bg = new GeometryDrawing(Brushes.LightYellow, new Pen(Brushes.Gray, 0.5),
                  new RectangleGeometry(new Rect(pt.X + 7, pt.Y - ft.Height - 4, ft.Width + 6, ft.Height + 4), 3, 3));
               bg.Freeze();
               dc.DrawDrawing(bg);
               dc.DrawText(ft, new Point(pt.X + 10, pt.Y - ft.Height - 2));
            }

            if (_settings.ShowPointLabels)
               DrawPointLabels(dc);
         }

         if (_hasBounds)
            DrawAxes(dc, w, h);

         if (_title != null) DrawTitle(dc, w);
      }

      private void ComputeFit(double w, double h)
      {
         double xMin = _xMin, xMax = _xMax;
         double yMin = _yMin, yMax = _yMax;

         double padX = (xMax - xMin) * 0.05 + 0.0001;
         double padY = (yMax - yMin) * 0.05 + 0.0001;
         xMin -= padX; xMax += padX;
         yMin -= padY; yMax += padY;

         double sx = w / (xMax - xMin);
         double sy = h / (yMax - yMin);
         _scale = Math.Min(sx, sy);

         double modelW = w / _scale;
         double modelH = h / _scale;
         _originX = xMin - (modelW - (xMax - xMin)) / 2;
         _originY = yMin - (modelH - (yMax - yMin)) / 2;
      }

      private void DrawPointLabels(DrawingContext dc)
      {
         if (_elements == null) return;
         var ftBrush = ParseBrush(_settings.Text);
         var typeface = new Typeface("Segoe UI");

         foreach (var el in _elements)
         {
            if (el is MarkerElement m)
            {
               int n = Math.Min(m.Xs.Length, m.Ys.Length);
               for (int i = 0; i < n; i++)
               {
                  var pt = ToScreen(m.Xs[i], m.Ys[i]);
                  var ft = new FormattedText($"({m.Xs[i]:F4}; {m.Ys[i]:F2})",
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
                  var pt = ToScreen(s.Xs[0], s.Ys[0]);
                  var ft = new FormattedText($"({s.Xs[0]:F4}; {s.Ys[0]:F2})",
                     CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                     typeface, _settings.FontSize, ftBrush, 1.0);
                  dc.DrawText(ft, new Point(pt.X + 5, pt.Y - ft.Height - 3));
               }
            }
         }
      }

      private void DrawGrid(DrawingContext dc, double w, double h)
      {
         var settings = _settings;
         if (!settings.ShowGrid) return;

         double xMin = _originX;
         double xMax = _originX + w / _scale;
         double yMin = _originY;
         double yMax = _originY + h / _scale;

         var ticksX = NiceTicks(xMin, xMax, settings.TickCount);
         var ticksY = NiceTicks(yMin, yMax, settings.TickCount);

         var brush = ParseBrush(settings.Grid);
         var pen = new Pen(brush, settings.GridThickness);
         pen.DashStyle = DashStyles.Dot;

         foreach (var x in ticksX)
         {
            double px = ToScreen(x, 0).X;
            if (px > 0 && px < w)
               dc.DrawLine(pen, new Point(px, 0), new Point(px, h));
         }
         foreach (var y in ticksY)
         {
            double py = ToScreen(0, y).Y;
            if (py > 0 && py < h)
               dc.DrawLine(pen, new Point(0, py), new Point(w, py));
         }
      }

      static double[] NiceTicks(double min, double max, int targetCount = 6)
      {
         if (max - min < 1e-12) return [min];
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
         _picked = null;
         InvalidateVisual();
      }

      private void DrawAxes(DrawingContext dc, double w, double h)
      {
         var settings = _settings;

         double xMin = _originX;
         double xMax = _originX + w / _scale;
         double yMin = _originY;
         double yMax = _originY + h / _scale;

         var brush = ParseBrush(settings.AxesColor);
         var axisPen = new Pen(brush, 1);
         var tickPen = new Pen(brush, 0.8);
         var fontSize = settings.AxesFontSize;
         var typeface = new Typeface("Segoe UI");

         double axisPxX, axisPxY;
         if (settings.AxesAtOrigin)
         {
            axisPxX = Clamp(ToScreen(0, 0).X, 0, w);
            axisPxY = Clamp(ToScreen(0, 0).Y, 0, h);
         }
         else
         {
            axisPxX = 0;
            axisPxY = h;
         }

         dc.DrawLine(axisPen, new Point(0, axisPxY), new Point(w, axisPxY));
         dc.DrawLine(axisPen, new Point(axisPxX, 0), new Point(axisPxX, h));

         if (!settings.ShowAxesValues) return;

         var ticksX = NiceTicks(xMin, xMax, settings.TickCount);
         var ticksY = NiceTicks(yMin, yMax, settings.TickCount);

         const double tickLen = 4;
         const double gap = 4;

         foreach (var t in ticksX)
         {
            var sp = ToScreen(t, 0);
            double px = sp.X;
            if (px < 0 || px > w) continue;
            double ty = axisPxY;
            dc.DrawLine(tickPen, new Point(px, ty - tickLen), new Point(px, ty + tickLen));
            var label = FormatTick(t);
            var ft = new FormattedText(label,
               CultureInfo.CurrentCulture,
               FlowDirection.LeftToRight, typeface, fontSize, brush, 96);
            double lx = px - ft.Width / 2;
            double ly;
            if (ty + tickLen + gap + ft.Height <= h)
               ly = ty + tickLen + gap;
            else
               ly = ty - tickLen - gap - ft.Height;
            if (lx < 0) lx = 0;
            if (lx + ft.Width > w) lx = w - ft.Width;
            if (ly < 0) ly = 0;
            if (ly + ft.Height > h) ly = h - ft.Height;
            dc.DrawText(ft, new Point(lx, ly));
         }

         foreach (var t in ticksY)
         {
            var sp = ToScreen(0, t);
            double py = sp.Y;
            if (py < 0 || py > h) continue;
            double tx = axisPxX;
            dc.DrawLine(tickPen, new Point(tx - tickLen, py), new Point(tx + tickLen, py));
            var label = FormatTick(t);
            var ft = new FormattedText(label,
               CultureInfo.CurrentCulture,
               FlowDirection.LeftToRight, typeface, fontSize, brush, 96);
            double lx, ly = py - ft.Height / 2;
            if (tx - ft.Width - tickLen - gap >= 0)
               lx = tx - ft.Width - tickLen - gap;
            else
               lx = tx + tickLen + gap;
            if (lx < 0) lx = 0;
            if (lx + ft.Width > w) lx = w - ft.Width;
            if (ly < 0) ly = 0;
            if (ly + ft.Height > h) ly = h - ft.Height;
            dc.DrawText(ft, new Point(lx, ly));
         }

         if (_xLabel != null)
         {
            var ft = new FormattedText(_xLabel, CultureInfo.CurrentCulture,
               FlowDirection.LeftToRight, typeface, 11, brush, 96);
            dc.DrawText(ft, new Point((w - ft.Width) / 2, h - ft.Height - 2));
         }
         if (_yLabel != null)
         {
            var ft = new FormattedText(_yLabel, CultureInfo.CurrentCulture,
               FlowDirection.LeftToRight, typeface, 11, brush, 96);
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

      private Point ToScreen(double mx, double my)
         => new(_scale * (mx - _originX),
                RenderSize.Height - _scale * (my - _originY));

      private (double X, double Y) ToModel(Point sp)
         => (sp.X / _scale + _originX,
             (RenderSize.Height - sp.Y) / _scale + _originY);

      protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
      {
         base.OnMouseLeftButtonDown(e);
         if (!_settings.ShowTooltips || !_hasBounds || _elements == null) return;

         var pos = e.GetPosition(this);

         double bestDist = 20 * 20;
         double bestX = 0, bestY = 0, bestPx = 0, bestPy = 0;

         foreach (var el in _elements)
         {
            if (el is MarkerElement m)
            {
               int n = Math.Min(m.Xs.Length, m.Ys.Length);
               for (int i = 0; i < n; i++)
               {
                  var pt = ToScreen(m.Xs[i], m.Ys[i]);
                  double d = (pos.X - pt.X) * (pos.X - pt.X) + (pos.Y - pt.Y) * (pos.Y - pt.Y);
                  if (d < bestDist) { bestDist = d; bestX = m.Xs[i]; bestY = m.Ys[i]; bestPx = pt.X; bestPy = pt.Y; }
               }
            }
            else if (el is ScatterElement s)
            {
               int n = Math.Min(s.Xs.Length, s.Ys.Length);
               for (int i = 0; i < n; i++)
               {
                  var pt = ToScreen(s.Xs[i], s.Ys[i]);
                  double d = (pos.X - pt.X) * (pos.X - pt.X) + (pos.Y - pt.Y) * (pos.Y - pt.Y);
                  if (d < bestDist) { bestDist = d; bestX = s.Xs[i]; bestY = s.Ys[i]; bestPx = pt.X; bestPy = pt.Y; }
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

      private static double Clamp(double v, double lo, double hi)
         => v < lo ? lo : v > hi ? hi : v;
   }
}
