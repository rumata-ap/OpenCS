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
        bool      _hasDragged;

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
            _hasDragged = false;

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
                _hasDragged = true;
                var (mx, my) = ToModel(sp);
                (mx, my) = TrySnap(mx, my);
                _vm.MoveBarCommand.Execute((_dragBar, mx, my));
                InvalidateVisual();
                return;
            }

            if (_dragEdge != null && e.LeftButton == MouseButtonState.Pressed)
            {
                _hasDragged = true;
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
