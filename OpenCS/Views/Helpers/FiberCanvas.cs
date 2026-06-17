using OpenCS.ViewModels;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
        Point  _dragStart;        // экранная точка начала жеста
        Point  _rubberEnd;        // текущий конец рамки (экранные пиксели)
        bool   _didDrag;          // мышь сдвинулась > порога
        bool   _modAtDown;        // Shift/Ctrl был зажат при MouseDown

        // ── Hover tooltip ─────────────────────────────────────────────
        readonly ToolTip _tip = new();
        string? _lastTip;

        // ── Multi-selection & popup ───────────────────────────────────
        readonly HashSet<FiberDrawData> _selectedFibers =
            new(ReferenceEqualityComparer.Instance);
        readonly HashSet<RebarDrawData> _selectedRebars =
            new(ReferenceEqualityComparer.Instance);
        readonly Popup     _selPopup;
        readonly TextBlock _selPopupText;

        static readonly Pen _selectPen1 = MakeFreezePen(Brushes.White,          3.0);
        static readonly Pen _selectPen2 = MakeFreezePen(
            new SolidColorBrush(Color.FromRgb(0xFF, 0xB3, 0x00)), 1.5);  // amber

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
                old.PropertyChanged -= fc.OnVmPropertyChanged;
                old.FitAllRequested -= fc.FitToView;
            }
            if (e.NewValue is SectionPlotVM vm)
            {
                vm.PropertyChanged += fc.OnVmPropertyChanged;
                vm.FitAllRequested += fc.FitToView;
                fc._fitted = false;
            }
            fc._selectedFibers.Clear();
            fc._selectedRebars.Clear();
            fc._selPopup.IsOpen = false;
        }

        void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
            => InvalidateVisual();

        bool _fitted;

        static readonly Pen _transparentPen = new(Brushes.Transparent, 0);
        static readonly Pen _outlinePen     = new(Brushes.Black, 0.5);
        static readonly Pen _markerPen      = new(Brushes.DarkBlue, 2);
        static readonly Pen _gridPen        = MakeFreezePen(
            new SolidColorBrush(Color.FromArgb(70, 20, 20, 20)), 0.4);

        public FiberCanvas()
        {
            ToolTipService.SetToolTip(this, _tip);
            ToolTipService.SetInitialShowDelay(this, 300);
            ToolTipService.SetIsEnabled(this, false);
            ClipToBounds = true;

            _selPopupText = new TextBlock
            {
                FontFamily = new FontFamily("Consolas"),
                FontSize    = 11,
                Foreground  = Brushes.Black,
                Padding     = new Thickness(0),
            };
            _selPopup = new Popup
            {
                Child = new Border
                {
                    Background      = new SolidColorBrush(Color.FromRgb(0xFF, 0xFD, 0xE7)),
                    BorderBrush     = new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B)),
                    BorderThickness = new Thickness(1),
                    CornerRadius    = new CornerRadius(4),
                    Padding         = new Thickness(8, 5, 8, 5),
                    Child           = _selPopupText,
                    Effect          = new System.Windows.Media.Effects.DropShadowEffect
                    {
                        ShadowDepth = 2, BlurRadius = 6,
                        Color = Colors.Black, Opacity = 0.25
                    }
                },
                PlacementTarget  = this,
                Placement        = PlacementMode.Mouse,
                AllowsTransparency = true,
                StaysOpen        = false,
            };
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
                _fitted = true;
                // BeginInvoke: ActualWidth/ActualHeight установятся после завершения текущего прохода Arrange
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, FitToView);
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

            // Динамические перья из настроек VM
            var hullPen    = MakePen(vm.HullColorHex,       vm.HullThickness);
            var holePen    = MakePen(vm.HoleColorHex,       vm.HoleThickness);
            var neutralPen = MakeDashedPen(ParseBrush(vm.NeutralAxisColorHex), vm.NeutralAxisThickness);

            // Клип по контурам сечения (hull − holes) — переиспользуется для сетки и нейтральной линии
            Geometry? sectionClip = null;
            if (vm.NoMeshAreas.Count > 0)
            {
                var clipGeom = new StreamGeometry { FillRule = FillRule.EvenOdd };
                using (var clipCtx = clipGeom.Open())
                {
                    foreach (var a in vm.NoMeshAreas)
                    {
                        if (a.Hull.Count >= 3)
                        {
                            clipCtx.BeginFigure(ToScreen(a.Hull[0]), isFilled: true, isClosed: true);
                            for (int i = 1; i < a.Hull.Count; i++)
                                clipCtx.LineTo(ToScreen(a.Hull[i]), isStroked: false, isSmoothJoin: false);
                        }
                        foreach (var hole in a.Holes)
                        {
                            if (hole.Count < 3) continue;
                            clipCtx.BeginFigure(ToScreen(hole[0]), isFilled: true, isClosed: true);
                            for (int i = 1; i < hole.Count; i++)
                                clipCtx.LineTo(ToScreen(hole[i]), isStroked: false, isSmoothJoin: false);
                        }
                    }
                }
                clipGeom.Freeze();
                sectionClip = clipGeom;
            }

            // Основной материал (фибры и псевдофибры)
            bool isStressMode  = vm.Mode == SectionPlotMode.Stress;
            bool smoothColormap = vm.SmoothColormap;
            if (vm.ShowConcrete)
            {
                foreach (var f in vm.ConcreteFibers)
                {
                    Brush brush;
                    if (isStressMode && Math.Abs(f.Value) < 1e-9)
                    {
                        brush = Brushes.White;
                    }
                    else if (smoothColormap &&
                             Math.Abs(f.GradientMax.val - f.GradientMin.val) > 1e-10)
                    {
                        var c0 = ColormapHelper.GetColor(f.GradientMin.val,
                            vm.ConcreteMin, vm.ConcreteMax, f.IsRebar);
                        var c1 = ColormapHelper.GetColor(f.GradientMax.val,
                            vm.ConcreteMin, vm.ConcreteMax, f.IsRebar);
                        var lgb = new LinearGradientBrush(c0, c1,
                            ToScreen(f.GradientMin.pt), ToScreen(f.GradientMax.pt));
                        lgb.MappingMode = BrushMappingMode.Absolute;
                        brush = lgb;
                    }
                    else
                    {
                        var color = smoothColormap
                            ? ColormapHelper.GetColor(f.Value, vm.ConcreteMin, vm.ConcreteMax, f.IsRebar)
                            : ColormapHelper.GetDiscreteColor(f.Value, vm.ConcreteMin, vm.ConcreteMax, f.IsRebar);
                        brush = new SolidColorBrush(color);
                    }
                    dc.DrawGeometry(brush, _transparentPen, BuildPathWithHoles(f.Vertices, f.Holes));
                }
            }

            // Сетка фибр — тонкие полупрозрачные контуры ячеек поверх заливки
            if (vm.ShowFiberGrid && vm.ShowConcrete && sectionClip != null)
            {
                dc.PushClip(sectionClip);
                foreach (var f in vm.ConcreteFibers)
                    dc.DrawGeometry(null, _gridPen, BuildPath(f.Vertices));
                dc.Pop();
            }

            // Контуры областей — ВСЕГДА и поверх фибр
            foreach (var a in vm.NoMeshAreas)
                DrawNoMesh(dc, a, vm.ShowValues, hullPen, holePen, vm.FiberLabelFontSize);

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

            // Подсветка выбранных элементов
            foreach (var f in _selectedFibers)
            {
                var geom = BuildPath(f.Vertices);
                dc.DrawGeometry(null, _selectPen1, geom);
                dc.DrawGeometry(null, _selectPen2, geom);
            }
            foreach (var r in _selectedRebars)
            {
                var sc  = ToScreen(r.Center);
                double radius = r.RadiusMm * _scale;
                dc.DrawEllipse(null, _selectPen1, sc, radius + 1.5, radius + 1.5);
                dc.DrawEllipse(null, _selectPen2, sc, radius + 1.5, radius + 1.5);
            }

            // Подписи значений
            if (vm.ShowValues)
            {
                var tf = new Typeface("Consolas");
                double fs = vm.FiberLabelFontSize;
                if (vm.ShowConcrete)
                    foreach (var f in vm.ConcreteFibers)
                    {
                        var txt = new FormattedText($"{f.Value:G4}", CultureInfo.InvariantCulture,
                            FlowDirection.LeftToRight, tf, fs, Brushes.Black, 1.0);
                        var sc = ToScreen(f.Centroid);
                        dc.DrawText(txt, new Point(sc.X - txt.Width / 2, sc.Y - txt.Height / 2));
                    }
                if (vm.ShowRebar)
                    foreach (var r in vm.RebarFibers)
                    {
                        var txt = new FormattedText($"{r.Value:G4}", CultureInfo.InvariantCulture,
                            FlowDirection.LeftToRight, tf, fs, Brushes.Black, 1.0);
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

            // Нейтральная линия деформаций (ε = 0) — клипируется контуром сечения
            if (vm.NeutralAxis?.Count == 2)
            {
                var p1 = ToScreen(vm.NeutralAxis[0]);
                var p2 = ToScreen(vm.NeutralAxis[1]);
                if (sectionClip != null) dc.PushClip(sectionClip);
                dc.DrawLine(neutralPen, p1, p2);
                if (sectionClip != null) dc.Pop();
            }

            // Центр тяжести НДС
            if (vm.NdsCentroid.HasValue)
            {
                var sc = ToScreen(vm.NdsCentroid.Value);
                DrawCentroidMarker(dc, sc, vm.CentroidNdsSize, ParseBrush(vm.CentroidNdsColorHex));
            }

            // Рамка выделения (пока тянется)
            if (_modAtDown && _didDrag)
            {
                double rx1 = Math.Min(_dragStart.X, _rubberEnd.X);
                double ry1 = Math.Min(_dragStart.Y, _rubberEnd.Y);
                double rx2 = Math.Max(_dragStart.X, _rubberEnd.X);
                double ry2 = Math.Max(_dragStart.Y, _rubberEnd.Y);
                var rubFill = new SolidColorBrush(Color.FromArgb(30, 0xFF, 0xB3, 0x00));
                var rubPen  = new Pen(new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B)), 1)
                              { DashStyle = DashStyles.Dash };
                dc.DrawRectangle(rubFill, rubPen, new Rect(rx1, ry1, rx2 - rx1, ry2 - ry1));
            }

            // Оверлей агрегата выделенных элементов
            int selCount = _selectedFibers.Count + _selectedRebars.Count;
            if (selCount > 0)
            {
                double totalA = _selectedFibers.Sum(f => f.AreaMm2) +
                                _selectedRebars.Sum(r => r.AreaMm2);
                double totalN = (_selectedFibers.Sum(f => f.Sigma * f.AreaMm2) +
                                 _selectedRebars.Sum(r => r.Sigma * r.AreaMm2)) / 1000.0; // кН
                string aggText = $"{selCount} эл.   A = {totalA:F0} мм²   N = {totalN:+0.0;-0.0} кН";

                var tf = new Typeface("Consolas");
                var ft = new FormattedText(aggText, CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, tf, 11, Brushes.Black, 1.0);
                double pad = 6, bx = 8, by = ActualHeight - ft.Height - pad * 2 - 8;
                dc.DrawRoundedRectangle(
                    new SolidColorBrush(Color.FromArgb(210, 0xFF, 0xFD, 0xE7)),
                    new Pen(new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B)), 1),
                    new Rect(bx, by, ft.Width + pad * 2, ft.Height + pad * 2), 4, 4);
                dc.DrawText(ft, new Point(bx + pad, by + pad));
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

        Geometry BuildPathWithHoles(IReadOnlyList<Point> outer, IReadOnlyList<IReadOnlyList<Point>> holes)
        {
            if (holes.Count == 0) return BuildPath(outer);
            var geom = new StreamGeometry { FillRule = FillRule.EvenOdd };
            using var ctx = geom.Open();
            ctx.BeginFigure(ToScreen(outer[0]), true, true);
            for (int i = 1; i < outer.Count; i++)
                ctx.LineTo(ToScreen(outer[i]), true, false);
            foreach (var hole in holes)
            {
                if (hole.Count < 3) continue;
                ctx.BeginFigure(ToScreen(hole[0]), true, true);
                for (int i = 1; i < hole.Count; i++)
                    ctx.LineTo(ToScreen(hole[i]), false, false);
            }
            geom.Freeze();
            return geom;
        }

        void DrawNoMesh(DrawingContext dc, NoMeshAreaDrawData a, bool showValues, Pen hullPen, Pen holePen, double labelFontSize = 9.0)
        {
            dc.DrawGeometry(null, hullPen, BuildPath(a.Hull));
            foreach (var hole in a.Holes)
                dc.DrawGeometry(null, holePen, BuildPath(hole));

            if (showValues)
            {
                var tf = new Typeface("Consolas");
                foreach (var (pt, val) in a.HullValues)
                {
                    var sc = ToScreen(pt);
                    var txt = new FormattedText($"{val:G4}", CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight, tf, labelFontSize, Brushes.DarkBlue, 1.0);
                    dc.DrawText(txt, new Point(sc.X + 2, sc.Y - txt.Height / 2));
                }
            }
        }

        void DrawCentroidMarker(DrawingContext dc, Point sc, double size, Brush brush)
        {
            var pen = new Pen(brush, 2.0);
            double half = size / 2;
            dc.DrawLine(pen, new Point(sc.X - half, sc.Y), new Point(sc.X + half, sc.Y));
            dc.DrawLine(pen, new Point(sc.X, sc.Y - half), new Point(sc.X, sc.Y + half));
            dc.DrawEllipse(null, pen, sc, half * 0.6, half * 0.6);
        }

        static Brush ParseBrush(string hex)
        {
            try
            {
                var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
                b.Freeze();
                return b;
            }
            catch { return Brushes.Black; }
        }

        static Pen MakePen(string colorHex, double thickness)
        {
            var pen = new Pen(ParseBrush(colorHex), thickness);
            pen.Freeze();
            return pen;
        }

        static Pen MakeFreezePen(Brush brush, double thickness)
        {
            brush.Freeze();
            var pen = new Pen(brush, thickness);
            pen.Freeze();
            return pen;
        }

        Geometry BuildHolesGeometry(IReadOnlyList<IReadOnlyList<Point>> holes)
        {
            if (holes.Count == 0) return Geometry.Empty;
            var group = new GeometryGroup();
            foreach (var hole in holes)
                group.Children.Add(BuildPath(hole));
            return group;
        }

        static Pen MakeDashedPen(Brush brush, double thickness)
        {
            var p = new Pen(brush, thickness) { DashStyle = DashStyles.Dash };
            p.Freeze();
            return p;
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
            _modAtDown = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ||
                         Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            _didDrag   = false;
            _dragStart = _rubberEnd = e.GetPosition(this);
            CaptureMouse();
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            bool wasClick  = !_didDrag;
            bool modWasSet = _modAtDown;
            _didDrag   = false;
            _modAtDown = false;
            ReleaseMouseCapture();
            InvalidateVisual(); // убрать рамку

            if (wasClick)
                HandleClick(e.GetPosition(this), modWasSet);
            else if (modWasSet)
                HandleRubberBand(_dragStart, _rubberEnd, additive: true);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (IsMouseCaptured)
            {
                var pos = e.GetPosition(this);
                double dx = pos.X - _dragStart.X;
                double dy = pos.Y - _dragStart.Y;
                if (!_didDrag && dx * dx + dy * dy > 9)
                    _didDrag = true;
                if (_didDrag)
                {
                    if (_modAtDown)
                    {
                        _rubberEnd = pos;       // растягиваем рамку
                        InvalidateVisual();
                    }
                    else
                    {
                        _tx += dx; _ty += dy;   // панорамирование
                        _dragStart = pos;
                        _selPopup.IsOpen = false;
                        InvalidateVisual();
                    }
                }
                return;
            }

            // HitTest для тултипа при наведении
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

        void HandleClick(Point screenPos, bool additive)
        {
            var vm = ViewModel;
            if (vm == null) return;
            var mp = ToModel(screenPos);

            RebarDrawData? hitRebar = null;
            FiberDrawData? hitFiber = null;

            if (vm.ShowRebar)
                foreach (var r in vm.RebarFibers)
                {
                    double dx = mp.X - r.Center.X, dy = mp.Y - r.Center.Y;
                    double ht = r.RadiusMm + 0.5 / _scale;
                    if (dx * dx + dy * dy <= ht * ht) { hitRebar = r; break; }
                }

            if (hitRebar == null && vm.ShowConcrete)
                foreach (var f in vm.ConcreteFibers)
                    if (PointInPoly(mp, f.Vertices)) { hitFiber = f; break; }

            if (hitRebar == null && hitFiber == null)
            {
                if (!additive)
                {
                    _selectedFibers.Clear();
                    _selectedRebars.Clear();
                    _selPopup.IsOpen = false;
                }
                InvalidateVisual();
                return;
            }

            if (!additive)
            {
                _selectedFibers.Clear();
                _selectedRebars.Clear();
            }

            if (hitRebar != null)
            {
                if (additive && _selectedRebars.Contains(hitRebar))
                    _selectedRebars.Remove(hitRebar);
                else
                    _selectedRebars.Add(hitRebar);
            }
            else if (hitFiber != null)
            {
                if (additive && _selectedFibers.Contains(hitFiber))
                    _selectedFibers.Remove(hitFiber);
                else
                    _selectedFibers.Add(hitFiber);
            }

            UpdateSelPopup();
            InvalidateVisual();
        }

        void HandleRubberBand(Point startScreen, Point endScreen, bool additive)
        {
            var vm = ViewModel;
            if (vm == null) return;

            double x1 = Math.Min(startScreen.X, endScreen.X);
            double y1 = Math.Min(startScreen.Y, endScreen.Y);
            double x2 = Math.Max(startScreen.X, endScreen.X);
            double y2 = Math.Max(startScreen.Y, endScreen.Y);

            if (!additive)
            {
                _selectedFibers.Clear();
                _selectedRebars.Clear();
            }

            if (vm.ShowConcrete)
                foreach (var f in vm.ConcreteFibers)
                {
                    var sc = ToScreen(f.Centroid);
                    if (sc.X >= x1 && sc.X <= x2 && sc.Y >= y1 && sc.Y <= y2)
                        _selectedFibers.Add(f);
                }

            if (vm.ShowRebar)
                foreach (var r in vm.RebarFibers)
                {
                    var sc = ToScreen(r.Center);
                    if (sc.X >= x1 && sc.X <= x2 && sc.Y >= y1 && sc.Y <= y2)
                        _selectedRebars.Add(r);
                }

            _selPopup.IsOpen = false;
            InvalidateVisual();
        }

        void UpdateSelPopup()
        {
            int total = _selectedFibers.Count + _selectedRebars.Count;
            if (total == 1)
            {
                string? text = _selectedFibers.Count == 1
                    ? _selectedFibers.First().Tooltip
                    : _selectedRebars.First().Tooltip;
                _selPopupText.Text = text ?? string.Empty;
                _selPopup.IsOpen = false;
                _selPopup.IsOpen = true;
            }
            else
            {
                _selPopup.IsOpen = false;
            }
        }

        static bool PointInPoly(Point p, IReadOnlyList<Point> v)
        {
            int n = v.Count; bool inside = false;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                double xi = v[i].X, yi = v[i].Y, xj = v[j].X, yj = v[j].Y;
                if (((yi > p.Y) != (yj > p.Y)) &&
                    p.X < (xj - xi) * (p.Y - yi) / (yj - yi) + xi)
                    inside = !inside;
            }
            return inside;
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
