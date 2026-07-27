using OpenCS.Views;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace OpenCS.Views.Helpers;

/// <summary>Read-only превью списка PlotElement с ручными pan/zoom (колесо мыши / левая кнопка +
/// перетаскивание) — для PlanarRegionMemberDialog. В отличие от общего PlotCanvas, автофит
/// выполняется только один раз при первом заполнении данными (или после Clear()) — последующие
/// SetElements (правка Hull/Holes, триангуляция) не сбрасывают пользовательский zoom/pan.</summary>
public class PlanarRegionPreviewCanvas : FrameworkElement
{
    IReadOnlyList<PlotElement>? _elements;
    double _xMin, _xMax, _yMin, _yMax;
    bool _hasBounds;
    bool _hasFitted;

    double _scale = 200;
    double _originX;
    double _originY;

    Point _dragStart;
    bool _isPanning;

    public PlanarRegionPreviewCanvas()
    {
        ClipToBounds = true;
        IsHitTestVisible = true;
    }

    protected override Size MeasureOverride(Size availableSize)
        => new(
            double.IsInfinity(availableSize.Width)  ? 200 : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? 200 : availableSize.Height);

    public void SetElements(IReadOnlyList<PlotElement> elements, double xMin, double xMax, double yMin, double yMax)
    {
        _elements = elements;
        _xMin = xMin; _xMax = xMax; _yMin = yMin; _yMax = yMax;
        _hasBounds = true;
        if (!_hasFitted)
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
        _scale = System.Math.Min(sx, sy);

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
        foreach (var el in _elements)
            el.Render(dc, ToScreen);
    }

    Point ToScreen(double mx, double my)
        => new(_scale * (mx - _originX), ActualHeight - _scale * (my - _originY));

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        if (ActualWidth < 2 || ActualHeight < 2) return;
        var pos = e.GetPosition(this);
        double factor = e.Delta > 0 ? 1.2 : 1.0 / 1.2;

        double mx = pos.X / _scale + _originX;
        double my = (ActualHeight - pos.Y) / _scale + _originY;

        _scale *= factor;

        _originX = mx - pos.X / _scale;
        _originY = my - (ActualHeight - pos.Y) / _scale;

        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(this);
        _isPanning = true;
        CaptureMouse();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!_isPanning) return;
        var pos = e.GetPosition(this);
        double dx = pos.X - _dragStart.X;
        double dy = pos.Y - _dragStart.Y;
        _originX -= dx / _scale;
        _originY += dy / _scale;
        _dragStart = pos;
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        _isPanning = false;
        ReleaseMouseCapture();
    }
}
