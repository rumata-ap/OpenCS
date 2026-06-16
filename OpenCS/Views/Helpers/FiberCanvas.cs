using OpenCS.ViewModels;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace OpenCS.Views.Helpers
{
    /// <summary>
    /// Кастомный FrameworkElement для отрисовки сечения с цветовой картой σ/ε.
    /// Рисует всё через DrawingContext.OnRender — без WPF-элементов на фибру.
    /// Поддерживает зум колёсиком, панорамирование ЛКМ, тултип при наведении,
    /// кнопку FitAll.
    /// </summary>
    public class FiberCanvas : FrameworkElement
    {
        // ── Transform state ───────────────────────────────────────────
        double _scale = 1.0;      // пикселей на мм
        double _tx = 0, _ty = 0; // смещение в пикселях
        Point  _dragStart;
        bool   _dragging;

        // ── Tooltip ───────────────────────────────────────────────────
        readonly ToolTip _tip = new();
        string? _lastTip;

        // ── DP: ViewModel ─────────────────────────────────────────────
        public static readonly DependencyProperty ViewModelProperty =
            DependencyProperty.Register(nameof(ViewModel), typeof(SectionPlotVM),
                typeof(FiberCanvas),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender,
                    OnViewModelChanged));

        public SectionPlotVM? ViewModel
        {
            get => (SectionPlotVM?)GetValue(ViewModelProperty);
            set => SetValue(ViewModelProperty, value);
        }

        static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var fc = (FiberCanvas)d;
            if (e.OldValue is SectionPlotVM old)
            {
                old.PropertyChanged   -= fc.OnVmPropertyChanged;
                old.FitAllRequested   -= fc.FitToView;
            }
            if (e.NewValue is SectionPlotVM vm)
            {
                vm.PropertyChanged   += fc.OnVmPropertyChanged;
                vm.FitAllRequested   += fc.FitToView;
                fc._fitted = false;  // сбросить флаг первой подгонки
            }
        }

        void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
            => InvalidateVisual();

        bool _fitted;

        static readonly Pen _transparentPen = new(Brushes.Transparent, 0);
        static readonly Pen _outlinePen     = new(Brushes.Black, 0.5);
        static readonly Pen _markerPen      = new(Brushes.DarkBlue, 2);
        static readonly Pen _hullPen        = new(Brushes.Black, 1.0);
        static readonly Pen _holePen        = new(Brushes.DarkGray, 0.7);
        static readonly Brush _comprBrush   = CreateHatchBrush(Color.FromArgb(120, 50, 120, 220));   // синяя штриховка — сжатие
        static readonly Brush _tensBrush    = CreateHatchBrush(Color.FromArgb(120, 220, 80, 50));    // красная штриховка — растяжение

        public FiberCanvas()
        {
            ToolTipService.SetToolTip(this, _tip);
            ToolTipService.SetInitialShowDelay(this, 300);
            ToolTipService.SetIsEnabled(this, false);
            ClipToBounds = true;
        }

        // ── Layout ────────────────────────────────────────────────────
        protected override Size MeasureOverride(Size availableSize)
            => new(
                double.IsInfinity(availableSize.Width)  ? 200 : availableSize.Width,
                double.IsInfinity(availableSize.Height) ? 200 : availableSize.Height);

        protected override Size ArrangeOverride(Size finalSize)
        {
            if (!_fitted && ViewModel != null)
            {
                FitToView();
                _fitted = true;
            }
            return finalSize;
        }

        // ── FitToView ─────────────────────────────────────────────────
        public void FitToView()
        {
            var vm = ViewModel;
            if (vm == null || ActualWidth < 1 || ActualHeight < 1) return;

            double xMin = double.MaxValue, xMax = double.MinValue;
            double yMin = double.MaxValue, yMax = double.MinValue;

            void Expand(Point p)
            {
                if (p.X < xMin) xMin = p.X; if (p.X > xMax) xMax = p.X;
                if (p.Y < yMin) yMin = p.Y; if (p.Y > yMax) yMax = p.Y;
            }

            foreach (var f in vm.ConcreteFibers)
                foreach (var p in f.Vertices) Expand(p);
            foreach (var a in vm.NoMeshAreas)
                foreach (var p in a.Hull) Expand(p);
            foreach (var r in vm.RebarFibers)
            {
                Expand(new Point(r.Center.X - r.RadiusMm, r.Center.Y - r.RadiusMm));
                Expand(new Point(r.Center.X + r.RadiusMm, r.Center.Y + r.RadiusMm));
            }

            if (xMin > xMax) { _scale = 1; _tx = _ty = 0; InvalidateVisual(); return; }

            double pad = 20;
            double sw = ActualWidth  - 2 * pad;
            double sh = ActualHeight - 2 * pad;
            double mw = xMax - xMin, mh = yMax - yMin;
            if (mw < 1e-6) mw = 1; if (mh < 1e-6) mh = 1;

            _scale = Math.Min(sw / mw, sh / mh);
            _tx = pad + (sw - mw * _scale) / 2 - xMin * _scale;
            // Y инвертирован: screen_y = -model_y * scale + ty → ty = pad + yMax * scale
            _ty = pad + (sh - mh * _scale) / 2 + yMax * _scale;

            InvalidateVisual();
        }

        // ── Model ↔ Screen ────────────────────────────────────────────
        // Y инвертируется: модельная ось Y вверх, экранная — вниз
        Point ToScreen(Point model) =>
            new(model.X * _scale + _tx, -model.Y * _scale + _ty);

        Point ToModel(Point screen) =>
            new((screen.X - _tx) / _scale, -(screen.Y - _ty) / _scale);

        // ── OnRender ──────────────────────────────────────────────────
        protected override void OnRender(DrawingContext dc)
        {
            dc.DrawRectangle(SystemColors.WindowBrush, null,
                new Rect(0, 0, ActualWidth, ActualHeight));

            var vm = ViewModel;
            if (vm == null) return;

            // Основной материал (не арматура)
            if (vm.ShowConcrete)
            {
                foreach (var f in vm.ConcreteFibers)
                {
                    var brush = new SolidColorBrush(
                        ColormapHelper.GetDiscreteColor(f.Value, vm.ConcreteMin, vm.ConcreteMax, f.IsRebar));
                    dc.DrawGeometry(brush, _transparentPen, BuildPath(f.Vertices));
                }
                foreach (var a in vm.NoMeshAreas)
                    DrawNoMesh(dc, a, vm.ShowValues);
            }

            // Арматура
            if (vm.ShowRebar)
            {
                foreach (var r in vm.RebarFibers)
                {
                    var center = ToScreen(r.Center);
                    double radius = r.RadiusMm * _scale;
                    var brush = new SolidColorBrush(
                        ColormapHelper.GetDiscreteColor(r.Value, vm.RebarMin, vm.RebarMax, true));
                    dc.DrawEllipse(brush, _outlinePen, center, radius, radius);
                }
            }

            // Подписи значений
            if (vm.ShowValues)
            {
                var tf = new Typeface("Consolas");
                foreach (var f in vm.ConcreteFibers)
                {
                    if (!vm.ShowConcrete) continue;
                    var txt = new FormattedText($"{f.Value:G4}", CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight, tf, 9, Brushes.Black, 1.0);
                    var sc = ToScreen(f.Centroid);
                    dc.DrawText(txt, new Point(sc.X - txt.Width / 2, sc.Y - txt.Height / 2));
                }
                foreach (var r in vm.RebarFibers)
                {
                    if (!vm.ShowRebar) continue;
                    var txt = new FormattedText($"{r.Value:G4}", CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight, tf, 9, Brushes.Black, 1.0);
                    var sc = ToScreen(r.Center);
                    dc.DrawText(txt, new Point(sc.X - txt.Width / 2, sc.Y - txt.Height / 2));
                }
            }

            // Маркер максимального сжатия
            if (vm.ShowMaxCompr)
            {
                Point? minCentroid = null;
                double minVal = double.MaxValue;
                foreach (var f in vm.ConcreteFibers)
                    if (f.Value < minVal) { minVal = f.Value; minCentroid = f.Centroid; }
                foreach (var r in vm.RebarFibers)
                    if (r.Value < minVal) { minVal = r.Value; minCentroid = r.Center; }

                if (minCentroid.HasValue)
                {
                    var sc = ToScreen(minCentroid.Value);
                    double ms = 6;
                    dc.DrawLine(_markerPen, new Point(sc.X - ms, sc.Y), new Point(sc.X + ms, sc.Y));
                    dc.DrawLine(_markerPen, new Point(sc.X, sc.Y - ms), new Point(sc.X, sc.Y + ms));
                }
            }
        }

        Geometry BuildPath(IReadOnlyList<Point> vertices)
        {
            var geom = new StreamGeometry();
            using var ctx = geom.Open();
            ctx.BeginFigure(ToScreen(vertices[0]), true, true);
            for (int i = 1; i < vertices.Count; i++)
                ctx.LineTo(ToScreen(vertices[i]), true, false);
            geom.Freeze();
            return geom;
        }

        void DrawNoMesh(DrawingContext dc, NoMeshAreaDrawData a, bool showValues)
        {
            // Зона сжатия
            if (a.CompressionZone != null && a.CompressionZone.Count >= 3)
                dc.DrawGeometry(_comprBrush, null, BuildPath(a.CompressionZone));

            // Зона растяжения
            if (a.TensionZone != null && a.TensionZone.Count >= 3)
                dc.DrawGeometry(_tensBrush, null, BuildPath(a.TensionZone));

            // Контур hull
            dc.DrawGeometry(null, _hullPen, BuildPath(a.Hull));

            // Контуры отверстий
            foreach (var hole in a.Holes)
                dc.DrawGeometry(null, _holePen, BuildPath(hole));

            // Значения в вершинах hull
            if (showValues)
            {
                var tf = new Typeface("Consolas");
                foreach (var (pt, val) in a.HullValues)
                {
                    var sc = ToScreen(pt);
                    var txt = new FormattedText($"{val:G4}", CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight, tf, 8, Brushes.DarkBlue, 1.0);
                    dc.DrawText(txt, new Point(sc.X + 2, sc.Y - txt.Height / 2));
                }
            }
        }

        Geometry BuildHolesGeometry(IReadOnlyList<IReadOnlyList<Point>> holes)
        {
            if (holes.Count == 0) return Geometry.Empty;
            var group = new GeometryGroup();
            foreach (var hole in holes)
                group.Children.Add(BuildPath(hole));
            return group;
        }

        static Brush CreateHatchBrush(Color color)
        {
            var pen = new Pen(new SolidColorBrush(color), 1.0);
            pen.Freeze();
            var dg = new DrawingGroup();
            using (var ctx = dg.Open())
            {
                ctx.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, 8, 8));
                ctx.DrawLine(pen, new Point(0, 8), new Point(8, 0));
                ctx.DrawLine(pen, new Point(-4, 8), new Point(4, 0));
                ctx.DrawLine(pen, new Point(4, 8), new Point(12, 0));
            }
            var brush = new DrawingBrush
            {
                Drawing      = dg,
                TileMode     = TileMode.Tile,
                Viewport     = new Rect(0, 0, 8, 8),
                ViewportUnits = BrushMappingMode.Absolute
            };
            brush.Freeze();
            return brush;
        }

        // ── Mouse ─────────────────────────────────────────────────────
        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            var pos = e.GetPosition(this);
            double factor = e.Delta > 0 ? 1.2 : 1.0 / 1.2;
            _scale *= factor;
            _tx = pos.X + (_tx - pos.X) * factor;
            _ty = pos.Y + (_ty - pos.Y) * factor;
            InvalidateVisual();
            e.Handled = true;
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            _dragging  = true;
            _dragStart = e.GetPosition(this);
            CaptureMouse();
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            _dragging = false;
            ReleaseMouseCapture();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_dragging)
            {
                var pos = e.GetPosition(this);
                _tx += pos.X - _dragStart.X;
                _ty += pos.Y - _dragStart.Y;
                _dragStart = pos;
                InvalidateVisual();
                return;
            }

            // HitTest для тултипа
            var vm = ViewModel;
            if (vm == null) return;
            var modelPos = ToModel(e.GetPosition(this));
            string? found = FindTooltip(vm, modelPos);
            if (found != _lastTip)
            {
                _lastTip = found;
                _tip.Content = found ?? string.Empty;
                ToolTipService.SetIsEnabled(this, found != null);
            }
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            ToolTipService.SetIsEnabled(this, false);
            _lastTip = null;
        }

        string? FindTooltip(SectionPlotVM vm, Point modelPos)
        {
            double threshold = 5.0 / _scale; // 5 пикселей → модельные мм

            if (vm.ShowRebar)
                foreach (var r in vm.RebarFibers)
                {
                    double dx = modelPos.X - r.Center.X;
                    double dy = modelPos.Y - r.Center.Y;
                    if (Math.Sqrt(dx*dx + dy*dy) <= r.RadiusMm + 1)
                        return r.Tooltip;
                }

            if (vm.ShowConcrete)
            {
                FiberDrawData? nearest = null;
                double nearestDist = threshold;
                foreach (var f in vm.ConcreteFibers)
                {
                    double dx = modelPos.X - f.Centroid.X;
                    double dy = modelPos.Y - f.Centroid.Y;
                    double d = Math.Sqrt(dx*dx + dy*dy);
                    if (d < nearestDist) { nearestDist = d; nearest = f; }
                }
                if (nearest != null) return nearest.Tooltip;
            }

            return null;
        }
    }
}
