using OpenCS.ViewModels;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace OpenCS.Views.Helpers;

/// <summary>Канва превью МКЭ-сетки кручения в диалоге задачи.</summary>
public sealed class TorsionMeshPreviewCanvas : FrameworkElement
{
    double _scale = 1.0;
    double _tx, _ty;
    Point _dragStart;
    bool _dragging;
    bool _fitted;

    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(nameof(ViewModel), typeof(TorsionMeshPreviewVM),
            typeof(TorsionMeshPreviewCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender,
                OnViewModelChanged));

    public TorsionMeshPreviewVM? ViewModel
    {
        get => (TorsionMeshPreviewVM?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var c = (TorsionMeshPreviewCanvas)d;
        if (e.OldValue is TorsionMeshPreviewVM old)
        {
            old.PropertyChanged -= c.OnVmChanged;
            old.FitAllRequested -= c.FitToView;
        }
        if (e.NewValue is TorsionMeshPreviewVM vm)
        {
            vm.PropertyChanged += c.OnVmChanged;
            vm.FitAllRequested += c.FitToView;
            c._fitted = false;
            c.RequestAutoFit();
        }
    }

    void OnVmChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TorsionMeshPreviewVM.HasGeometry)
            && ViewModel?.HasGeometry == true
            && !_fitted)
            RequestAutoFit();
        InvalidateVisual();
    }

    static readonly Pen _hullPen = new(Brushes.Black, 0.8);
    static readonly Pen _meshEdgePen = new(Brushes.Black, 0.3);

    public TorsionMeshPreviewCanvas()
    {
        ClipToBounds = true;
        SizeChanged += (_, _) => RequestAutoFit();
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 280 : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? 280 : availableSize.Height);

    protected override Size ArrangeOverride(Size finalSize)
    {
        RequestAutoFit();
        return finalSize;
    }

    void RequestAutoFit()
    {
        if (_fitted || ViewModel == null || !ViewModel.HasGeometry) return;
        if (ActualWidth < 1 || ActualHeight < 1) return;
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
            if (p.X < xMin) xMin = p.X; if (p.X > xMax) xMax = p.X;
            if (p.Y < yMin) yMin = p.Y; if (p.Y > yMax) yMax = p.Y;
        }

        foreach (var p in vm.OuterHull) Expand(p);
        foreach (var hole in vm.Holes)
            foreach (var p in hole) Expand(p);

        if (xMin > xMax) { _scale = 1; _tx = _ty = 0; InvalidateVisual(); return; }

        const double pad = 16;
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

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(SystemColors.WindowBrush, null, new Rect(0, 0, ActualWidth, ActualHeight));
        var vm = ViewModel;
        if (vm == null || !vm.HasGeometry) return;

        if (vm.OuterHull.Count >= 3)
            dc.DrawGeometry(TorsionMeshPreviewVM.OuterFillBrush, null, BuildPath(vm.OuterHull));
        foreach (var hole in vm.Holes)
            if (hole.Count >= 3)
                dc.DrawGeometry(TorsionMeshPreviewVM.HoleFillBrush, null, BuildPath(hole));

        foreach (var t in vm.Triangles)
        {
            var color = TorsionMeshPreviewVM.MeshColorForAngle(t.MinAngleDeg);
            var brush = new SolidColorBrush(color);
            dc.DrawGeometry(brush, _meshEdgePen, BuildPath(t.Vertices));
        }

        foreach (var hole in vm.Holes)
            if (hole.Count >= 3)
                dc.DrawGeometry(null, _hullPen, BuildPath(hole));
        if (vm.OuterHull.Count >= 3)
            dc.DrawGeometry(null, _hullPen, BuildPath(vm.OuterHull));
    }

    Geometry BuildPath(IReadOnlyList<Point> verts)
    {
        var g = new StreamGeometry();
        using var ctx = g.Open();
        if (verts.Count < 3) return g;
        ctx.BeginFigure(ToScreen(verts[0]), true, true);
        for (int i = 1; i < verts.Count; i++)
            ctx.LineTo(ToScreen(verts[i]), true, false);
        g.Freeze();
        return g;
    }

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

    Point ToModel(Point screen) => new((screen.X - _tx) / _scale, -(screen.Y - _ty) / _scale);

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            _dragging = true;
            _dragStart = e.GetPosition(this);
            CaptureMouse();
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!_dragging || e.LeftButton != MouseButtonState.Pressed) return;
        var pos = e.GetPosition(this);
        Vector d = pos - _dragStart;
        _tx += d.X;
        _ty += d.Y;
        _dragStart = pos;
        InvalidateVisual();
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        if (_dragging)
        {
            _dragging = false;
            ReleaseMouseCapture();
        }
    }
}
