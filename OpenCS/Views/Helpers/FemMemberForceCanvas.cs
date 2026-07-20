using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Linq;
using System.Collections.Generic;

namespace OpenCS.Views.Helpers;

/// <summary>2D-эпюра выбранной компоненты усилия вдоль одного конструктивного стержня.</summary>
public sealed class FemMemberForceCanvas : Canvas
{
    /// <summary>Сегмент эпюры: дуговые координаты концов и значения усилия.</summary>
    public readonly record struct Segment(double S0, double S1, double V0, double V1);

    IReadOnlyList<Segment> _segments = [];
    string _title = "";

    private readonly Line _hoverLine;
    private readonly TextBlock _hoverZLabel;
    private readonly TextBlock _hoverVLabel;

    double _x0, _x1, _axisY, _sMin, _sMax, _sSpan, _vMax;

    public FemMemberForceCanvas()
    {
        Background = Brushes.White;
        SizeChanged += (_, _) => Redraw();

        _hoverLine = new Line
        {
            Stroke = Brushes.DimGray, StrokeThickness = 0.8,
            StrokeDashArray = new DoubleCollection { 4, 3 },
            Visibility = Visibility.Collapsed,
        };
        _hoverZLabel = new TextBlock { FontSize = 9, Foreground = Brushes.DimGray, Visibility = Visibility.Collapsed, TextAlignment = TextAlignment.Center };
        _hoverVLabel = new TextBlock { FontSize = 10, Foreground = Brushes.Black, FontWeight = FontWeights.Bold, Visibility = Visibility.Collapsed };

        MouseMove += OnMouseMove;
        MouseLeave += OnMouseLeave;
    }

    /// <summary>Задаёт данные эпюры и перерисовывает.</summary>
    public void SetData(IReadOnlyList<Segment> segments, string title)
    {
        _segments = segments ?? [];
        _title = title ?? "";
        Redraw();
    }

    void Redraw()
    {
        for (int i = Children.Count - 1; i >= 0; i--)
        {
            var c = Children[i];
            if (c == _hoverLine || c == _hoverZLabel || c == _hoverVLabel) continue;
            Children.RemoveAt(i);
        }
        HideHover();
        if (ActualWidth < 20 || ActualHeight < 20) return;

        const double margin = 40;
        double w = ActualWidth, h = ActualHeight;
        _x0 = margin; _x1 = w - margin;
        _axisY = h / 2;

        _sMax = _segments.Count > 0 ? _segments.Max(s => s.S1) : 0;
        _sMin = _segments.Count > 0 ? _segments.Min(s => s.S0) : 0;
        _sSpan = _sMax - _sMin;
        _vMax = _segments.Count > 0
            ? _segments.SelectMany(s => new[] { System.Math.Abs(s.V0), System.Math.Abs(s.V1) }).DefaultIfEmpty(0).Max()
            : 0;

        // Заголовок
        AddText(_title, _x0, 6, Brushes.Black, 13, true);

        if (_sSpan <= 1e-9 || _vMax <= 1e-12)
        {
            AddText("нет данных", _x0, _axisY - 8, Brushes.Gray, 12, false);
            DrawAxis(_x0, _x1, _axisY);
            EnsureHoverOverlays();
            return;
        }

        double MapX(double s) => _x0 + (s - _sMin) / _sSpan * (_x1 - _x0);
        double MapY(double v) => _axisY - v / _vMax * (h / 2 - margin);

        var fill = new SolidColorBrush(Color.FromArgb(90, 0x2b, 0x6c, 0xb0));
        var stroke = new SolidColorBrush(Color.FromRgb(0x2b, 0x6c, 0xb0));

        foreach (var seg in _segments)
        {
            var poly = new Polygon
            {
                Fill = fill,
                Stroke = stroke,
                StrokeThickness = 1,
                Points =
                [
                    new Point(MapX(seg.S0), _axisY),
                    new Point(MapX(seg.S0), MapY(seg.V0)),
                    new Point(MapX(seg.S1), MapY(seg.V1)),
                    new Point(MapX(seg.S1), _axisY),
                ]
            };
            Children.Add(poly);
        }

        DrawAxis(_x0, _x1, _axisY);

        // Подписи экстремумов
        AddText(_vMax.ToString("G4", CultureInfo.InvariantCulture), _x1 - 60, MapY(_vMax) - 16, Brushes.Black, 11, false);
        AddText((-_vMax).ToString("G4", CultureInfo.InvariantCulture), _x1 - 60, MapY(-_vMax) + 2, Brushes.Black, 11, false);

        EnsureHoverOverlays();
    }

    void EnsureHoverOverlays()
    {
        foreach (UIElement ov in new UIElement[] { _hoverLine, _hoverZLabel, _hoverVLabel })
        {
            Children.Remove(ov);
            Children.Add(ov);
        }
    }

    void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_segments.Count == 0 || _sSpan <= 1e-9 || _vMax <= 1e-12) { HideHover(); return; }

        var pos = e.GetPosition(this);
        if (pos.X < _x0 || pos.X > _x1 || pos.Y < 0 || pos.Y > ActualHeight)
        { HideHover(); return; }

        double s = _sMin + (pos.X - _x0) / (_x1 - _x0) * _sSpan;
        s = System.Math.Max(_sMin, System.Math.Min(_sMax, s));

        double v = 0;
        bool found = false;
        foreach (var seg in _segments)
        {
            if (s >= seg.S0 && s <= seg.S1)
            {
                double span = seg.S1 - seg.S0;
                v = span > 1e-9 ? seg.V0 + (s - seg.S0) / span * (seg.V1 - seg.V0) : seg.V0;
                found = true;
                break;
            }
        }
        if (!found)
        {
            var closest = _segments.OrderBy(seg => System.Math.Min(System.Math.Abs(s - seg.S0), System.Math.Abs(s - seg.S1))).First();
            v = System.Math.Abs(s - closest.S0) < System.Math.Abs(s - closest.S1) ? closest.V0 : closest.V1;
            s = System.Math.Abs(s - closest.S0) < System.Math.Abs(s - closest.S1) ? closest.S0 : closest.S1;
        }

        double xLine = _x0 + (s - _sMin) / _sSpan * (_x1 - _x0);
        double yLine = _axisY - v / _vMax * (ActualHeight / 2 - 40);

        _hoverLine.X1 = xLine; _hoverLine.X2 = xLine;
        _hoverLine.Y1 = 0; _hoverLine.Y2 = ActualHeight;
        _hoverLine.Visibility = Visibility.Visible;

        _hoverZLabel.Text = s.ToString("G3", CultureInfo.InvariantCulture);
        SetLeft(_hoverZLabel, xLine - 15);
        SetTop(_hoverZLabel, ActualHeight - 16);
        _hoverZLabel.Visibility = Visibility.Visible;

        _hoverVLabel.Text = v.ToString("G4", CultureInfo.InvariantCulture);
        SetLeft(_hoverVLabel, xLine + 6);
        SetTop(_hoverVLabel, yLine - 16);
        _hoverVLabel.Visibility = Visibility.Visible;
    }

    void OnMouseLeave(object sender, MouseEventArgs e) => HideHover();

    void HideHover()
    {
        _hoverLine.Visibility = _hoverZLabel.Visibility = _hoverVLabel.Visibility = Visibility.Collapsed;
    }

    void DrawAxis(double x0, double x1, double axisY)
    {
        Children.Add(new Line { X1 = x0, Y1 = axisY, X2 = x1, Y2 = axisY, Stroke = Brushes.Black, StrokeThickness = 1.2 });
    }

    void AddText(string text, double x, double y, Brush brush, double size, bool bold)
    {
        var tb = new TextBlock { Text = text, Foreground = brush, FontSize = size };
        if (bold) tb.FontWeight = FontWeights.Bold;
        SetLeft(tb, x);
        SetTop(tb, y);
        Children.Add(tb);
    }
}
