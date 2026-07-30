using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace OpenCS.Views.Helpers;

/// <summary>Read-only превью списка PlotElement для PlanarRegionMemberDialog. Колесо мыши всегда
/// масштабирует вид (чистая навигация камеры, геометрию Hull/Holes не трогает — геометрические
/// правки выполняются через PlanarRegionMemberVM.TranslateGeometry/ScaleGeometry/RotateGeometryDegrees,
/// вызываемые из диалоговых кнопок "Переместить"/"Масштаб"/"Повернуть"). Сетка с числовыми метками
/// осей. Автофит выполняется один раз при первом заполнении данными (или после Clear()) —
/// последующие SetElements не сбрасывают пользовательский zoom.</summary>
public class PlanarRegionPreviewCanvas : FrameworkElement
{
    IReadOnlyList<PlotElement>? _elements;
    double _xMin, _xMax, _yMin, _yMax;
    bool _hasBounds;
    bool _hasFitted;

    double _scale = 200;
    double _originX;
    double _originY;

    public PlanarRegionPreviewCanvas()
    {
        ClipToBounds = true;
        IsHitTestVisible = true;
    }

    protected override Size MeasureOverride(Size availableSize)
        => new(
            double.IsInfinity(availableSize.Width)  ? 200 : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? 200 : availableSize.Height);

    protected override Size ArrangeOverride(Size finalSize)
    {
        var result = base.ArrangeOverride(finalSize);
        if (_hasBounds && !_hasFitted)
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            {
                if (_hasBounds && !_hasFitted) { FitToView(); _hasFitted = true; }
            });
        return result;
    }

    public void SetElements(IReadOnlyList<PlotElement> elements, double xMin, double xMax, double yMin, double yMax)
    {
        _elements = elements;
        _xMin = xMin; _xMax = xMax; _yMin = yMin; _yMax = yMax;
        _hasBounds = true;
        if (!_hasFitted && ActualWidth >= 2 && ActualHeight >= 2)
        {
            FitToView();
            _hasFitted = true;
        }
        InvalidateVisual();
    }

    public void FitToView()
    {
        if (!_hasBounds || ActualWidth < 2 || ActualHeight < 2) return;

        double padX = (_xMax - _xMin) * 0.05 + 0.0001;
        double padY = (_yMax - _yMin) * 0.05 + 0.0001;
        double xMin = _xMin - padX, xMax = _xMax + padX;
        double yMin = _yMin - padY, yMax = _yMax + padY;

        double sx = ActualWidth / (xMax - xMin);
        double sy = ActualHeight / (yMax - yMin);
        _scale = Math.Min(sx, sy);

        double modelW = ActualWidth / _scale;
        double modelH = ActualHeight / _scale;
        _originX = xMin - (modelW - (xMax - xMin)) / 2;
        _originY = yMin - (modelH - (yMax - yMin)) / 2;

        InvalidateVisual();
    }

    public void Clear()
    {
        _elements = null;
        _hasBounds = false;
        _hasFitted = false;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 2 || h < 2) return;
        dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, w, h));

        if (_elements == null || !_hasBounds) return;

        DrawGridAndAxes(dc, w, h);
        foreach (var el in _elements)
            el.Render(dc, ToScreen);
    }

    Point ToScreen(double mx, double my)
        => new(_scale * (mx - _originX), ActualHeight - _scale * (my - _originY));

    (double X, double Y) ToModel(Point screen)
        => (screen.X / _scale + _originX, (ActualHeight - screen.Y) / _scale + _originY);

    /// <summary>Подбирает _originX/_originY так, чтобы заданная модельная точка оказалась ровно в
    /// заданной экранной точке — используется после изменения _scale, чтобы зум колесом мыши не
    /// «прыгал».</summary>
    void AnchorOn((double X, double Y) model, Point screenTarget)
    {
        _originX = model.X - screenTarget.X / _scale;
        _originY = model.Y - (ActualHeight - screenTarget.Y) / _scale;
    }

    void DrawGridAndAxes(DrawingContext dc, double w, double h)
    {
        double xMin = _originX, xMax = _originX + w / _scale;
        double yMin = _originY, yMax = _originY + h / _scale;

        var ticksX = NiceTicks(xMin, xMax);
        var ticksY = NiceTicks(yMin, yMax);

        var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(60, 0, 0, 0)), 0.5) { DashStyle = DashStyles.Dot };
        foreach (var x in ticksX)
        {
            double px = ToScreen(x, 0).X;
            if (px > 0 && px < w) dc.DrawLine(gridPen, new Point(px, 0), new Point(px, h));
        }
        foreach (var y in ticksY)
        {
            double py = ToScreen(0, y).Y;
            if (py > 0 && py < h) dc.DrawLine(gridPen, new Point(0, py), new Point(w, py));
        }

        var axisPen = new Pen(Brushes.Black, 1);
        var tickPen = new Pen(Brushes.Black, 0.8);
        var typeface = new Typeface("Segoe UI");
        const double fontSize = 10;
        const double tickLen = 4, gap = 3;

        double axisPxX = Clamp(ToScreen(0, 0).X, 0, w);
        double axisPxY = Clamp(ToScreen(0, 0).Y, 0, h);
        dc.DrawLine(axisPen, new Point(0, axisPxY), new Point(w, axisPxY));
        dc.DrawLine(axisPen, new Point(axisPxX, 0), new Point(axisPxX, h));

        foreach (var t in ticksX)
        {
            double px = ToScreen(t, 0).X;
            if (px < 0 || px > w) continue;
            dc.DrawLine(tickPen, new Point(px, axisPxY - tickLen), new Point(px, axisPxY + tickLen));
            var ft = new FormattedText(FormatTick(t), CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, typeface, fontSize, Brushes.Black, 96);
            double ly = axisPxY + tickLen + gap + ft.Height <= h ? axisPxY + tickLen + gap : axisPxY - tickLen - gap - ft.Height;
            dc.DrawText(ft, new Point(Clamp(px - ft.Width / 2, 0, w - ft.Width), Clamp(ly, 0, h - ft.Height)));
        }
        foreach (var t in ticksY)
        {
            double py = ToScreen(0, t).Y;
            if (py < 0 || py > h) continue;
            dc.DrawLine(tickPen, new Point(axisPxX - tickLen, py), new Point(axisPxX + tickLen, py));
            var ft = new FormattedText(FormatTick(t), CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, typeface, fontSize, Brushes.Black, 96);
            double lx = axisPxX - ft.Width - tickLen - gap >= 0 ? axisPxX - ft.Width - tickLen - gap : axisPxX + tickLen + gap;
            dc.DrawText(ft, new Point(Clamp(lx, 0, w - ft.Width), Clamp(py - ft.Height / 2, 0, h - ft.Height)));
        }
    }

    static double[] NiceTicks(double min, double max, int targetCount = 6)
    {
        if (max - min < 1e-12) return [min];
        double range = max - min;
        double roughStep = range / targetCount;
        double exponent = Math.Floor(Math.Log10(roughStep));
        double fraction = roughStep / Math.Pow(10, exponent);
        double niceStep = fraction <= 1.5 ? 1 : fraction <= 3 ? 2 : fraction <= 7 ? 5 : 10;
        niceStep *= Math.Pow(10, exponent);

        double first = Math.Ceiling(min / niceStep) * niceStep;
        var list = new List<double>();
        for (double v = first; v <= max + niceStep * 0.5; v += niceStep)
            list.Add(v);
        return [.. list];
    }

    static string FormatTick(double v)
    {
        var av = Math.Abs(v);
        if (av < 1e-12) return "0";
        if (av < 0.001) return v.ToString("E2");
        if (av < 0.01) return v.ToString("F5");
        if (av < 1) return v.ToString("F4");
        if (av < 100) return v.ToString("F2");
        if (av < 10000) return v.ToString("F0");
        return v.ToString("E2");
    }

    static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        if (ActualWidth < 2 || ActualHeight < 2) return;
        var pos = e.GetPosition(this);
        var before = ToModel(pos);
        double factor = e.Delta > 0 ? 1.2 : 1.0 / 1.2;
        _scale *= factor;
        AnchorOn(before, pos);
        InvalidateVisual();
        e.Handled = true;
    }

    public event Action<double, double>? ModelClicked;

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (ActualWidth < 2 || ActualHeight < 2) return;
        var (x, y) = ToModel(e.GetPosition(this));
        ModelClicked?.Invoke(x, y);
    }

    bool _isPanning;
    Point _panDragStart;

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.ChangedButton != MouseButton.Middle) return;
        _isPanning = true;
        _panDragStart = e.GetPosition(this);
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_isPanning || !IsMouseCaptured) return;
        var pos = e.GetPosition(this);
        _originX -= (pos.X - _panDragStart.X) / _scale;
        _originY += (pos.Y - _panDragStart.Y) / _scale;
        _panDragStart = pos;
        InvalidateVisual();
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.ChangedButton != MouseButton.Middle || !_isPanning) return;
        _isPanning = false;
        ReleaseMouseCapture();
    }
}
