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

      private double _scaleX = 200;
      private double _scaleY = 200;
      private double _originX = 0;
      private double _originY = 0;
      private bool _squareAxes;
      private bool _showOriginXAxis = true;
      private bool _showOriginYAxis = true;

      // Область холста, отведённая под сами данные (без полей под подписи осей) —
      // вычисляется заново на каждый OnRender в ComputePlotRect.
      private Rect _plotRect;

      // Ручной масштаб (Ctrl+колесо) — переопределяет автоподбор границ по данным до
      // следующего Draw() с новыми данными (см. Draw()).
      private double? _zoomXMin, _zoomXMax, _zoomYMin, _zoomYMax;

      const double ZoomStep = 1.15;
      const double MinZoomSpanFraction = 1e-4;

      private (double x, double y, double px, double py)? _picked;

      public PlotCanvas()
      {
         ClipToBounds = true;
         IsHitTestVisible = true;
         Focusable = true;
      }

      public void ApplySettings(PlotSettings s)
      {
         _settings = s;
         InvalidateVisual();
      }

      /// <summary>Настраивает видимость дополнительных опорных осей в начале координат.</summary>
      public void SetOriginReferenceAxesVisibility(bool showXAxis, bool showYAxis)
      {
         _showOriginXAxis = showXAxis;
         _showOriginYAxis = showYAxis;
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
         {
            _plotRect = ComputePlotRect(w, h);
            ComputeFit();
         }
         else
         {
            _plotRect = new Rect(0, 0, w, h);
         }

         if (_settings.ShowGrid && _hasBounds)
            DrawGrid(dc);

         if (_elements != null && _elements.Count > 0 && _hasBounds)
         {
            dc.PushClip(new RectangleGeometry(_plotRect));
            foreach (var el in _elements)
               el.Render(dc, ToScreen);
            dc.Pop();

            if (_picked.HasValue)
            {
               var p = _picked.Value;
               var pt = new Point(p.px, p.py);
               var hlb = ParseBrush(_settings.Highlight);
               dc.DrawEllipse(hlb, new Pen(hlb, 1.5), pt, 6, 6);

               var ft = new FormattedText(FormatPointLabel(p.x, p.y),
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

      /// <summary>
      /// Прямоугольник холста, отведённый под сами данные — по краям (снизу под подписи
      /// значений X и заголовок оси X, слева под подписи значений Y и заголовок оси Y, сверху
      /// под заголовок графика) вычитаются поля, чтобы подписи никогда не рисовались поверх
      /// кривых внутри графика, а были снаружи области данных.
      /// </summary>
      private Rect ComputePlotRect(double w, double h)
      {
         const double edgePad = 6;
         const double tickLen = 4;
         const double gap = 4;
         const double tickLabelHeight = 16;
         const double titleAreaHeight = 15;

         double left = edgePad, right = edgePad + 4, top = edgePad, bottom = edgePad;

         if (_title != null) top += titleAreaHeight;

         if (_settings.ShowAxesValues)
         {
            double padX = (_xMax - _xMin) * 0.05 + 0.0001;
            double padY = (_yMax - _yMin) * 0.05 + 0.0001;
            var ticksY = NiceTicks((_yMin - padY), (_yMax + padY), _settings.TickCount);
            var typeface = new Typeface("Segoe UI");
            double maxTickW = 0;
            foreach (var t in ticksY)
            {
               var ft = new FormattedText(FormatTick(t), CultureInfo.CurrentCulture,
                  FlowDirection.LeftToRight, typeface, _settings.AxesFontSize, Brushes.Black, 96);
               if (ft.Width > maxTickW) maxTickW = ft.Width;
            }
            left += tickLen + gap + maxTickW;
            bottom += tickLen + gap + tickLabelHeight;
         }

         if (_xLabel != null) bottom += gap + titleAreaHeight;
         if (_yLabel != null) left += gap + titleAreaHeight;

         double pw = Math.Max(10, w - left - right);
         double ph = Math.Max(10, h - top - bottom);
         return new Rect(left, top, pw, ph);
      }

      private void ComputeFit()
      {
         double xMin = _zoomXMin ?? _xMin, xMax = _zoomXMax ?? _xMax;
         double yMin = _zoomYMin ?? _yMin, yMax = _zoomYMax ?? _yMax;

         double padX = (xMax - xMin) * 0.05 + 0.0001;
         double padY = (yMax - yMin) * 0.05 + 0.0001;
         xMin -= padX; xMax += padX;
         yMin -= padY; yMax += padY;

         double sx = _plotRect.Width / (xMax - xMin);
         double sy = _plotRect.Height / (yMax - yMin);
         _scaleX = _squareAxes ? Math.Min(sx, sy) : sx;
         _scaleY = _squareAxes ? Math.Min(sx, sy) : sy;

         double modelW = _plotRect.Width / _scaleX;
         double modelH = _plotRect.Height / _scaleY;
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
                  var ft = new FormattedText(FormatPointLabel(m.Xs[i], m.Ys[i]),
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
                  var ft = new FormattedText(FormatPointLabel(s.Xs[0], s.Ys[0]),
                     CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                     typeface, _settings.FontSize, ftBrush, 1.0);
                  dc.DrawText(ft, new Point(pt.X + 5, pt.Y - ft.Height - 3));
               }
            }
         }
      }

      private void DrawGrid(DrawingContext dc)
      {
         var settings = _settings;
         if (!settings.ShowGrid) return;

         double xMin = _originX;
         double xMax = _originX + _plotRect.Width / _scaleX;
         double yMin = _originY;
         double yMax = _originY + _plotRect.Height / _scaleY;

         var ticksX = NiceTicks(xMin, xMax, settings.TickCount);
         var ticksY = NiceTicks(yMin, yMax, settings.TickCount);

         var brush = ParseBrush(settings.Grid);
         var pen = new Pen(brush, settings.GridThickness);
         pen.DashStyle = DashStyles.Dot;

         foreach (var x in ticksX)
         {
            double px = ToScreen(x, 0).X;
            if (px >= _plotRect.Left && px <= _plotRect.Right)
               dc.DrawLine(pen, new Point(px, _plotRect.Top), new Point(px, _plotRect.Bottom));
         }
         foreach (var y in ticksY)
         {
            double py = ToScreen(0, y).Y;
            if (py >= _plotRect.Top && py <= _plotRect.Bottom)
               dc.DrawLine(pen, new Point(_plotRect.Left, py), new Point(_plotRect.Right, py));
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
         _squareAxes = squareAxes;
         _hasBounds = true;
         _xLabel = xLabel;
         _yLabel = yLabel;
         _title = title;
         _zoomXMin = _zoomXMax = _zoomYMin = _zoomYMax = null;
         InvalidateVisual();
      }

      public void Clear()
      {
         _elements = null;
         _hasBounds = false;
         _squareAxes = false;
         _title = _xLabel = _yLabel = null;
         _picked = null;
         _zoomXMin = _zoomXMax = _zoomYMin = _zoomYMax = null;
         InvalidateVisual();
      }

      private void DrawAxes(DrawingContext dc, double w, double h)
      {
         var settings = _settings;

         double xMin = _originX;
         double xMax = _originX + _plotRect.Width / _scaleX;
         double yMin = _originY;
         double yMax = _originY + _plotRect.Height / _scaleY;

         var brush = ParseBrush(settings.AxesColor);
         var axisPen = new Pen(brush, 1);
         var tickPen = new Pen(brush, 0.8);
         var fontSize = settings.AxesFontSize;
         var typeface = new Typeface("Segoe UI");

         double axisPxX, axisPxY;
         if (settings.AxesAtOrigin)
         {
            axisPxX = Clamp(ToScreen(0, 0).X, _plotRect.Left, _plotRect.Right);
            axisPxY = Clamp(ToScreen(0, 0).Y, _plotRect.Top, _plotRect.Bottom);
         }
         else
         {
            axisPxX = _plotRect.Left;
            axisPxY = _plotRect.Bottom;
         }

         dc.DrawLine(axisPen, new Point(_plotRect.Left, axisPxY), new Point(_plotRect.Right, axisPxY));
         dc.DrawLine(axisPen, new Point(axisPxX, _plotRect.Top), new Point(axisPxX, _plotRect.Bottom));

         DrawOriginReferenceAxes(dc, w, h);

         if (!settings.ShowAxesValues) return;

         var ticksX = NiceTicks(xMin, xMax, settings.TickCount);
         var ticksY = NiceTicks(yMin, yMax, settings.TickCount);

         const double tickLen = 4;
         const double gap = 4;

         // Подписи значений всегда рисуются СНАРУЖИ области данных (в отведённых полях
         // ComputePlotRect) — независимо от того, где физически проходит линия оси
         // (например, при AxesAtOrigin ось может пересекать данные посередине).
         foreach (var t in ticksX)
         {
            double px = ToScreen(t, 0).X;
            if (px < _plotRect.Left || px > _plotRect.Right) continue;
            dc.DrawLine(tickPen, new Point(px, axisPxY - tickLen), new Point(px, axisPxY + tickLen));
            var label = FormatTick(t);
            var ft = new FormattedText(label,
               CultureInfo.CurrentCulture,
               FlowDirection.LeftToRight, typeface, fontSize, brush, 96);
            double lx = Clamp(px - ft.Width / 2, 0, Math.Max(0, w - ft.Width));
            double ly = _plotRect.Bottom + tickLen + gap;
            dc.DrawText(ft, new Point(lx, ly));
         }

         foreach (var t in ticksY)
         {
            double py = ToScreen(0, t).Y;
            if (py < _plotRect.Top || py > _plotRect.Bottom) continue;
            dc.DrawLine(tickPen, new Point(axisPxX - tickLen, py), new Point(axisPxX + tickLen, py));
            var label = FormatTick(t);
            var ft = new FormattedText(label,
               CultureInfo.CurrentCulture,
               FlowDirection.LeftToRight, typeface, fontSize, brush, 96);
            double lx = _plotRect.Left - tickLen - gap - ft.Width;
            double ly = Clamp(py - ft.Height / 2, 0, Math.Max(0, h - ft.Height));
            dc.DrawText(ft, new Point(lx, ly));
         }

         // Заголовки осей — за подписями значений, на самом краю холста (дальше от данных,
         // чем сами числа), см. запрос "надписи осей с противоположных сторон от осей".
         if (_xLabel != null)
         {
            var ft = new FormattedText(_xLabel, CultureInfo.CurrentCulture,
               FlowDirection.LeftToRight, typeface, 11, brush, 96);
            dc.DrawText(ft, new Point(_plotRect.Left + (_plotRect.Width - ft.Width) / 2, h - ft.Height - 2));
         }
         if (_yLabel != null)
         {
            var ft = new FormattedText(_yLabel, CultureInfo.CurrentCulture,
               FlowDirection.LeftToRight, typeface, 11, brush, 96);
            dc.PushTransform(new RotateTransform(-90));
            dc.DrawText(ft, new Point(-(_plotRect.Top + _plotRect.Height / 2) - ft.Width / 2, 2));
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

      void DrawOriginReferenceAxes(DrawingContext dc, double w, double h)
      {
         if (!_settings.ShowOriginReferenceAxes) return;

         double px0 = ToScreen(0, 0).X;
         double py0 = ToScreen(0, 0).Y;
          bool showVertical = _showOriginYAxis && px0 >= _plotRect.Left && px0 <= _plotRect.Right;
          bool showHorizontal = _showOriginXAxis && py0 >= _plotRect.Top && py0 <= _plotRect.Bottom;
         if (!showVertical && !showHorizontal) return;

         var xBrush = Brushes.ForestGreen;
         var yBrush = Brushes.RoyalBlue;
         var xPen = new Pen(xBrush, 1.4);
         var yPen = new Pen(yBrush, 1.4);
         var typeface = new Typeface("Segoe UI Semibold");
         double fontSize = Math.Max(11, _settings.AxesFontSize);
         var haloBrush = new SolidColorBrush(Color.FromArgb(230, 255, 255, 255));
         haloBrush.Freeze();
         const double outerPad = 4;
         const double lineGap = 6;
         const double haloPad = 2;

         void DrawLabel(FormattedText ft, double x, double y)
         {
            dc.DrawRectangle(haloBrush, null,
               new Rect(x - haloPad, y - haloPad, ft.Width + 2 * haloPad, ft.Height + 2 * haloPad));
            dc.DrawText(ft, new Point(x, y));
         }

         if (showHorizontal)
         {
            var ft = new FormattedText(Loc.S("AxisLabelX"), CultureInfo.CurrentCulture,
               FlowDirection.LeftToRight, typeface, fontSize, xBrush, 96);
            double ly = py0 - ft.Height - 2;
            if (ly < _plotRect.Top + outerPad) ly = Math.Min(_plotRect.Bottom - ft.Height - outerPad, py0 + 2);
            double leftLabelX = _plotRect.Left + outerPad;
            double rightLabelX = _plotRect.Right - ft.Width - outerPad;
            double lineStartX = leftLabelX + ft.Width + lineGap;
            double lineEndX = rightLabelX - lineGap;
            if (lineEndX > lineStartX)
               dc.DrawLine(xPen, new Point(lineStartX, py0), new Point(lineEndX, py0));
            DrawLabel(ft, leftLabelX, ly);
            DrawLabel(ft, rightLabelX, ly);
         }

         if (showVertical)
         {
            var ft = new FormattedText(Loc.S("AxisLabelY"), CultureInfo.CurrentCulture,
               FlowDirection.LeftToRight, typeface, fontSize, yBrush, 96);
            double lx = px0 + 4;
            if (lx + ft.Width > _plotRect.Right - outerPad) lx = Math.Max(_plotRect.Left + outerPad, px0 - ft.Width - 4);
            double topLabelY = _plotRect.Top + outerPad;
            double bottomLabelY = _plotRect.Bottom - ft.Height - outerPad;
            double lineStartY = topLabelY + ft.Height + lineGap;
            double lineEndY = bottomLabelY - lineGap;
            if (lineEndY > lineStartY)
               dc.DrawLine(yPen, new Point(px0, lineStartY), new Point(px0, lineEndY));
            DrawLabel(ft, lx, topLabelY);
            DrawLabel(ft, lx, bottomLabelY);
         }
      }

      private Point ToScreen(double mx, double my)
         => new(_plotRect.X + _scaleX * (mx - _originX),
                _plotRect.Y + _plotRect.Height - _scaleY * (my - _originY));

      private (double X, double Y) ToModel(Point sp)
         => ((sp.X - _plotRect.X) / _scaleX + _originX,
             (_plotRect.Y + _plotRect.Height - sp.Y) / _scaleY + _originY);

      protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
      {
         base.OnMouseLeftButtonDown(e);
         Focus();
         if (_hasBounds && e.ClickCount == 2)
         {
            _zoomXMin = _zoomXMax = _zoomYMin = _zoomYMax = null;
            InvalidateVisual();
            return;
         }

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

      /// <summary>
      /// Ctrl+колесо мыши — масштабирование вокруг точки под курсором (в модельных
      /// координатах), не затрагивая другие сценарии прокрутки (скролл вкладок/страницы
      /// колесом без Ctrl работает как обычно). По умолчанию растягивается только ось X
      /// (например, кривизна на графиках кривизна-момент) — ось Y всегда остаётся по
      /// полным данным, чтобы масштаб менялся именно "по ширине". Для графиков с
      /// квадратными осями (<see cref="EnableSquareAxes"/> у сервиса, например полярная
      /// диаграмма биаксиального взаимодействия) масштабируются обе оси синхронно — там
      /// раздельный зум исказил бы геометрический смысл графика. Увеличение — без
      /// ограничения, уменьшение — не дальше исходного (полного) масштаба; при
      /// достижении/превышении исходного масштаба зум по соответствующей оси сбрасывается
      /// в автоподбор. Двойной клик сбрасывает масштаб по обеим осям сразу.
      /// </summary>
      protected override void OnMouseWheel(MouseWheelEventArgs e)
      {
         base.OnMouseWheel(e);
         if (!_hasBounds) return;
         if (Keyboard.Modifiers != ModifierKeys.Control) return;

         var pos = e.GetPosition(this);
         if (pos.X < _plotRect.Left || pos.X > _plotRect.Right ||
             pos.Y < _plotRect.Top || pos.Y > _plotRect.Bottom)
         {
            e.Handled = true;
            return;
         }

         var (mx, my) = ToModel(pos);
         double factor = e.Delta > 0 ? 1.0 / ZoomStep : ZoomStep;

         bool okX = ZoomAxis(mx, factor, _originX, _originX + _plotRect.Width / _scaleX,
            _xMin, _xMax, out double? newXMin, out double? newXMax);
         if (!okX) { e.Handled = true; return; }
         _zoomXMin = newXMin; _zoomXMax = newXMax;

         if (_squareAxes)
         {
            bool okY = ZoomAxis(my, factor, _originY, _originY + _plotRect.Height / _scaleY,
               _yMin, _yMax, out double? newYMin, out double? newYMax);
            if (!okY) { e.Handled = true; return; }
            _zoomYMin = newYMin; _zoomYMax = newYMax;
         }

         InvalidateVisual();
         e.Handled = true;
      }

      /// <summary>
      /// Считает новый диапазон одной оси при масштабировании вокруг точки anchor.
      /// Возвращает false, если новый диапазон вырожден (дальше увеличивать некуда).
      /// Диапазон, равный или шире исходного (dataMin..dataMax с тем же паддингом, что и
      /// автоподбор), приводится к null (автоподбор) — уменьшение масштаба дальше исходного
      /// не идёт.
      /// </summary>
      static bool ZoomAxis(
         double anchor, double factor, double curMin, double curMax,
         double dataMin, double dataMax, out double? newMin, out double? newMax)
      {
         double newLo = anchor - (anchor - curMin) * factor;
         double newHi = anchor + (curMax - anchor) * factor;
         double fullSpan = Math.Max((dataMax - dataMin) * 1.1 + 0.0002, 1e-9);
         double newSpan = newHi - newLo;

         if (newSpan < fullSpan * MinZoomSpanFraction)
         {
            newMin = null; newMax = null;
            return false;
         }

         if (newSpan >= fullSpan)
         {
            newMin = null; newMax = null;
         }
         else
         {
            newMin = newLo; newMax = newHi;
         }
         return true;
      }

      static string FormatTick(double v)
      {
         var av = Math.Abs(v);
         if (av < 1e-12) return "0";
         if (av < 0.001) return v.ToString("E2");
         if (av < 0.01) return v.ToString("F5");
         if (av < 1) return v.ToString("F4");
         if (av < 100) return v.ToString("F2");
         if (av < 10000) return v.ToString("F0");
         return v.ToString("E2");
      }

      static string FormatPointLabel(double x, double y)
         => $"({FormatCoord(x, 4)}; {FormatCoord(y, 2)})";

      static string FormatCoord(double v, int decimals)
      {
         double rounded = Math.Round(v, decimals, MidpointRounding.AwayFromZero);
         if (rounded == 0) return "0";
         return rounded.ToString($"F{decimals}", CultureInfo.CurrentCulture);
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
