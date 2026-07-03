using OpenCS.ViewModels;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace OpenCS.Views.Helpers;

/// <summary>
/// Интерактивный WPF-канвас σ(ε)-диаграммы: кривые Ic/It, перетаскиваемые маркеры,
/// зум колёсиком мыши, пан ЛКМ на пустом месте, hover-линия, магнит на излом-точках.
/// </summary>
public sealed class DiagramCanvas : FrameworkElement
{
    // ─── Трансформация экран↔модель ───
    double _scaleX = 1, _scaleY = 1;
    double _tx, _ty;
    bool _fitted;

    // ─── Взаимодействие ───
    Point _dragStart;
    bool  _panning;
    int   _dragIdx = -1;   // индекс перетаскиваемой точки (-1 = нет)

    // ─── Hover ───
    Point? _hoverScreen;   // null = курсор вне канваса или идёт drag/pan
    const double SnapRadius = 15.0;

    // ─── Pen/brush кэш ───
    static readonly Pen   _bluePen       = new(new SolidColorBrush(Color.FromRgb(0, 0, 180)), 1.5);
    static readonly Pen   _redPen        = new(new SolidColorBrush(Color.FromRgb(180, 0, 0)), 1.5);
    static readonly Pen   _axisPen       = new(Brushes.LightGray, 0.8);
    static readonly Pen   _blueMarkerPen = new(new SolidColorBrush(Color.FromRgb(0, 0, 160)), 1.0);
    static readonly Pen   _redMarkerPen  = new(new SolidColorBrush(Color.FromRgb(160, 0, 0)), 1.0);
    static readonly Pen   _charMarkerPen = new(Brushes.Black, 2.0);
    static readonly Brush _blueFill      = new SolidColorBrush(Color.FromRgb(120, 120, 255));
    static readonly Brush _redFill       = new SolidColorBrush(Color.FromRgb(255, 120, 120));

    static readonly Pen   _hoverLinePen  = MakeHoverLinePen();
    static readonly Pen   _snapRingPen   = new(new SolidColorBrush(Color.FromRgb(220, 120, 0)), 1.5);

    static Pen MakeHoverLinePen()
    {
        var p = new Pen(new SolidColorBrush(Color.FromArgb(160, 80, 80, 80)), 0.8);
        p.DashStyle = new DashStyle(new double[] { 4, 3 }, 0);
        p.Freeze();
        return p;
    }

    static readonly Typeface _labelTypeface = new("Segoe UI");

    const double MarkerR = 5.0;

    static DiagramCanvas()
    {
        _bluePen.Freeze(); _redPen.Freeze(); _axisPen.Freeze();
        _blueMarkerPen.Freeze(); _redMarkerPen.Freeze();
        _charMarkerPen.Freeze();
        _blueFill.Freeze(); _redFill.Freeze();
        _snapRingPen.Freeze();
    }

    // ─── DependencyProperty ───
    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(nameof(ViewModel), typeof(DiagramEditVM),
            typeof(DiagramCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender,
                OnVmChanged));

    public DiagramEditVM? ViewModel
    {
        get => (DiagramEditVM?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    static void OnVmChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var c = (DiagramCanvas)d;
        if (e.OldValue is DiagramEditVM old)
            old.Points.CollectionChanged -= c.OnPointsChanged;
        if (e.NewValue is DiagramEditVM vm)
        {
            vm.Points.CollectionChanged += c.OnPointsChanged;
            c._fitted = false;
        }
    }

    void OnPointsChanged(object? s,
        System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        => InvalidateVisual();

    public DiagramCanvas() { ClipToBounds = true; }

    // ─── Measure/Arrange ───
    protected override Size MeasureOverride(Size a)
        => new(double.IsInfinity(a.Width) ? 300 : a.Width,
               double.IsInfinity(a.Height) ? 200 : a.Height);

    protected override Size ArrangeOverride(Size s)
    {
        if (!_fitted && ViewModel != null)
        {
            _fitted = true;
            Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Loaded,
                (Action)FitToView);
        }
        return s;
    }

    // ─── FitToView ───
    public void FitToView()
    {
        var vm = ViewModel;
        if (vm == null || ActualWidth < 1 || ActualHeight < 1) return;

        var pts = vm.Points;
        if (pts.Count == 0)
        {
            _scaleX = _scaleY = 100;
            _tx = ActualWidth / 2;
            _ty = ActualHeight / 2;
            InvalidateVisual();
            return;
        }

        double epsMin = Math.Min(pts.Min(p => p.Eps), 0);
        double epsMax = Math.Max(pts.Max(p => p.Eps), 0);
        double sigMin = Math.Min(pts.Min(p => p.Sig), 0);
        double sigMax = Math.Max(pts.Max(p => p.Sig), 0);

        const double pad = 35;
        double sw = ActualWidth  - 2 * pad;
        double sh = ActualHeight - 2 * pad;
        double dE = epsMax - epsMin; if (dE < 1e-12) dE = 0.01;
        double dS = sigMax - sigMin; if (dS < 1e-12) dS = 1;

        _scaleX = sw / dE;
        _scaleY = sh / dS;
        _tx = pad - epsMin * _scaleX;
        _ty = pad + sigMax * _scaleY;
        InvalidateVisual();
    }

    Point ToScreen(double eps, double sig)
        => new(eps * _scaleX + _tx, -sig * _scaleY + _ty);

    (double eps, double sig) ToModel(Point screen)
        => ((screen.X - _tx) / _scaleX, -(screen.Y - _ty) / _scaleY);

    // ─── OnRender ───
    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(SystemColors.WindowBrush, null,
            new Rect(0, 0, ActualWidth, ActualHeight));

        var vm = ViewModel;
        if (vm == null) return;

        // Оси
        var origin = ToScreen(0, 0);
        dc.DrawLine(_axisPen, new Point(0, origin.Y), new Point(ActualWidth, origin.Y));
        dc.DrawLine(_axisPen, new Point(origin.X, 0), new Point(origin.X, ActualHeight));

        var sorted = vm.Points.OrderBy(p => p.Eps).ToList();

        // Ic (синий): ε ≤ 0
        var icPts = sorted.Where(p => p.Eps <= 1e-15).ToList();
        DrawBranch(dc, icPts, _bluePen, _blueFill, _blueMarkerPen);

        // It (красный): ε ≥ 0
        var itPts = sorted.Where(p => p.Eps >= -1e-15).ToList();
        DrawBranch(dc, itPts, _redPen, _redFill, _redMarkerPen);

        // Подписи осей
        DrawLabel(dc, "ε", new Point(ActualWidth - 18, origin.Y - 16));
        DrawLabel(dc, "σ", new Point(origin.X + 4, 4));

        DrawHoverOverlay(dc, vm);
    }

    void DrawHoverOverlay(DrawingContext dc, DiagramEditVM vm)
    {
        if (!_hoverScreen.HasValue) return;
        var pos = _hoverScreen.Value;

        // Найти ближайшую точку в радиусе SnapRadius
        DiagramPoint? snap = null;
        double bestD = SnapRadius;
        foreach (var pt in vm.Points)
        {
            var sc = ToScreen(pt.Eps, pt.Sig);
            double dx = pos.X - sc.X, dy = pos.Y - sc.Y;
            double d = Math.Sqrt(dx * dx + dy * dy);
            if (d < bestD) { bestD = d; snap = pt; }
        }

        double lineX;
        string labelText;
        if (snap != null)
        {
            var sc = ToScreen(snap.Eps, snap.Sig);
            lineX = sc.X;
            // Кольцо-магнит вокруг захваченной точки
            dc.DrawEllipse(null, _snapRingPen, sc, MarkerR + 5, MarkerR + 5);
            string epsStr = snap.Eps.ToString("F6", CultureInfo.InvariantCulture);
            string sigStr = snap.Sig.ToString("F2", CultureInfo.InvariantCulture);
            labelText = $"ε={epsStr}  σ={sigStr}";
        }
        else
        {
            lineX = pos.X;
            var (eps, _) = ToModel(pos);
            double sig = InterpSig(vm, eps);
            string epsStr2 = eps.ToString("F6", CultureInfo.InvariantCulture);
            string sigStr2 = sig.ToString("F2", CultureInfo.InvariantCulture);
            labelText = $"ε={epsStr2}  σ={sigStr2}";
        }

        // Вертикальная пунктирная линия
        dc.DrawLine(_hoverLinePen, new Point(lineX, 0), new Point(lineX, ActualHeight));

        // Подпись значения
        var ft = new FormattedText(labelText,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            _labelTypeface, 10, Brushes.DimGray,
            pixelsPerDip: 1.0);
        double labelX = lineX + 5;
        if (labelX + ft.Width > ActualWidth - 4) labelX = lineX - ft.Width - 5;
        dc.DrawText(ft, new Point(labelX, 4));
    }

    void DrawBranch(DrawingContext dc,
                    System.Collections.Generic.List<DiagramPoint> pts,
                    Pen linePen, Brush fill, Pen markerPen)
    {
        if (pts.Count >= 2)
        {
            var geom = new StreamGeometry();
            using (var ctx = geom.Open())
            {
                ctx.BeginFigure(ToScreen(pts[0].Eps, pts[0].Sig), false, false);
                for (int i = 1; i < pts.Count; i++)
                    ctx.LineTo(ToScreen(pts[i].Eps, pts[i].Sig), true, false);
            }
            geom.Freeze();
            dc.DrawGeometry(null, linePen, geom);
        }

        foreach (var p in pts)
        {
            var sc = ToScreen(p.Eps, p.Sig);
            if (p.IsCharacteristic)
                dc.DrawEllipse(fill, _charMarkerPen, sc, MarkerR + 2, MarkerR + 2);
            else
                dc.DrawEllipse(fill, markerPen, sc, MarkerR, MarkerR);
        }
    }

    static void DrawLabel(DrawingContext dc, string text, Point pos)
    {
        var ft = new FormattedText(text,
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            _labelTypeface, 11, Brushes.Gray,
            pixelsPerDip: 1.0);
        dc.DrawText(ft, pos);
    }

    // ─── Hit-test точек ───
    int HitTestPoint(Point screen)
    {
        var vm = ViewModel;
        if (vm == null) return -1;
        var pts = vm.Points.ToList();
        for (int i = 0; i < pts.Count; i++)
        {
            var s = ToScreen(pts[i].Eps, pts[i].Sig);
            double dx = screen.X - s.X, dy = screen.Y - s.Y;
            if (dx * dx + dy * dy <= (MarkerR + 3) * (MarkerR + 3))
                return i;
        }
        return -1;
    }

    // ─── Mouse events ───
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(this);
        _dragIdx = HitTestPoint(pos);
        if (_dragIdx >= 0)
        {
            _hoverScreen = null;
            CaptureMouse();
        }
        else
        {
            _panning   = true;
            _dragStart = pos;
            _hoverScreen = null;
            CaptureMouse();
        }
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        _dragIdx = -1;
        _panning = false;
        _hoverScreen = e.GetPosition(this);
        ReleaseMouseCapture();
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var pos = e.GetPosition(this);

        if (_dragIdx >= 0 && ViewModel != null)
        {
            var (eps, sig) = ToModel(pos);
            var pt  = ViewModel.Points[_dragIdx];
            pt.Eps  = eps;
            pt.Sig  = sig;
            _hoverScreen = null;
            InvalidateVisual();
        }
        else if (_panning)
        {
            _tx += pos.X - _dragStart.X;
            _ty += pos.Y - _dragStart.Y;
            _dragStart = pos;
            _hoverScreen = null;
            InvalidateVisual();
        }
        else
        {
            _hoverScreen = pos;
            InvalidateVisual();
        }
        e.Handled = true;
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        _hoverScreen = null;
        InvalidateVisual();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        var pos    = e.GetPosition(this);
        double factor = e.Delta > 0 ? 1.15 : 1.0 / 1.15;
        var (eps0, sig0) = ToModel(pos);
        _scaleX *= factor;
        _scaleY *= factor;
        _tx = pos.X - eps0 * _scaleX;
        _ty = pos.Y + sig0 * _scaleY;
        _hoverScreen = pos;
        InvalidateVisual();
        e.Handled = true;
    }

    // Линейная интерполяция σ по ε из отсортированного набора точек диаграммы.
    static double InterpSig(DiagramEditVM vm, double eps)
    {
        var pts = vm.Points.OrderBy(p => p.Eps).ToList();
        if (pts.Count == 0) return 0;
        if (eps <= pts[0].Eps)     return pts[0].Sig;
        if (eps >= pts[^1].Eps)    return pts[^1].Sig;
        for (int i = 1; i < pts.Count; i++)
        {
            if (pts[i].Eps >= eps)
            {
                double t = (eps - pts[i - 1].Eps) / (pts[i].Eps - pts[i - 1].Eps);
                return pts[i - 1].Sig + t * (pts[i].Sig - pts[i - 1].Sig);
            }
        }
        return pts[^1].Sig;
    }
}
