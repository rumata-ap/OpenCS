using OpenCS.ViewModels;
using OpenCS.Views.Helpers;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

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
            old.FitAllRequested -= fc.FitToView;
        }
        if (e.NewValue is FireMeshPlotVM vm)
        {
            vm.PropertyChanged += fc.OnVmChanged;
            vm.FitAllRequested += fc.FitToView;
            fc._fitted = false;
        }
    }

    void OnVmChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        => InvalidateVisual();

    static readonly Pen _transparentPen = new(Brushes.Transparent, 0);
    static readonly Pen _outlinePen = new(Brushes.Black, 0.5);

    public FireMeshCanvas()
    {
        ToolTipService.SetToolTip(this, _tip);
        ToolTipService.SetInitialShowDelay(this, 300);
        ToolTipService.SetIsEnabled(this, false);
        ClipToBounds = true;
    }

    protected override Size MeasureOverride(Size availableSize)
        => new(
            double.IsInfinity(availableSize.Width) ? 200 : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? 200 : availableSize.Height);

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (!_fitted && ViewModel != null)
        {
            _fitted = true;
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, FitToView);
        }
        return finalSize;
    }

    public void FitToView()
    {
        var vm = ViewModel;
        if (vm == null || ActualWidth < 1 || ActualHeight < 1) return;

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
        foreach (var r in vm.Points)
        {
            Expand(new Point(r.CenterMm.X - r.RadiusMm, r.CenterMm.Y - r.RadiusMm));
            Expand(new Point(r.CenterMm.X + r.RadiusMm, r.CenterMm.Y + r.RadiusMm));
        }

        if (xMin > xMax) { _scale = 1; _tx = _ty = 0; InvalidateVisual(); return; }

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
    }

    Point ToScreen(Point model) => new(model.X * _scale + _tx, -model.Y * _scale + _ty);
    Point ToModel(Point screen) => new((screen.X - _tx) / _scale, -(screen.Y - _ty) / _scale);

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(SystemColors.WindowBrush, null, new Rect(0, 0, ActualWidth, ActualHeight));
        var vm = ViewModel;
        if (vm == null) return;

        bool useThermal = vm.Mode is FireMeshPlotMode.Temperature or FireMeshPlotMode.Gamma;

        foreach (var t in vm.Triangles)
        {
            var brush = new SolidColorBrush(useThermal
                ? ColormapHelper.GetThermalDiscreteColor(t.Value, vm.ValueMin, vm.ValueMax, FireMeshPlotVM.NumBands)
                : ColormapHelper.GetDiscreteColor(t.Value, vm.ValueMin, vm.ValueMax, false, FireMeshPlotVM.NumBands));
            dc.DrawGeometry(brush, _transparentPen, BuildPathScreen(t.VerticesMm, ToScreen));
        }

        foreach (var r in vm.Points)
        {
            var center = ToScreen(r.CenterMm);
            double radius = r.RadiusMm * _scale;
            var brush = new SolidColorBrush(useThermal
                ? ColormapHelper.GetThermalDiscreteColor(r.Value, vm.ValueMin, vm.ValueMax, FireMeshPlotVM.NumBands)
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
        return g;
    }
}
