using OpenCS.Utilites;
using OpenCS.ViewModels;

using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace OpenCS.Views
{
    /// <summary>
    /// Интерактивный WPF-холст для редактора групп арматуры.
    /// Рисует через OnRender/DrawingContext; hit-test и drag реализованы вручную.
    /// </summary>
    public class RebarGroupCanvas : FrameworkElement
    {
        RebarGroupEditorVM? _vm;

        // Координатный трансформ: model → screen
        double _scale   = 200;  // px/м
        double _originX = 0;    // модельные X при screen.X = 0
        double _originY = 0;    // модельные Y при screen.Y = 0 (ось Y инвертирована)

        // Drag-состояние
        BarItem?  _dragBar;
        EdgeItem? _dragEdge;
        double    _dragEdgeMidX, _dragEdgeMidY;

        // Fill-состояние
        BarItem? _fillBar1;

        // Hover
        EdgeItem? _hoverEdge;

        static readonly Pen   _refPen       = new(Brushes.LightGray, 1.5);
        static readonly Pen   _coverPen     = new(new SolidColorBrush(Color.FromRgb(59, 130, 246)), 1.5) { DashStyle = DashStyles.Dash };
        static readonly Pen   _barPen       = new(new SolidColorBrush(Color.FromRgb(153, 27, 27)), 1.0);
        static readonly Pen   _noMatPen     = new(new SolidColorBrush(Color.FromRgb(156, 163, 175)), 1.0);
        static readonly Brush _barFill      = new SolidColorBrush(Color.FromRgb(249, 115, 22));
        static readonly Brush _noMatFill    = new SolidColorBrush(Color.FromRgb(209, 213, 219));
        static readonly Brush _selFill      = new SolidColorBrush(Color.FromRgb(37, 99, 235));
        static readonly Brush _fill1Fill    = new SolidColorBrush(Color.FromRgb(14, 165, 233));
        static readonly Brush _handleNormal = new SolidColorBrush(Color.FromRgb(100, 149, 237));
        static readonly Brush _handleHover  = Brushes.Orange;

        static RebarGroupCanvas()
        {
            // Заморозить кисти для производительности
            ((SolidColorBrush)_barFill).Freeze();
            ((SolidColorBrush)_noMatFill).Freeze();
            ((SolidColorBrush)_selFill).Freeze();
            ((SolidColorBrush)_fill1Fill).Freeze();
            ((SolidColorBrush)_handleNormal).Freeze();
        }

        public RebarGroupCanvas()
        {
            Focusable = true;
            ClipToBounds = true;
        }

        public void SetVM(RebarGroupEditorVM vm)
        {
            _vm = vm;
            vm.PropertyChanged += OnVmPropertyChanged;
            vm.Bars.CollectionChanged  += OnCollectionChanged;
            vm.Edges.CollectionChanged += OnCollectionChanged;
            FitToView();
        }

        void OnVmPropertyChanged(object? s, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(RebarGroupEditorVM.CoverLinePoints)
                               or nameof(RebarGroupEditorVM.ReferencePoints)
                               or nameof(RebarGroupEditorVM.FillMode)
                               or nameof(RebarGroupEditorVM.SelectedMaterial))
                Dispatcher.Invoke(InvalidateVisual);
        }

        void OnCollectionChanged(object? s, NotifyCollectionChangedEventArgs e)
            => Dispatcher.Invoke(InvalidateVisual);

        // ── Рендеринг ────────────────────────────────────────────────────────

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, w, h));
            if (_vm == null) return;

            // Координатная сетка
            DrawGrid(dc, w, h);

            // Координатные оси с подписями
            DrawAxes(dc, w, h);

            // Опорный контур (серый)
            DrawPolyline(dc, _refPen, _vm.ReferencePoints, closed: true);

            // Линия защитного слоя (синяя пунктир)
            DrawPolyline(dc, _coverPen, _vm.CoverLinePoints, closed: true);

            // Ручки рёбер (алмазы на серединах рёбер)
            foreach (var edge in _vm.Edges)
            {
                var (hx, hy) = edge.HandlePoint;
                var sp = ToScreen(hx, hy);
                DrawDiamond(dc, sp, edge == _hoverEdge ? _handleHover : _handleNormal);
            }

            // Стержни
            bool hasMat = _vm.SelectedMaterial != null;
            foreach (var bar in _vm.Bars)
            {
                var sp = ToScreen(bar.X, bar.Y);
                double r = Math.Max(4, bar.Diameter / 2 * _scale);
                Brush fill = bar.IsSelected ? _selFill :
                             bar == _fillBar1 ? _fill1Fill :
                             hasMat ? _barFill : _noMatFill;
                Pen pen = (bar.IsSelected || bar == _fillBar1 || hasMat) ? _barPen : _noMatPen;
                dc.DrawEllipse(fill, pen, sp, r, r);
            }
        }

        void DrawPolyline(DrawingContext dc, Pen pen,
            System.Collections.Generic.IReadOnlyList<(double X, double Y)> pts, bool closed)
        {
            if (pts.Count < 2) return;
            var geom = new StreamGeometry();
            using var ctx = geom.Open();
            ctx.BeginFigure(ToScreen(pts[0].X, pts[0].Y), false, closed);
            for (int i = 1; i < pts.Count; i++)
                ctx.LineTo(ToScreen(pts[i].X, pts[i].Y), true, false);
            geom.Freeze();
            dc.DrawGeometry(null, pen, geom);
        }

        void DrawGrid(DrawingContext dc, double w, double h)
        {
            var settings = _vm?.App?.PlotSettings;
            if (settings == null || !settings.ShowGrid) return;

            double xMin = _originX;
            double xMax = _originX + w / _scale;
            double yMin = _originY;
            double yMax = _originY + h / _scale;

            var ticksX = NiceTicks(xMin, xMax, 6);
            var ticksY = NiceTicks(yMin, yMax, 6);

            Brush brush;
            try { brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(settings.Grid)); }
            catch { brush = new SolidColorBrush(Color.FromRgb(211, 211, 211)); }
            brush.Freeze();

            var pen = new Pen(brush, settings.GridThickness);
            pen.DashStyle = DashStyles.Dot;
            pen.Freeze();

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

        void DrawAxes(DrawingContext dc, double w, double h)
        {
            var settings = _vm?.App?.PlotSettings;
            if (settings == null) return;

            double xMin = _originX;
            double xMax = _originX + w / _scale;
            double yMin = _originY;
            double yMax = _originY + h / _scale;

            Brush brush;
            try { brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(settings.AxesColor)); }
            catch { brush = new SolidColorBrush(Color.FromRgb(100, 100, 100)); }
            brush.Freeze();

            var axisPen = new Pen(brush, 1);
            axisPen.Freeze();

            // Положение осей
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

            // Ось X
            dc.DrawLine(axisPen, new Point(0, axisPxY), new Point(w, axisPxY));
            // Ось Y
            dc.DrawLine(axisPen, new Point(axisPxX, 0), new Point(axisPxX, h));

            DrawOriginReferenceAxes(dc, w, h);

            if (!settings.ShowAxesValues) return;

            var ticksX = NiceTicks(xMin, xMax, settings.TickCount);
            var ticksY = NiceTicks(yMin, yMax, settings.TickCount);
            double fontSize = settings.AxesFontSize;

            var typeface = new Typeface("Segoe UI");
            var tickPen = new Pen(brush, 0.8);
            tickPen.Freeze();

            const double tickLen = 4;

            const double gap = 4;

            // X-тики (вдоль оси)
            foreach (var t in ticksX)
            {
                var sp = ToScreen(t, 0);
                double px = sp.X;
                if (px < 0 || px > w) continue;
                double ty = axisPxY;
                dc.DrawLine(tickPen, new Point(px, ty - tickLen), new Point(px, ty + tickLen));
                var label = FormatTick(t);
                var ft = new FormattedText(label,
                    System.Globalization.CultureInfo.CurrentCulture,
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

            // Y-тики (вдоль оси)
            foreach (var t in ticksY)
            {
                var sp = ToScreen(0, t);
                double py = sp.Y;
                if (py < 0 || py > h) continue;
                double tx = axisPxX;
                dc.DrawLine(tickPen, new Point(tx - tickLen, py), new Point(tx + tickLen, py));
                var label = FormatTick(t);
                var ft = new FormattedText(label,
                    System.Globalization.CultureInfo.CurrentCulture,
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
        }

        static string FormatTick(double v)
        {
            var av = Math.Abs(v);
            if (av < 1e-12) return "0";
            if (av < 0.001) return v.ToString("E2");
            if (av < 0.01)  return v.ToString("F5");
            if (av < 1)     return v.ToString("F4");
            if (av < 100)   return v.ToString("F2");
            if (av < 10000) return v.ToString("F0");
            return v.ToString("E2");
        }

        void DrawOriginReferenceAxes(DrawingContext dc, double w, double h)
        {
            var settings = _vm?.App?.PlotSettings;
            if (settings == null || !settings.ShowOriginReferenceAxes) return;

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
            double fontSize = Math.Max(11, settings.AxesFontSize);
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
                var ft = new FormattedText(Loc.S("AxisLabelX"),
                    System.Globalization.CultureInfo.CurrentCulture,
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
                var ft = new FormattedText(Loc.S("AxisLabelY"),
                    System.Globalization.CultureInfo.CurrentCulture,
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

        static double Clamp(double v, double lo, double hi)
            => v < lo ? lo : v > hi ? hi : v;

        static double[] NiceTicks(double min, double max, int target)
        {
            if (max - min < 1e-12) return [];
            double rough = (max - min) / target;
            double mag = Math.Pow(10, Math.Floor(Math.Log10(rough)));
            double res = rough / mag;
            double nice = res <= 1.5 ? 1 : res <= 3.5 ? 2 : res <= 7.5 ? 5 : 10;
            nice *= mag;
            double start = Math.Ceiling(min / nice) * nice;
            int n = (int)((max - start) / nice) + 1;
            if (n > 50) return [];
            var ticks = new double[n];
            for (int i = 0; i < n; i++) ticks[i] = start + i * nice;
            return ticks;
        }

        static void DrawDiamond(DrawingContext dc, Point center, Brush fill)
        {
            const double s = 5;
            var geom = new StreamGeometry();
            using var ctx = geom.Open();
            ctx.BeginFigure(new Point(center.X,     center.Y - s), true, true);
            ctx.LineTo(     new Point(center.X + s, center.Y    ), true, false);
            ctx.LineTo(     new Point(center.X,     center.Y + s), true, false);
            ctx.LineTo(     new Point(center.X - s, center.Y    ), true, false);
            geom.Freeze();
            dc.DrawGeometry(fill, new Pen(Brushes.Gray, 0.8), geom);
        }

        // ── Координатные трансформы ───────────────────────────────────────────

        Point ToScreen(double mx, double my)
            => new(_scale * (mx - _originX),
                   ActualHeight - _scale * (my - _originY));

        (double X, double Y) ToModel(Point sp)
            => (sp.X / _scale + _originX,
                (ActualHeight - sp.Y) / _scale + _originY);

        public void FitToView()
        {
            if (_vm == null || ActualWidth < 1 || ActualHeight < 1) return;

            double xMin = double.MaxValue, xMax = double.MinValue;
            double yMin = double.MaxValue, yMax = double.MinValue;

            void Expand(double x, double y)
            {
                if (x < xMin) xMin = x; if (x > xMax) xMax = x;
                if (y < yMin) yMin = y; if (y > yMax) yMax = y;
            }

            foreach (var p in _vm.ReferencePoints) Expand(p.X, p.Y);
            foreach (var p in _vm.CoverLinePoints)  Expand(p.X, p.Y);
            foreach (var b in _vm.Bars)             Expand(b.X, b.Y);

            if (xMin > xMax) { xMin = -0.5; xMax = 0.5; yMin = -0.5; yMax = 0.5; }

            double padX = (xMax - xMin) * 0.15 + 0.01;
            double padY = (yMax - yMin) * 0.15 + 0.01;
            xMin -= padX; xMax += padX;
            yMin -= padY; yMax += padY;

            double sx = ActualWidth  / (xMax - xMin);
            double sy = ActualHeight / (yMax - yMin);
            _scale = Math.Min(sx, sy);

            double modelW = ActualWidth  / _scale;
            double modelH = ActualHeight / _scale;
            _originX = xMin - (modelW - (xMax - xMin)) / 2;
            _originY = yMin - (modelH - (yMax - yMin)) / 2;

            InvalidateVisual();
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo info)
        {
            base.OnRenderSizeChanged(info);
            FitToView();
        }

        /// <summary>Зум колесом мыши с фиксацией модельной точки под курсором (как в FiberCanvas).</summary>
        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            var pos = e.GetPosition(this);
            var (mx, my) = ToModel(pos);
            double factor = e.Delta > 0 ? 1.2 : 1.0 / 1.2;
            _scale *= factor;
            _originX = mx - pos.X / _scale;
            _originY = my - (ActualHeight - pos.Y) / _scale;
            InvalidateVisual();
            e.Handled = true;
        }

        // ── Hit-test ─────────────────────────────────────────────────────────

        const double HitBarPx    = 8;
        const double HitHandlePx = 8;
        const double SnapPx      = 10;

        BarItem? HitBar(Point sp)
        {
            if (_vm == null) return null;
            BarItem? best = null;
            double bestD = double.MaxValue;
            foreach (var bar in _vm.Bars)
            {
                var bp = ToScreen(bar.X, bar.Y);
                double d = Math.Sqrt((sp.X - bp.X) * (sp.X - bp.X) + (sp.Y - bp.Y) * (sp.Y - bp.Y));
                double r = Math.Max(HitBarPx, bar.Diameter / 2 * _scale);
                if (d <= r && d < bestD) { best = bar; bestD = d; }
            }
            return best;
        }

        EdgeItem? HitHandle(Point sp)
        {
            if (_vm == null) return null;
            foreach (var edge in _vm.Edges)
            {
                var (hx, hy) = edge.HandlePoint;
                var hp = ToScreen(hx, hy);
                double d = Math.Sqrt((sp.X - hp.X) * (sp.X - hp.X) + (sp.Y - hp.Y) * (sp.Y - hp.Y));
                if (d <= HitHandlePx) return edge;
            }
            return null;
        }

        (double X, double Y) TrySnap(double mx, double my)
        {
            if (_vm == null) return (mx, my);
            double threshold = SnapPx / _scale;
            foreach (var cv in _vm.CoverLinePoints)
            {
                double dx = cv.X - mx, dy = cv.Y - my;
                if (Math.Sqrt(dx * dx + dy * dy) < threshold)
                    return (cv.X, cv.Y);
            }
            return (mx, my);
        }

        // ── MouseDown ────────────────────────────────────────────────────────

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            if (_vm == null) return;
            var sp = e.GetPosition(this);

            // Fill mode: выбор первого/второго стержня
            if (_vm.FillMode)
            {
                var hit = HitBar(sp);
                if (hit != null)
                {
                    if (_fillBar1 == null)
                    {
                        _fillBar1 = hit;
                        InvalidateVisual();
                    }
                    else if (hit != _fillBar1)
                    {
                        _vm.FillBetweenCommand.Execute((_fillBar1, hit));
                        _fillBar1 = null;
                        InvalidateVisual();
                    }
                }
                else
                {
                    _fillBar1 = null;
                    InvalidateVisual();
                }
                e.Handled = true;
                return;
            }

            // Проверить ручку ребра
            var hitHandle = HitHandle(sp);
            if (hitHandle != null)
            {
                _dragEdge = hitHandle;
                _dragEdgeMidX = (hitHandle.StartX + hitHandle.EndX) / 2;
                _dragEdgeMidY = (hitHandle.StartY + hitHandle.EndY) / 2;
                CaptureMouse();
                e.Handled = true;
                return;
            }

            // Проверить стержень
            var hitBar = HitBar(sp);
            if (hitBar != null)
            {
                _dragBar = hitBar;
                _vm.SelectBarCommand.Execute(hitBar);
                CaptureMouse();
                e.Handled = true;
                return;
            }

            // Клик в пустое место — добавить стержень
            var (mx, my) = ToModel(sp);
            (mx, my) = TrySnap(mx, my);
            _vm.AddBarCommand.Execute((mx, my));
            e.Handled = true;
        }

        // ── MouseMove ────────────────────────────────────────────────────────

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_vm == null) return;
            var sp = e.GetPosition(this);

            if (_dragBar != null && e.LeftButton == MouseButtonState.Pressed)
            {
                var (mx, my) = ToModel(sp);
                (mx, my) = TrySnap(mx, my);
                _vm.MoveBarCommand.Execute((_dragBar, mx, my));
                InvalidateVisual();
                return;
            }

            if (_dragEdge != null && e.LeftButton == MouseButtonState.Pressed)
            {
                var (mx, my) = ToModel(sp);
                double proj = (mx - _dragEdgeMidX) * _dragEdge.NormalX
                            + (my - _dragEdgeMidY) * _dragEdge.NormalY;
                _vm.MoveEdgeHandleCommand.Execute((_dragEdge, proj));
                InvalidateVisual();
                return;
            }

            // Hover над ручкой
            var hov = HitHandle(sp);
            if (hov != _hoverEdge)
            {
                _hoverEdge = hov;
                InvalidateVisual();
            }
        }

        // ── MouseUp ──────────────────────────────────────────────────────────

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            if (IsMouseCaptured) ReleaseMouseCapture();
            _dragBar  = null;
            _dragEdge = null;
        }
    }
}
