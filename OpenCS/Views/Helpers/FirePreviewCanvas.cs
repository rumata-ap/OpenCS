using OpenCS.Utilites;
using OpenCS.ViewModels;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace OpenCS.Views.Helpers;

/// <summary>
/// Интерактивная канва превью огневого сечения: зум, панорамирование, выбор ГУ в режиме «Вручную», оверлей сетки.
/// </summary>
public sealed class FirePreviewCanvas : FrameworkElement
{
    double _scale = 1.0;
    double _tx, _ty;
    Point _dragStart;
    bool _dragging;
    bool _fitted;
    bool _panWithLmb;

    readonly ToolTip _tip = new();
    string? _lastTip;

    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(nameof(ViewModel), typeof(FirePreviewVM),
            typeof(FirePreviewCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender,
                OnViewModelChanged));

    public FirePreviewVM? ViewModel
    {
        get => (FirePreviewVM?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var fc = (FirePreviewCanvas)d;
        if (e.OldValue is FirePreviewVM old)
        {
            old.PropertyChanged -= fc.OnVmChanged;
            old.FitAllRequested -= fc.FitToView;
        }
        if (e.NewValue is FirePreviewVM vm)
        {
            vm.PropertyChanged += fc.OnVmChanged;
            vm.FitAllRequested += fc.FitToView;
            fc._fitted = false;
            fc.RequestAutoFit();
        }
    }

    void OnVmChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FirePreviewVM.HasGeometry)
            && ViewModel?.HasGeometry == true
            && !_fitted)
            RequestAutoFit();
        InvalidateVisual();
    }

    static readonly Pen _transparentPen = new(Brushes.Transparent, 0);
    static readonly Pen _outlinePen = new(Brushes.Black, 0.5);
    static readonly Pen _hullPen = new(Brushes.Black, 0.8);
    static readonly Pen _meshEdgePen = new(Brushes.Black, 0.3);
    static readonly Pen _meshMidsidePen = new(Brushes.Black, 0.6);
    const double MeshMidsideNodeRadiusPx = 1.2;

    public FirePreviewCanvas()
    {
        ToolTipService.SetToolTip(this, _tip);
        ToolTipService.SetInitialShowDelay(this, 300);
        ToolTipService.SetIsEnabled(this, false);
        ClipToBounds = true;
        Focusable = true;
        SizeChanged += (_, _) => RequestAutoFit();
    }

    protected override Size MeasureOverride(Size availableSize)
        => new(
            double.IsInfinity(availableSize.Width) ? 200 : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? 200 : availableSize.Height);

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (!_fitted && ViewModel?.HasGeometry == true)
        {
            _fitted = true;
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, FitToView);
        }
        return finalSize;
    }

    void RequestAutoFit()
    {
        if (_fitted || ViewModel == null || !ViewModel.HasGeometry)
            return;
        if (ActualWidth < 1 || ActualHeight < 1)
            return;
        _fitted = true;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, FitToView);
    }

    public void FitToView()
    {
        var vm = ViewModel;
        if (vm == null || !vm.HasGeometry || ActualWidth < 1 || ActualHeight < 1) return;

        double xMin = double.MaxValue, xMax = double.MinValue;
        double yMin = double.MaxValue, yMax = double.MinValue;

        void Expand(Point p)
        {
            if (p.X < xMin) xMin = p.X;
            if (p.X > xMax) xMax = p.X;
            if (p.Y < yMin) yMin = p.Y;
            if (p.Y > yMax) yMax = p.Y;
        }

        foreach (var p in vm.OuterHull) Expand(p);
        foreach (var h in vm.Holes)
            foreach (var p in h.Vertices) Expand(p);
        foreach (var r in vm.Rebars)
        {
            Expand(new Point(r.Center.X - r.RadiusM, r.Center.Y - r.RadiusM));
            Expand(new Point(r.Center.X + r.RadiusM, r.Center.Y + r.RadiusM));
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
        if (vm == null || !vm.HasGeometry) return;

        if (vm.OuterHull.Count >= 3)
            dc.DrawGeometry(FirePreviewVM.OuterFillBrush, null, BuildPathScreen(vm.OuterHull));

        foreach (var hole in vm.Holes)
        {
            if (hole.Vertices.Count >= 3)
                dc.DrawGeometry(FirePreviewVM.HoleFillBrush, null, BuildPathScreen(hole.Vertices));
        }

        if (vm.ShowMesh)
        {
            foreach (var t in vm.MeshTriangles)
            {
                var color = FirePreviewVM.MeshColorForAngle(t.MinAngleDeg);
                var brush = new SolidColorBrush(color);
                dc.DrawGeometry(brush, _meshEdgePen, BuildPathScreen(t.Vertices));
            }

            if (vm.MeshMidsideNodes.Count > 0)
            {
                foreach (var p in vm.MeshMidsideNodes)
                {
                    var c = ToScreen(p);
                    dc.DrawEllipse(
                        FirePreviewVM.MeshMidsideNodeBrush,
                        _meshMidsidePen,
                        c,
                        MeshMidsideNodeRadiusPx,
                        MeshMidsideNodeRadiusPx);
                }
            }
        }

        foreach (var hole in vm.Holes)
            dc.DrawGeometry(null, _hullPen, BuildPathScreen(hole.Vertices));
        if (vm.OuterHull.Count >= 3)
            dc.DrawGeometry(null, _hullPen, BuildPathScreen(vm.OuterHull));

        foreach (var e in vm.OuterEdges)
            DrawEdge(dc, e, vm);
        foreach (var hole in vm.Holes)
            foreach (var e in hole.Edges)
                DrawEdge(dc, e, vm);

        foreach (var r in vm.Rebars)
        {
            var center = ToScreen(r.Center);
            double radius = r.RadiusM * _scale;
            dc.DrawEllipse(FirePreviewVM.RebarFillBrush, _outlinePen, center, radius, radius);
        }

        if (vm.IsManualBc)
        {
            var hint = new FormattedText(
                Loc.S("FireSection_ManualHint"),
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), 10,
                new SolidColorBrush(Color.FromArgb(160, 80, 80, 80)), 1.0);
            dc.DrawText(hint, new Point(6, ActualHeight - hint.Height - 4));
        }
    }

    void DrawEdge(DrawingContext dc, FirePreviewEdgeDraw e, FirePreviewVM vm)
    {
        var brush = FirePreviewVM.BrushForBc(e.BcType);
        var pen = new Pen(brush, 3);
        var a = ToScreen(e.A);
        var b = ToScreen(e.B);
        dc.DrawLine(pen, a, b);

        if (vm.ShowEdgeLabels)
        {
            var mid = new Point((a.X + b.X) * 0.5, (a.Y + b.Y) * 0.5);
            var label = e.EdgeIndex.ToString(CultureInfo.InvariantCulture);
            var ft = new FormattedText(label, CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, new Typeface("Segoe UI"), 8, Brushes.Black, 1.0);
            double r = Math.Max(ft.Width, ft.Height) * 0.55 + 2;
            dc.DrawEllipse(Brushes.White, _outlinePen, mid, r, r);
            dc.DrawText(ft, new Point(mid.X - ft.Width / 2, mid.Y - ft.Height / 2));
        }
    }

    static Geometry BuildPathScreen(IReadOnlyList<Point> verts, Func<Point, Point>? toScreen = null)
    {
        Func<Point, Point> map = toScreen ?? (p => p);
        var g = new StreamGeometry();
        using var ctx = g.Open();
        if (verts.Count < 3) return g;
        ctx.BeginFigure(map(verts[0]), true, true);
        for (int i = 1; i < verts.Count; i++)
            ctx.LineTo(map(verts[i]), true, false);
        return g;
    }

    Geometry BuildPathScreen(IReadOnlyList<Point> verts)
        => BuildPathScreen(verts, ToScreen);

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

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle ||
            (e.ChangedButton == MouseButton.Left && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)))
        {
            _panWithLmb = e.ChangedButton == MouseButton.Left;
            _dragging = true;
            _dragStart = e.GetPosition(this);
            CaptureMouse();
            e.Handled = true;
            return;
        }

        if (e.ChangedButton == MouseButton.Left && ViewModel?.IsManualBc == true)
        {
            var edge = HitTestEdge(e.GetPosition(this));
            if (edge != null)
            {
                ViewModel.CycleEdgeBc(edge);
                e.Handled = true;
                return;
            }
        }

        if (e.ChangedButton == MouseButton.Left)
        {
            _panWithLmb = true;
            _dragging = true;
            _dragStart = e.GetPosition(this);
            CaptureMouse();
        }
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        if (_dragging && (e.ChangedButton == MouseButton.Middle ||
            e.ChangedButton == MouseButton.Left))
        {
            _dragging = false;
            _panWithLmb = false;
            ReleaseMouseCapture();
        }
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
            return;
        }
        UpdateTooltip(pos);
    }

    void UpdateTooltip(Point screen)
    {
        var vm = ViewModel;
        if (vm == null) return;
        var model = ToModel(screen);
        string? tip = null;

        var edge = HitTestEdge(screen);
        if (edge != null)
        {
            string bcLabel = edge.BcType switch
            {
                "fire" => Loc.S("FireSection_BcFire"),
                "ambient" => Loc.S("FireSection_BcAmbient"),
                _ => Loc.S("FireSection_BcAdiabatic")
            };
            tip = string.Format(Loc.S("FireSection_EdgeTooltip"), edge.EdgeIndex, bcLabel);
            if (vm.IsManualBc)
                tip += "\n" + Loc.S("FireSection_EdgeClickHint");
        }

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

    FirePreviewEdgeDraw? HitTestEdge(Point screen)
    {
        var vm = ViewModel;
        if (vm == null) return null;

        double tolPx = 8;
        double tolModel = tolPx / Math.Max(_scale, 1e-9);
        var model = ToModel(screen);

        FirePreviewEdgeDraw? best = null;
        double bestDist = tolModel;

        void Test(FirePreviewEdgeDraw e)
        {
            double d = DistToSegment(model, e.A, e.B);
            if (d < bestDist)
            {
                bestDist = d;
                best = e;
            }
        }

        foreach (var e in vm.OuterEdges) Test(e);
        foreach (var h in vm.Holes)
            foreach (var e in h.Edges) Test(e);

        return best;
    }

    static double DistToSegment(Point p, Point a, Point b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double len2 = dx * dx + dy * dy;
        if (len2 < 1e-18) return Math.Sqrt((p.X - a.X) * (p.X - a.X) + (p.Y - a.Y) * (p.Y - a.Y));
        double t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2, 0, 1);
        double px = a.X + t * dx, py = a.Y + t * dy;
        double ex = p.X - px, ey = p.Y - py;
        return Math.Sqrt(ex * ex + ey * ey);
    }
}
