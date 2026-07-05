using CScore;

using OpenCS.Utilites;
using OpenCS.ViewModels;

using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace OpenCS.Views
{
   /// <summary>
   /// Интерактивный холст для рисования и редактирования контура мышью.
   /// </summary>
   public class ContourEditCanvas : FrameworkElement
   {
      ContourVM? _vm;
      PlotSettings _settings = PlotSettings.Default;

      double _scale = 200;
      double _originX;
      double _originY;

      StressPoint? _dragPoint;
      Point _cursorModel;
      bool _hasCursor;

      const double HitVertexPx = 10;
      const double CloseFirstPx = 12;

      static readonly Pen _bboxPen;
      static readonly Pen _rubberPen;
      static readonly Brush _vertexFill;
      static readonly Brush _vertexSelFill;
      static readonly Brush _vertexFirstFill;

      static ContourEditCanvas()
      {
         _bboxPen = new Pen(new SolidColorBrush(Color.FromRgb(148, 163, 184)), 1.2)
         { DashStyle = DashStyles.Dash };
         _bboxPen.Freeze();

         _rubberPen = new Pen(new SolidColorBrush(Color.FromRgb(107, 114, 128)), 1.2)
         { DashStyle = DashStyles.Dash };
         _rubberPen.Freeze();

         _vertexFill = new SolidColorBrush(Color.FromRgb(0, 58, 108));
         _vertexSelFill = new SolidColorBrush(Color.FromRgb(37, 99, 235));
         _vertexFirstFill = new SolidColorBrush(Color.FromRgb(5, 150, 105));
         _vertexFill.Freeze();
         _vertexSelFill.Freeze();
         _vertexFirstFill.Freeze();
      }

      public ContourEditCanvas()
      {
         Focusable = true;
         ClipToBounds = true;
      }

      public void SetVM(ContourVM vm, PlotSettings settings)
      {
         if (_vm != null)
         {
            _vm.PropertyChanged -= OnVmPropertyChanged;
            _vm.CanvasRefreshRequested -= OnCanvasRefreshRequested;
            _vm.Points.CollectionChanged -= OnPointsChanged;
         }

         _vm = vm;
         _settings = settings;

         vm.PropertyChanged += OnVmPropertyChanged;
         vm.CanvasRefreshRequested += OnCanvasRefreshRequested;
         vm.Points.CollectionChanged += OnPointsChanged;

         ApplyViewTransform();
         InvalidateVisual();
      }

      void OnVmPropertyChanged(object? s, PropertyChangedEventArgs e)
      {
         if (e.PropertyName is nameof(ContourVM.ViewWidth)
             or nameof(ContourVM.ViewHeight)
             or nameof(ContourVM.GridStepMm)
             or nameof(ContourVM.GridStepM)
             or nameof(ContourVM.SnapToGrid)
             or nameof(ContourVM.DrawingPhase)
             or nameof(ContourVM.IsEdit)
             or nameof(ContourVM.Point))
         {
            ApplyViewTransform();
            InvalidateVisual();
         }
      }

      void OnPointsChanged(object? s, NotifyCollectionChangedEventArgs e)
      {
         ApplyViewTransform();
         InvalidateVisual();
      }

      void OnCanvasRefreshRequested() => Dispatcher.Invoke(() =>
      {
         ApplyViewTransform();
         InvalidateVisual();
      });

      protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
      {
         base.OnRenderSizeChanged(sizeInfo);
         ApplyViewTransform();
         InvalidateVisual();
      }

      protected override void OnRender(DrawingContext dc)
      {
         base.OnRender(dc);

         double w = RenderSize.Width;
         double h = RenderSize.Height;
         if (w < 2 || h < 2 || _vm == null) return;

         dc.DrawRectangle(ParseBrush(_settings.Background), null, new Rect(0, 0, w, h));

         DrawAxes(dc, w, h);

         if (_vm.IsEdit && (_vm.IsDrawingSetup || _vm.IsDrawingActive))
            DrawViewBounds(dc);

         DrawContour(dc);

         if (_vm.IsDrawingActive && _hasCursor && _vm.Contour.Points.Count > 0 && !_vm.Contour.IsClosed)
            DrawRubberBand(dc);
      }

      void DrawViewBounds(DrawingContext dc)
      {
         var p0 = ToScreen(_vm!.ViewXMin, _vm.ViewYMin);
         var p1 = ToScreen(_vm.ViewXMax, _vm.ViewYMax);
         var rect = new Rect(
            Math.Min(p0.X, p1.X), Math.Min(p0.Y, p1.Y),
            Math.Abs(p1.X - p0.X), Math.Abs(p1.Y - p0.Y));
         dc.DrawRectangle(null, _bboxPen, rect);
      }

      void DrawAxes(DrawingContext dc, double w, double h)
      {
         if (_vm == null) return;

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

         DrawOriginReferenceAxes(dc, w, h);

         if (!settings.ShowAxesValues) return;

         const double tickLen = 4;
         const double gap = 4;

         foreach (var t in NiceTicks(xMin, xMax, settings.TickCount))
         {
            var sp = ToScreen(t, 0);
            double px = sp.X;
            if (px < 0 || px > w) continue;
            double ty = axisPxY;
            dc.DrawLine(tickPen, new Point(px, ty - tickLen), new Point(px, ty + tickLen));
            var ft = new FormattedText(FormatTick(t), CultureInfo.CurrentCulture,
               FlowDirection.LeftToRight, typeface, fontSize, brush, 96);
            double lx = px - ft.Width / 2;
            double ly = ty + tickLen + gap + ft.Height <= h
               ? ty + tickLen + gap
               : ty - tickLen - gap - ft.Height;
            if (lx < 0) lx = 0;
            if (lx + ft.Width > w) lx = w - ft.Width;
            if (ly < 0) ly = 0;
            if (ly + ft.Height > h) ly = h - ft.Height;
            dc.DrawText(ft, new Point(lx, ly));
         }

         foreach (var t in NiceTicks(yMin, yMax, settings.TickCount))
         {
            var sp = ToScreen(0, t);
            double py = sp.Y;
            if (py < 0 || py > h) continue;
            double tx = axisPxX;
            dc.DrawLine(tickPen, new Point(tx - tickLen, py), new Point(tx + tickLen, py));
            var ft = new FormattedText(FormatTick(t), CultureInfo.CurrentCulture,
               FlowDirection.LeftToRight, typeface, fontSize, brush, 96);
            double lx = tx - ft.Width - tickLen - gap >= 0
               ? tx - ft.Width - tickLen - gap
               : tx + tickLen + gap;
            double ly = py - ft.Height / 2;
            if (lx < 0) lx = 0;
            if (lx + ft.Width > w) lx = w - ft.Width;
            if (ly < 0) ly = 0;
            if (ly + ft.Height > h) ly = h - ft.Height;
            dc.DrawText(ft, new Point(lx, ly));
         }
      }

      void DrawOriginReferenceAxes(DrawingContext dc, double w, double h)
      {
         if (!_settings.ShowOriginReferenceAxes) return;

         double px0 = ToScreen(0, 0).X;
         double py0 = ToScreen(0, 0).Y;
         bool showVertical = px0 >= 0 && px0 <= w;
         bool showHorizontal = py0 >= 0 && py0 <= h;
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
            if (ly < outerPad) ly = Math.Min(h - ft.Height - outerPad, py0 + 2);
            double leftLabelX = outerPad;
            double rightLabelX = w - ft.Width - outerPad;
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
            if (lx + ft.Width > w - outerPad) lx = Math.Max(outerPad, px0 - ft.Width - 4);
            double topLabelY = outerPad;
            double bottomLabelY = h - ft.Height - outerPad;
            double lineStartY = topLabelY + ft.Height + lineGap;
            double lineEndY = bottomLabelY - lineGap;
            if (lineEndY > lineStartY)
               dc.DrawLine(yPen, new Point(px0, lineStartY), new Point(px0, lineEndY));
            DrawLabel(ft, lx, topLabelY);
            DrawLabel(ft, lx, bottomLabelY);
         }
      }

      void DrawContour(DrawingContext dc)
      {
         if (_vm == null) return;
         var pts = _vm.Contour.Points;
         if (pts.Count == 0) return;

         int drawCount = pts.Count;
         if (_vm.Contour.IsClosed && drawCount > 1)
            drawCount--;

         if (drawCount >= 2)
         {
            var curvePen = new Pen(ParseBrush(_settings.Curve), _settings.CurveThickness);
            var geom = new StreamGeometry();
            using var ctx = geom.Open();
            ctx.BeginFigure(ToScreen(pts[0].X, pts[0].Y), false, _vm.Contour.IsClosed);
            for (int i = 1; i < drawCount; i++)
               ctx.LineTo(ToScreen(pts[i].X, pts[i].Y), true, false);
            if (_vm.Contour.IsClosed && pts.Count > 1)
               ctx.LineTo(ToScreen(pts[0].X, pts[0].Y), true, false);
            geom.Freeze();
            dc.DrawGeometry(null, curvePen, geom);
         }

         if (!_vm.IsEdit) return;

         for (int i = 0; i < drawCount; i++)
         {
            var pt = pts[i];
            var sp = ToScreen(pt.X, pt.Y);
            Brush fill = pt == _vm.Point ? _vertexSelFill :
                         i == 0 ? _vertexFirstFill : _vertexFill;
            dc.DrawEllipse(fill, new Pen(Brushes.White, 1), sp, 5, 5);

            if (pt == _vm.Point || i == 0)
            {
               var ft = new FormattedText((i + 1).ToString(CultureInfo.CurrentCulture),
                  CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                  new Typeface("Segoe UI Semibold"), 9, Brushes.White, 1.0);
               dc.DrawText(ft, new Point(sp.X - ft.Width / 2, sp.Y - ft.Height / 2));
            }
         }
      }

      void DrawRubberBand(DrawingContext dc)
      {
         if (_vm == null) return;
         var last = _vm.Contour.Points[^1];
         var a = ToScreen(last.X, last.Y);
         var b = ToScreen(_cursorModel.X, _cursorModel.Y);
         dc.DrawLine(_rubberPen, a, b);
      }

      void ApplyViewTransform()
      {
         if (_vm == null || ActualWidth < 1 || ActualHeight < 1) return;

         double xMin = _vm.ViewXMin, xMax = _vm.ViewXMax;
         double yMin = _vm.ViewYMin, yMax = _vm.ViewYMax;

         double padX = (xMax - xMin) * 0.05 + 1e-4;
         double padY = (yMax - yMin) * 0.05 + 1e-4;
         xMin -= padX; xMax += padX;
         yMin -= padY; yMax += padY;

         double sx = ActualWidth / (xMax - xMin);
         double sy = ActualHeight / (yMax - yMin);
         _scale = Math.Min(sx, sy);

         double modelW = ActualWidth / _scale;
         double modelH = ActualHeight / _scale;
         _originX = xMin - (modelW - (xMax - xMin)) / 2;
         _originY = yMin - (modelH - (yMax - yMin)) / 2;
      }

      Point ToScreen(double mx, double my)
         => new(_scale * (mx - _originX),
                ActualHeight - _scale * (my - _originY));

      (double X, double Y) ToModel(Point sp)
         => (sp.X / _scale + _originX,
             (ActualHeight - sp.Y) / _scale + _originY);

      StressPoint? HitVertex(Point sp)
      {
         if (_vm == null) return null;

         var pts = _vm.Contour.Points;
         int count = pts.Count;
         if (count == 0) return null;

         int unique = _vm.Contour.IsClosed && count > 1 ? count - 1 : count;
         StressPoint? best = null;
         double bestD = HitVertexPx * HitVertexPx;

         for (int i = 0; i < unique; i++)
         {
            var p = pts[i];
            var bp = ToScreen(p.X, p.Y);
            double d = (sp.X - bp.X) * (sp.X - bp.X) + (sp.Y - bp.Y) * (sp.Y - bp.Y);
            if (d <= bestD) { bestD = d; best = p; }
         }
         return best;
      }

      bool IsNearFirstVertex(Point sp)
      {
         if (_vm == null || _vm.Contour.Points.Count < 3 || _vm.Contour.IsClosed) return false;
         var p = _vm.Contour.Points[0];
         var bp = ToScreen(p.X, p.Y);
         double d = (sp.X - bp.X) * (sp.X - bp.X) + (sp.Y - bp.Y) * (sp.Y - bp.Y);
         return d <= CloseFirstPx * CloseFirstPx;
      }

      protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
      {
         base.OnMouseLeftButtonDown(e);
         if (_vm == null || !_vm.IsDrawingActive) return;

         var sp = e.GetPosition(this);
         Focus();

         if (IsNearFirstVertex(sp))
         {
            _vm.TryCloseAtFirstVertex();
            e.Handled = true;
            InvalidateVisual();
            return;
         }

         var hit = HitVertex(sp);
         if (hit != null)
         {
            _dragPoint = hit;
            _vm.Point = hit;
            CaptureMouse();
            e.Handled = true;
            return;
         }

         var (mx, my) = ToModel(sp);
         _vm.AddPoint(mx, my);
         e.Handled = true;
         InvalidateVisual();
      }

      protected override void OnMouseMove(MouseEventArgs e)
      {
         base.OnMouseMove(e);
         if (_vm == null) return;

         var sp = e.GetPosition(this);
         var (mx, my) = ToModel(sp);
         _cursorModel = new Point(_vm.SnapCoord(mx), _vm.SnapCoord(my));
         _hasCursor = true;

         if (_dragPoint != null && e.LeftButton == MouseButtonState.Pressed && _vm.IsDrawingActive)
         {
            _vm.MovePoint(_dragPoint, mx, my);
            InvalidateVisual();
            return;
         }

         if (_vm.IsDrawingSetup || _vm.IsDrawingActive)
            InvalidateVisual();
      }

      protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
      {
         base.OnMouseLeftButtonUp(e);
         if (_dragPoint != null)
         {
            _vm?.CommitPointMove();
            _dragPoint = null;
            if (IsMouseCaptured) ReleaseMouseCapture();
            InvalidateVisual();
         }
      }

      protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
      {
         base.OnMouseRightButtonDown(e);
         if (_vm == null || !_vm.IsDrawingActive) return;
         _vm.RemoveLastPoint();
         e.Handled = true;
         InvalidateVisual();
      }

      protected override void OnKeyDown(KeyEventArgs e)
      {
         base.OnKeyDown(e);
         if (_vm == null || !_vm.IsDrawingActive) return;
         if (e.Key == Key.Delete || e.Key == Key.Back)
         {
            _vm.RemoveLastPoint();
            e.Handled = true;
            InvalidateVisual();
         }
      }

      protected override void OnMouseLeave(MouseEventArgs e)
      {
         base.OnMouseLeave(e);
         _hasCursor = false;
         InvalidateVisual();
      }

      static double[] NiceTicks(double min, double max, int targetCount)
      {
         if (max - min < 1e-12) return [min];
         double range = max - min;
         double roughStep = range / targetCount;
         double exponent = Math.Floor(Math.Log10(roughStep));
         double fraction = roughStep / Math.Pow(10, exponent);
         double niceStep = fraction <= 1.5 ? 1 : fraction <= 3 ? 2 : fraction <= 7 ? 5 : 10;
         niceStep *= Math.Pow(10, exponent);

         double first = Math.Ceiling(min / niceStep) * niceStep;
         var list = new System.Collections.Generic.List<double>();
         for (double v = first; v <= max + niceStep * 0.5; v += niceStep)
            list.Add(v);
         return list.ToArray();
      }

      static string FormatTick(double v)
      {
         var av = Math.Abs(v);
         if (av < 1e-12) return "0";
         if (av < 0.001) return v.ToString("E2");
         if (av < 0.01) return v.ToString("F5");
         if (av < 1) return v.ToString("F4");
         if (av < 100) return v.ToString("F2");
         return v.ToString("F0");
      }

      static Brush ParseBrush(string hex)
      {
         try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
         catch { return Brushes.White; }
      }

      static double Clamp(double v, double lo, double hi)
         => v < lo ? lo : v > hi ? hi : v;
   }
}
