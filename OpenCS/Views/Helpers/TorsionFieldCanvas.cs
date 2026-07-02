using OpenCS.ViewModels;
using OpenCS.Views.Helpers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace OpenCS.Views.Helpers;

/// <summary>Canvas для карт поля кручения (МКЭ — треугольники, МГЭ — граничные отрезки).</summary>
public class TorsionFieldCanvas : FrameworkElement
{
    double _scale = 1.0;
    double _tx, _ty;
    Point _dragStart;
    bool _fitted;

    readonly ToolTip _tip = new();
    SmoothFieldBitmap.RasterResult? _raster;

    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(nameof(ViewModel), typeof(TorsionPlotVM),
            typeof(TorsionFieldCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender,
                OnVmChanged));

    public TorsionPlotVM? ViewModel
    {
        get => (TorsionPlotVM?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    static void OnVmChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var c = (TorsionFieldCanvas)d;
        if (e.OldValue is TorsionPlotVM old)
        {
            old.PropertyChanged -= c.OnVmPropertyChanged;
            old.FitAllRequested -= c.FitToView;
        }
        if (e.NewValue is TorsionPlotVM vm)
        {
            vm.PropertyChanged += c.OnVmPropertyChanged;
            vm.FitAllRequested += c.FitToView;
            c._fitted = false;
        }
        c._raster = null;
    }

    void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TorsionPlotVM.NeedRedraw) or nameof(TorsionPlotVM.VMin) or nameof(TorsionPlotVM.VMax))
            _raster = null;
        InvalidateVisual();
    }

    static readonly Pen _outlinePen = MakeFreezePen(Brushes.Black, 1.2);
    static readonly Pen _holePen = MakeFreezePen(Brushes.DimGray, 0.8);
    static readonly Pen _markerPen = MakeFreezePen(Brushes.DarkRed, 2);
    static readonly Pen _scPen = MakeFreezePen(Brushes.DarkGreen, 2);

    static Pen MakeFreezePen(Brush brush, double thickness)
    {
        var p = new Pen(brush, thickness);
        p.Freeze();
        return p;
    }

    public TorsionFieldCanvas()
    {
        ToolTipService.SetToolTip(this, _tip);
        ToolTipService.SetInitialShowDelay(this, 250);
        ToolTipService.SetIsEnabled(this, false);
        ClipToBounds = true;
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 300 : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? 300 : availableSize.Height);

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
            if (p.X < xMin) xMin = p.X; if (p.X > xMax) xMax = p.X;
            if (p.Y < yMin) yMin = p.Y; if (p.Y > yMax) yMax = p.Y;
        }

        foreach (var p in vm.OuterHullMm) Expand(p);
        foreach (var hole in vm.HolesMm)
            foreach (var p in hole) Expand(p);
        foreach (var t in vm.Triangles) { Expand(t.A); Expand(t.B); Expand(t.C); }
        foreach (var s in vm.Segments) { Expand(s.A); Expand(s.B); }

        if (xMin > xMax) { _scale = 1; _tx = _ty = 0; InvalidateVisual(); return; }

        double pad = 24;
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

        EnsureRaster(vm);

        if (_raster != null)
        {
            var r = _raster;
            dc.DrawImage(r.Bitmap, new Rect(
                r.XMinMm * _scale + _tx,
                -r.YMaxMm * _scale + _ty,
                (r.XMaxMm - r.XMinMm) * _scale,
                (r.YMaxMm - r.YMinMm) * _scale));
        }
        else
        {
            DrawSegments(dc, vm);
            foreach (var (pt, val) in vm.NodeFallback)
            {
                if (!double.IsFinite(val)) continue;
                var brush = new SolidColorBrush(vm.GetColor(val));
                dc.DrawEllipse(brush, null, ToScreen(pt), 4, 4);
            }
        }

        DrawContours(dc, vm);

        if (vm.ShearCenterMm is { } sc)
            DrawCross(dc, sc, _scPen, 8);

        if (vm.TauMaxPointMm is { } tm)
            dc.DrawEllipse(Brushes.OrangeRed, _markerPen, ToScreen(tm), 5, 5);
    }

    void EnsureRaster(TorsionPlotVM vm)
    {
        if (vm.Triangles.Count == 0) { _raster = null; return; }
        if (_raster != null) return;
        _raster = SmoothFieldBitmap.Build(vm.Triangles.ToList(), vm.VMin, vm.VMax,
            (v, _, _) => vm.GetColor(v))?.Freeze();
    }

    void DrawSegments(DrawingContext dc, TorsionPlotVM vm)
    {
        foreach (var s in vm.Segments)
        {
            if (!double.IsFinite(s.ValA)) continue;
            var pen = new Pen(new SolidColorBrush(vm.GetColor(s.ValA)), 4)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };
            dc.DrawLine(pen, ToScreen(s.A), ToScreen(s.B));
        }
    }

    void DrawContours(DrawingContext dc, TorsionPlotVM vm)
    {
        if (vm.OuterHullMm.Count >= 3)
            DrawPolyline(dc, vm.OuterHullMm, _outlinePen, closed: true);
        foreach (var hole in vm.HolesMm)
            if (hole.Count >= 3)
                DrawPolyline(dc, hole, _holePen, closed: true);
    }

    void DrawPolyline(DrawingContext dc, IReadOnlyList<Point> pts, Pen pen, bool closed)
    {
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(ToScreen(pts[0]), false, closed);
            for (int i = 1; i < pts.Count; i++)
                ctx.LineTo(ToScreen(pts[i]), true, false);
        }
        geo.Freeze();
        dc.DrawGeometry(null, pen, geo);
    }

    void DrawCross(DrawingContext dc, Point model, Pen pen, double size)
    {
        var c = ToScreen(model);
        dc.DrawLine(pen, new Point(c.X - size, c.Y), new Point(c.X + size, c.Y));
        dc.DrawLine(pen, new Point(c.X, c.Y - size), new Point(c.X, c.Y + size));
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        var pos = e.GetPosition(this);
        double factor = e.Delta > 0 ? 1.15 : 1.0 / 1.15;
        double mx = (pos.X - _tx) / _scale;
        double my = -(pos.Y - _ty) / _scale;
        _scale *= factor;
        _tx = pos.X - mx * _scale;
        _ty = pos.Y + my * _scale;
        _raster = null;
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            _dragStart = e.GetPosition(this);
            CaptureMouse();
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var pos = e.GetPosition(this);
        if (e.LeftButton == MouseButtonState.Pressed && IsMouseCaptured)
        {
            Vector d = pos - _dragStart;
            _tx += d.X;
            _ty += d.Y;
            _dragStart = pos;
            _raster = null;
            InvalidateVisual();
            return;
        }
        UpdateTooltip(pos);
    }

    void UpdateTooltip(Point screenPos)
    {
        var vm = ViewModel;
        if (vm == null) { ToolTipService.SetIsEnabled(this, false); return; }

        var model = ToModel(screenPos);
        double bestDist2 = double.MaxValue;
        double bestVal = double.NaN;
        Point bestPt = model;
        bool found = false;

        foreach (var t in vm.Triangles)
        {
            if (PointInTri(model, t.A, t.B, t.C, out double w0, out double w1, out double w2))
            {
                bestVal = w0 * t.Va + w1 * t.Vb + w2 * t.Vc;
                bestPt = model;
                found = true;
                break;
            }
        }

        if (!found)
        {
            foreach (var s in vm.Segments)
            {
                double d2 = DistToSegment(model, s.A, s.B, out var proj);
                if (d2 < bestDist2) { bestDist2 = d2; bestPt = proj; bestVal = s.ValA; }
            }
        }

        double thresh = 12 / _scale;
        if (!found && bestDist2 > thresh * thresh)
        {
            ToolTipService.SetIsEnabled(this, false);
            return;
        }

        string tip = $"x={bestPt.X:F1}  y={bestPt.Y:F1}\n{vm.FormatValue(bestVal)} {vm.ValueUnit}";
        _tip.Content = tip;
        ToolTipService.SetIsEnabled(this, true);
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        if (IsMouseCaptured) ReleaseMouseCapture();
    }

    static bool PointInTri(Point p, Point a, Point b, Point c,
        out double w0, out double w1, out double w2)
    {
        double area = (b.X - a.X) * (c.Y - a.Y) - (c.X - a.X) * (b.Y - a.Y);
        if (Math.Abs(area) < 1e-18) { w0 = w1 = w2 = 0; return false; }
        w0 = ((b.X - p.X) * (c.Y - p.Y) - (c.X - p.X) * (b.Y - p.Y)) / area;
        w1 = ((c.X - p.X) * (a.Y - p.Y) - (a.X - p.X) * (c.Y - p.Y)) / area;
        w2 = 1 - w0 - w1;
        const double eps = -1e-9;
        return w0 >= eps && w1 >= eps && w2 >= eps;
    }

    static double DistToSegment(Point p, Point a, Point b, out Point proj)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double len2 = dx * dx + dy * dy;
        if (len2 < 1e-18) { proj = a; var ex = p.X - a.X; var ey = p.Y - a.Y; return ex * ex + ey * ey; }
        double t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2, 0, 1);
        proj = new Point(a.X + t * dx, a.Y + t * dy);
        double px = p.X - proj.X, py = p.Y - proj.Y;
        return px * px + py * py;
    }
}
