using OpenCS.ViewModels;
using OpenCS.Views.Dialogs;
using OpenCS.Views.Helpers;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
namespace OpenCS.Views.Helpers;

/// <summary>
/// Интерактивная канва T3-карты (температура, γ, σ, ε): зум колёсиком, пан ЛКМ.
/// </summary>
public sealed class FireMeshCanvas : FrameworkElement
{
    double _scale = 1.0;
    double _tx, _ty;
    Point _dragStart;
    bool _dragging;
    bool _fitted;

    readonly ToolTip _tip = new();
    string? _lastTip;

    DrawingGroup? _smoothField;
    int _smoothFieldRedraw = -1;
    bool _smoothFieldBuildScheduled;
    int _smoothFieldBuildGeneration;
    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(nameof(ViewModel), typeof(FireMeshPlotVM),
            typeof(FireMeshCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender,
                OnViewModelChanged));

    public FireMeshPlotVM? ViewModel
    {
        get => (FireMeshPlotVM?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var fc = (FireMeshCanvas)d;
        if (e.OldValue is FireMeshPlotVM old)
        {
            old.PropertyChanged -= fc.OnVmChanged;
            old.FitAllRequested -= fc.OnFitAllRequested;
            fc.InvalidateThermalCache();
        }
        if (e.NewValue is FireMeshPlotVM vm)
        {
            vm.PropertyChanged += fc.OnVmChanged;
            vm.FitAllRequested += fc.OnFitAllRequested;
            fc._fitted = false;
            fc.InvalidateThermalCache();
            fc.ScheduleSmoothFieldBuild(vm);
            fc.RequestAutoFit();
        }
    }

    void OnFitAllRequested()
    {
        _fitted = false;
        if (FitToView())
            _fitted = true;
    }

    void OnVmChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FireMeshPlotVM.NeedRedraw)
            or nameof(FireMeshPlotVM.SmoothColormap)
            or nameof(FireMeshPlotVM.Triangles)
            or nameof(FireMeshPlotVM.ValueMin)
            or nameof(FireMeshPlotVM.ValueMax)
            or nameof(FireMeshPlotVM.Isolines)
            or nameof(FireMeshPlotVM.IsolineLabels)
            or nameof(FireMeshPlotVM.SectionContours)
            or nameof(FireMeshPlotVM.MeshEdges))
        {
            if (e.PropertyName is nameof(FireMeshPlotVM.Triangles)
                or nameof(FireMeshPlotVM.SectionContours))
                RequestAutoFit();
            if (e.PropertyName is nameof(FireMeshPlotVM.NeedRedraw)
                or nameof(FireMeshPlotVM.SmoothColormap)
                or nameof(FireMeshPlotVM.Triangles)
                or nameof(FireMeshPlotVM.ValueMin)
                or nameof(FireMeshPlotVM.ValueMax))
                InvalidateThermalCache();
        }
        if (ViewModel != null)
            ScheduleSmoothFieldBuild(ViewModel);
        InvalidateVisual();
    }

    void InvalidateThermalCache()
    {
        _smoothField = null;
        _smoothFieldRedraw = -1;
        _smoothFieldBuildScheduled = false;
        _smoothFieldBuildGeneration++;
    }

    static bool UseSmoothThermalField(FireMeshPlotVM vm)
        => vm.Mode == FireMeshPlotMode.Temperature && vm.SmoothColormap;

    void ScheduleSmoothFieldBuild(FireMeshPlotVM vm)
    {
        if (!UseSmoothThermalField(vm))
            return;
        if (_smoothField != null && _smoothFieldRedraw == vm.NeedRedraw)
            return;
        if (_smoothFieldBuildScheduled)
            return;

        _smoothFieldBuildScheduled = true;
        int buildGen = _smoothFieldBuildGeneration;

        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (ViewModel != vm || buildGen != _smoothFieldBuildGeneration || !UseSmoothThermalField(vm))
            {
                _smoothFieldBuildScheduled = false;
                return;
            }

            if (_smoothField != null && _smoothFieldRedraw == vm.NeedRedraw)
            {
                _smoothFieldBuildScheduled = false;
                return;
            }

            var owner = Window.GetWindow(this);
            bool ownerWasEnabled = owner?.IsEnabled ?? true;
            if (owner != null)
                owner.IsEnabled = false;

            var loading = new FireSmoothMapLoadingWindow { Owner = owner };

            async void OnContentRendered(object? sender, EventArgs e)
            {
                loading.ContentRendered -= OnContentRendered;
                try
                {
                    await Dispatcher.InvokeAsync(static () => { }, DispatcherPriority.Render);
                    await RebuildSmoothFieldAsync(vm, buildGen);
                    InvalidateVisual();
                }
                finally
                {
                    loading.Close();
                    if (owner != null)
                        owner.IsEnabled = ownerWasEnabled;
                    _smoothFieldBuildScheduled = false;
                }
            }

            loading.ContentRendered += OnContentRendered;
            loading.Show();
        });
    }

    async Task RebuildSmoothFieldAsync(FireMeshPlotVM vm, int buildGen)
    {
        if (!UseSmoothThermalField(vm))
            return;
        if (_smoothField != null && _smoothFieldRedraw == vm.NeedRedraw)
            return;
        if (ViewModel != vm || buildGen != _smoothFieldBuildGeneration)
            return;

        if (!vm.TryGetThermalRasterData(out var mesh, out var nodalT, out _, out _))
            return;

        var dg = await FireSmoothThermalField.BuildT3Async(mesh, nodalT, Dispatcher);
        if (ViewModel != vm || buildGen != _smoothFieldBuildGeneration || !UseSmoothThermalField(vm))
            return;

        _smoothField = dg;
        _smoothFieldRedraw = vm.NeedRedraw;
    }

    void DrawSmoothField(DrawingContext dc)
    {
        if (_smoothField == null) return;
        dc.PushTransform(new MatrixTransform(_scale, 0, 0, -_scale, _tx, _ty));
        dc.DrawDrawing(_smoothField);
        dc.Pop();
    }
    static readonly Pen _transparentPen = new(Brushes.Transparent, 0);
    static readonly Pen _outlinePen = new(Brushes.Black, 0.5);
    static readonly Pen _isolinePen = CreateFrozenPen(0, 0, 0, 0.55, 0.8);
    static readonly Pen _meshPen = CreateFrozenPen(80, 80, 80, 0.35, 0.4);
    static readonly Pen _outerContourPen = CreateFrozenPen(20, 20, 20, 0.9, 1.2);
    static readonly Pen _holeContourPen = CreateFrozenPen(20, 20, 20, 0.75, 1.0);
    static readonly Brush _thermalBgBrush = CreateFrozenBrush(232, 232, 232);
    static readonly Brush _labelHaloBrush = CreateFrozenBrush(255, 255, 255);
    static readonly Typeface _labelTypeface = new("Segoe UI");

    static Brush CreateFrozenBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    static Pen CreateFrozenPen(byte r, byte g, byte b, double opacity, double thickness)
    {
        var brush = new SolidColorBrush(Color.FromArgb((byte)(opacity * 255), r, g, b));
        brush.Freeze();
        var pen = new Pen(brush, thickness);
        pen.Freeze();
        return pen;
    }

    public FireMeshCanvas()
    {
        ToolTipService.SetToolTip(this, _tip);
        ToolTipService.SetInitialShowDelay(this, 300);
        ToolTipService.SetIsEnabled(this, false);
        ClipToBounds = true;
        SizeChanged += (_, _) => RequestAutoFit();
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true)
                RequestAutoFitOnShow();
        };
    }

    protected override Size MeasureOverride(Size availableSize)
        => new(
            double.IsInfinity(availableSize.Width) ? 200 : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? 200 : availableSize.Height);

    protected override Size ArrangeOverride(Size finalSize)
    {
        RequestAutoFit();
        return finalSize;
    }

    bool HasPlottableGeometry()
    {
        var vm = ViewModel;
        if (vm == null)
            return false;
        if (vm.Triangles.Count > 0)
            return true;
        foreach (var contour in vm.SectionContours)
        {
            if (contour.PointsMm.Count > 0)
                return true;
        }
        return false;
    }

    /// <summary>Подогнать вид при первом появлении геометрии или размера канвы.</summary>
    void RequestAutoFit()
    {
        if (_fitted || ViewModel == null || !HasPlottableGeometry())
            return;
        if (ActualWidth < 1 || ActualHeight < 1)
            return;

        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            if (_fitted || ViewModel == null || !HasPlottableGeometry())
                return;
            if (ActualWidth < 1 || ActualHeight < 1)
                return;
            if (FitToView())
                _fitted = true;
        });
    }

    /// <summary>Сбросить масштаб и подогнать всё поле (при активации вкладки).</summary>
    public void RequestAutoFitOnShow()
    {
        _fitted = false;
        RequestAutoFit();
    }

    public bool FitToView()
    {
        var vm = ViewModel;
        if (vm == null || ActualWidth < 1 || ActualHeight < 1)
            return false;

        double xMin = double.MaxValue, xMax = double.MinValue;
        double yMin = double.MaxValue, yMax = double.MinValue;

        void Expand(Point p)
        {
            if (p.X < xMin) xMin = p.X;
            if (p.X > xMax) xMax = p.X;
            if (p.Y < yMin) yMin = p.Y;
            if (p.Y > yMax) yMax = p.Y;
        }

        foreach (var t in vm.Triangles)
            foreach (var p in t.VerticesMm) Expand(p);
        if (vm.ShowIsolines)
        {
            foreach (var seg in vm.Isolines)
            {
                Expand(seg.A);
                Expand(seg.B);
            }
        }
        foreach (var r in vm.Points)
        {
            Expand(new Point(r.CenterMm.X - r.RadiusMm, r.CenterMm.Y - r.RadiusMm));
            Expand(new Point(r.CenterMm.X + r.RadiusMm, r.CenterMm.Y + r.RadiusMm));
        }
        foreach (var contour in vm.SectionContours)
            foreach (var p in contour.PointsMm)
                Expand(p);

        if (xMin > xMax)
        {
            _scale = 1;
            _tx = _ty = 0;
            InvalidateVisual();
            return false;
        }

        const double pad = 20;
        double sw = ActualWidth - 2 * pad;
        double sh = ActualHeight - 2 * pad;
        double mw = xMax - xMin, mh = yMax - yMin;
        if (mw < 1e-6) mw = 1;
        if (mh < 1e-6) mh = 1;

        _scale = Math.Min(sw / mw, sh / mh);
        _tx = pad + (sw - mw * _scale) / 2 - xMin * _scale;
        _ty = pad + (sh - mh * _scale) / 2 + yMax * _scale;
        InvalidateVisual();
        return true;
    }

    Point ToScreen(Point model) => new(model.X * _scale + _tx, -model.Y * _scale + _ty);
    Point ToModel(Point screen) => new((screen.X - _tx) / _scale, -(screen.Y - _ty) / _scale);

    protected override void OnRender(DrawingContext dc)
    {
        var vm = ViewModel;
        dc.DrawRectangle(
            vm?.Mode == FireMeshPlotMode.Temperature ? _thermalBgBrush : SystemColors.WindowBrush,
            null, new Rect(0, 0, ActualWidth, ActualHeight));
        if (vm == null) return;

        bool useThermal = vm.Mode is FireMeshPlotMode.Temperature or FireMeshPlotMode.Gamma;
        bool smoothThermal = UseSmoothThermalField(vm);

        if (smoothThermal)
        {
            if (_smoothField == null)
                ScheduleSmoothFieldBuild(vm);
            if (_smoothField != null)
                DrawSmoothField(dc);
            else
                DrawDiscreteThermalTriangles(dc, vm);
        }
        else
        {
            DrawDiscreteThermalTriangles(dc, vm, useThermal);
        }

        if (vm.IsTemperatureMode)
        {
            DrawMeshEdges(dc, vm);
            DrawSectionContours(dc, vm);
        }

        if (vm.ShowIsolines && vm.Isolines.Count > 0)
        {
            foreach (var seg in vm.Isolines)
            {
                var a = ToScreen(seg.A);
                var b = ToScreen(seg.B);
                dc.DrawLine(_isolinePen, a, b);
            }

            if (vm.ShowIsolineLabels)
                DrawIsolineLabels(dc, vm);
        }

        foreach (var r in vm.Points)
        {
            var center = ToScreen(r.CenterMm);
            double radius = r.RadiusMm * _scale;
            var brush = new SolidColorBrush(useThermal
                ? ColormapHelper.GetThermalColor(r.Value, vm.ValueMin, vm.ValueMax)
                : ColormapHelper.GetDiscreteColor(r.Value, vm.ValueMin, vm.ValueMax, true, FireMeshPlotVM.NumBands));
            dc.DrawEllipse(brush, _outlinePen, center, radius, radius);
        }

        if (vm.ShowValues)
        {
            var tf = new Typeface("Consolas");
            foreach (var t in vm.Triangles)
            {
                var txt = new FormattedText(FormatVal(t.Value), CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, tf, 9, Brushes.Black, 1.0);
                var sc = ToScreen(t.CentroidMm);
                dc.DrawText(txt, new Point(sc.X - txt.Width / 2, sc.Y - txt.Height / 2));
            }
        }
    }

    void DrawMeshEdges(DrawingContext dc, FireMeshPlotVM vm)
    {
        foreach (var edge in vm.MeshEdges)
            dc.DrawLine(_meshPen, ToScreen(edge.AMm), ToScreen(edge.BMm));
    }

    void DrawSectionContours(DrawingContext dc, FireMeshPlotVM vm)
    {
        foreach (var contour in vm.SectionContours)
        {
            if (contour.PointsMm.Count < 2)
                continue;

            var pen = contour.IsHole ? _holeContourPen : _outerContourPen;
            for (int i = 0; i < contour.PointsMm.Count; i++)
            {
                var a = ToScreen(contour.PointsMm[i]);
                var b = ToScreen(contour.PointsMm[(i + 1) % contour.PointsMm.Count]);
                dc.DrawLine(pen, a, b);
            }
        }
    }

    void DrawIsolineLabels(DrawingContext dc, FireMeshPlotVM vm)
    {
        const double fontSize = 9.0;
        const double haloPad = 2.0;

        foreach (var label in vm.IsolineLabels)
        {
            var screen = ToScreen(label.PositionMm);
            var text = new FormattedText(
                label.Text,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                _labelTypeface,
                fontSize,
                Brushes.Black,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);

            double w = text.Width + 2 * haloPad;
            double h = text.Height + 2 * haloPad;
            var origin = new Point(screen.X - w * 0.5, screen.Y - h * 0.5);

            dc.PushTransform(new RotateTransform(label.AngleDeg, screen.X, screen.Y));
            dc.DrawRectangle(_labelHaloBrush, null, new Rect(origin, new Size(w, h)));
            dc.DrawText(text, new Point(origin.X + haloPad, origin.Y + haloPad));
            dc.Pop();
        }
    }

    void DrawDiscreteThermalTriangles(DrawingContext dc, FireMeshPlotVM vm, bool? useThermalOverride = null)
    {
        bool useThermal = useThermalOverride
            ?? vm.Mode is FireMeshPlotMode.Temperature or FireMeshPlotMode.Gamma;
        foreach (var t in vm.Triangles)
        {
            var brush = new SolidColorBrush(useThermal
                ? ColormapHelper.GetThermalDiscreteColor(t.Value, vm.ValueMin, vm.ValueMax, FireMeshPlotVM.NumBands)
                : ColormapHelper.GetDiscreteColor(t.Value, vm.ValueMin, vm.ValueMax, false, FireMeshPlotVM.NumBands));
            dc.DrawGeometry(brush, _transparentPen, BuildPathScreen(t.VerticesMm, ToScreen));
        }
    }

    static string FormatVal(double v)
        => Math.Abs(v) > 100 ? $"{v:F0}" : $"{v:G4}";

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        var pos = e.GetPosition(this);
        double factor = e.Delta > 0 ? 1.1 : 1 / 1.1;
        var model = ToModel(pos);
        _scale *= factor;
        _tx = pos.X - model.X * _scale;
        _ty = pos.Y + model.Y * _scale;
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        _dragging = true;
        _dragStart = e.GetPosition(this);
        CaptureMouse();
        UpdateTooltip(e.GetPosition(this));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        _dragging = false;
        ReleaseMouseCapture();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var pos = e.GetPosition(this);
        if (_dragging)
        {
            _tx += pos.X - _dragStart.X;
            _ty += pos.Y - _dragStart.Y;
            _dragStart = pos;
            InvalidateVisual();
        }
        UpdateTooltip(pos);
    }

    void UpdateTooltip(Point screen)
    {
        var vm = ViewModel;
        if (vm == null) return;
        var model = ToModel(screen);
        string? tip = HitTest(vm, model);
        if (tip != _lastTip)
        {
            _lastTip = tip;
            if (string.IsNullOrEmpty(tip))
            {
                ToolTipService.SetIsEnabled(this, false);
                _tip.Content = null;
            }
            else
            {
                _tip.Content = tip;
                ToolTipService.SetIsEnabled(this, true);
            }
        }
    }

    static string? HitTest(FireMeshPlotVM vm, Point modelMm)
    {
        foreach (var r in vm.Points)
        {
            double dx = modelMm.X - r.CenterMm.X;
            double dy = modelMm.Y - r.CenterMm.Y;
            if (dx * dx + dy * dy <= r.RadiusMm * r.RadiusMm)
                return r.Tooltip;
        }
        foreach (var t in vm.Triangles)
        {
            if (PointInTri(modelMm, t.VerticesMm))
                return t.Tooltip;
        }
        return null;
    }

    static bool PointInTri(Point p, IReadOnlyList<Point> tri)
    {
        if (tri.Count < 3) return false;
        static double Sign(Point a, Point b, Point c)
            => (a.X - c.X) * (b.Y - c.Y) - (b.X - c.X) * (a.Y - c.Y);
        double d1 = Sign(p, tri[0], tri[1]);
        double d2 = Sign(p, tri[1], tri[2]);
        double d3 = Sign(p, tri[2], tri[0]);
        bool hasNeg = d1 < 0 || d2 < 0 || d3 < 0;
        bool hasPos = d1 > 0 || d2 > 0 || d3 > 0;
        return !(hasNeg && hasPos);
    }

    static Geometry BuildPathScreen(IReadOnlyList<Point> vertsMm, Func<Point, Point> toScreen)
    {
        var g = new StreamGeometry();
        using var ctx = g.Open();
        if (vertsMm.Count < 3) return g;
        ctx.BeginFigure(toScreen(vertsMm[0]), true, true);
        ctx.LineTo(toScreen(vertsMm[1]), true, false);
        ctx.LineTo(toScreen(vertsMm[2]), true, false);
        g.Freeze();
        return g;
    }
}
