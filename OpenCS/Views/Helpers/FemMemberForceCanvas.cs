using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Linq;
using System.Collections.Generic;
using OpenCS.Utilites;

namespace OpenCS.Views.Helpers;

/// <summary>2D-эпюра выбранной компоненты усилия вдоль одного конструктивного стержня
/// с маркерами точек интегрирования.</summary>
public sealed class FemMemberForceCanvas : Canvas
{
    /// <summary>Сегмент эпюры: дуговые координаты концов и значения усилия.</summary>
    public readonly record struct Segment(double S0, double S1, double V0, double V1);

    /// <summary>Маркер точки интегрирования на эпюре.</summary>
    /// <param name="Failed">Точка не прошла проверку — маркер красный.</param>
    public readonly record struct Marker(double S, bool Available, object Key, string Label, bool Failed = false);

    /// <summary>Клик ЛКМ по маркеру ТИ.</summary>
    public event Action<object>? MarkerClicked;
    /// <summary>Клик ПКМ по маркеру ТИ (вызов контекстного меню).</summary>
    public event Action<object>? MarkerContextMenuRequested;

    IReadOnlyList<Segment> _segments = [];
    IReadOnlyList<Segment> _secondary = [];
    IReadOnlyList<Segment> _reference = [];
    IReadOnlyList<Marker> _markers = [];
    object? _selectedMarkerKey;
    string _title = "";

    private readonly Line _hoverLine;
    private readonly TextBlock _hoverZLabel;
    private readonly TextBlock _hoverVLabel;

    double _x0, _x1, _axisY, _sMin, _sMax, _sSpan, _vMax, _vTop, _vBottom;

    const double PlotMargin = 40;

    /// <summary>Ось эпюры ставится по диапазону значений (у эпюры одного знака — у края холста);
    /// иначе — всегда посередине.</summary>
    public bool FitAxis { get; set; }

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
        MouseLeftButtonDown += OnMouseLeftButtonDown;
        MouseRightButtonDown += OnMouseRightButtonDown;
    }

    /// <summary>Задаёт данные эпюры и перерисовывает.</summary>
    /// <param name="secondary">Вторая эпюра на той же оси (наименьшие значения огибающей); null — её нет.</param>
    /// <param name="reference">Опорная линия без заливки (предел Кисп = 1); null — её нет.</param>
    public void SetData(IReadOnlyList<Segment> segments, string title, IReadOnlyList<Segment>? secondary = null,
                        IReadOnlyList<Segment>? reference = null)
    {
        _segments = segments ?? [];
        _secondary = secondary ?? [];
        _reference = reference ?? [];
        _title = title ?? "";
        Redraw();
    }

    /// <summary>Задаёт маркеры точек интегрирования и перерисовывает.</summary>
    public void SetMarkers(IReadOnlyList<Marker> markers)
    {
        _markers = markers ?? [];
        Redraw();
    }

    /// <summary>Выделяет маркер с заданным ключом (null — снять выделение).</summary>
    public void SelectMarker(object? key)
    {
        _selectedMarkerKey = key;
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

        const double margin = PlotMargin;
        double w = ActualWidth, h = ActualHeight;
        _x0 = margin; _x1 = w - margin;

        var all = _segments.Concat(_secondary).Concat(_reference).ToList();
        _sMax = all.Count > 0 ? all.Max(s => System.Math.Max(s.S0, s.S1)) : 0;
        _sMin = all.Count > 0 ? all.Min(s => System.Math.Min(s.S0, s.S1)) : 0;
        _sSpan = _sMax - _sMin;
        var values = all.SelectMany(s => new[] { s.V0, s.V1 }).ToList();
        _vMax = values.Count > 0 ? values.Max(System.Math.Abs) : 0;
        _vTop = FitAxis && values.Count > 0 ? System.Math.Max(0, values.Max()) : _vMax;
        _vBottom = FitAxis && values.Count > 0 ? System.Math.Min(0, values.Min()) : -_vMax;
        _axisY = _vMax > 1e-12 ? MapY(0) : h / 2;

        AddText(_title, _x0, 6, Brushes.Black, 13, true);

        if (_sSpan <= 1e-9 || _vMax <= 1e-12)
        {
            AddText(Loc.S("FemNoData"), _x0, _axisY - 8, Brushes.Gray, 12, false);
            DrawAxis(_x0, _x1, _axisY);
            DrawMarkers();
            EnsureHoverOverlays();
            return;
        }

        double MapX(double s) => _x0 + (s - _sMin) / _sSpan * (_x1 - _x0);

        void DrawSeries(IReadOnlyList<Segment> segments, Color color)
        {
            var fill = new SolidColorBrush(Color.FromArgb(90, color.R, color.G, color.B));
            var stroke = new SolidColorBrush(color);
            foreach (var seg in segments)
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
        }

        DrawSeries(_segments, Color.FromRgb(0x2b, 0x6c, 0xb0));
        DrawSeries(_secondary, Color.FromRgb(0xd9, 0x7a, 0x1e));
        foreach (var seg in _reference)
            Children.Add(new Line
            {
                X1 = MapX(seg.S0), Y1 = MapY(seg.V0), X2 = MapX(seg.S1), Y2 = MapY(seg.V1),
                Stroke = new SolidColorBrush(Color.FromRgb(0xc6, 0x28, 0x28)), StrokeThickness = 1.5,
                StrokeDashArray = [6, 4],
            });

        DrawAxis(_x0, _x1, _axisY);
        DrawMarkers();

        if (_vTop > 1e-12)
            AddText(_vTop.ToString("G4", CultureInfo.InvariantCulture), _x1 - 60, MapY(_vTop) - 16, Brushes.Black, 11, false);
        if (_vBottom < -1e-12)
            AddText(_vBottom.ToString("G4", CultureInfo.InvariantCulture), _x1 - 60, MapY(_vBottom) + 2, Brushes.Black, 11, false);

        EnsureHoverOverlays();
    }

    /// <summary>Экранная ордината значения: наибольшее — у верхнего поля, наименьшее — у нижнего.</summary>
    double MapY(double v) =>
        PlotMargin + (_vTop - v) / (_vTop - _vBottom) * (ActualHeight - 2 * PlotMargin);

    static bool TryValueAt(IReadOnlyList<Segment> segments, double s, out double value)
    {
        foreach (var seg in segments)
        {
            if (s < System.Math.Min(seg.S0, seg.S1) || s > System.Math.Max(seg.S0, seg.S1)) continue;
            double span = seg.S1 - seg.S0;
            value = System.Math.Abs(span) > 1e-9 ? seg.V0 + (s - seg.S0) / span * (seg.V1 - seg.V0) : seg.V0;
            return true;
        }
        value = 0;
        return false;
    }

    void DrawMarkers()
    {
        if (_markers.Count == 0 || _sSpan <= 1e-9) return;
        foreach (var m in _markers)
        {
            bool selected = Equals(_selectedMarkerKey, m.Key);
            var brush = selected ? Brushes.OrangeRed : m.Failed ? Brushes.Firebrick : m.Available ? Brushes.SeaGreen : Brushes.Gray;
            double size = selected ? 12 : 9;
            double mx = _x0 + (m.S - _sMin) / _sSpan * (_x1 - _x0);
            var ellipse = new Ellipse { Width = size, Height = size, Fill = brush };
            SetLeft(ellipse, mx - size / 2);
            SetTop(ellipse, _axisY - size / 2);
            Children.Add(ellipse);
        }
    }

    bool TryHitMarker(Point pos, out Marker marker)
    {
        foreach (var m in _markers)
        {
            if (_sSpan <= 1e-9) break;
            double mx = _x0 + (m.S - _sMin) / _sSpan * (_x1 - _x0);
            if (System.Math.Abs(pos.X - mx) <= 14 && System.Math.Abs(pos.Y - _axisY) <= 14)
            {
                marker = m;
                return true;
            }
        }
        marker = default;
        return false;
    }

    void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (TryHitMarker(e.GetPosition(this), out var marker))
        {
            _selectedMarkerKey = marker.Key;
            Redraw();
            MarkerClicked?.Invoke(marker.Key);
        }
    }

    void OnMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (TryHitMarker(e.GetPosition(this), out var marker))
        {
            _selectedMarkerKey = marker.Key;
            Redraw();
            MarkerContextMenuRequested?.Invoke(marker.Key);
        }
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
        var pos = e.GetPosition(this);
        if (TryHitMarker(pos, out var hovered))
        {
            _hoverLine.Visibility = Visibility.Collapsed;
            _hoverZLabel.Visibility = Visibility.Collapsed;
            _hoverVLabel.Text = hovered.Label;
            double mx = _x0 + (hovered.S - _sMin) / _sSpan * (_x1 - _x0);
            SetLeft(_hoverVLabel, mx + 8);
            SetTop(_hoverVLabel, _axisY - 26);
            _hoverVLabel.Visibility = Visibility.Visible;
            return;
        }
        if (_segments.Count == 0 || _sSpan <= 1e-9 || _vMax <= 1e-12) { HideHover(); return; }

        if (pos.X < _x0 || pos.X > _x1 || pos.Y < 0 || pos.Y > ActualHeight)
        { HideHover(); return; }

        double s = _sMin + (pos.X - _x0) / (_x1 - _x0) * _sSpan;
        s = System.Math.Max(_sMin, System.Math.Min(_sMax, s));

        bool found = TryValueAt(_segments, s, out double v);
        if (!found)
        {
            var closest = _segments.OrderBy(seg => System.Math.Min(System.Math.Abs(s - seg.S0), System.Math.Abs(s - seg.S1))).First();
            v = System.Math.Abs(s - closest.S0) < System.Math.Abs(s - closest.S1) ? closest.V0 : closest.V1;
            s = System.Math.Abs(s - closest.S0) < System.Math.Abs(s - closest.S1) ? closest.S0 : closest.S1;
        }

        double xLine = _x0 + (s - _sMin) / _sSpan * (_x1 - _x0);
        double yLine = MapY(v);

        _hoverLine.X1 = xLine; _hoverLine.X2 = xLine;
        _hoverLine.Y1 = 0; _hoverLine.Y2 = ActualHeight;
        _hoverLine.Visibility = Visibility.Visible;

        _hoverZLabel.Text = s.ToString("G3", CultureInfo.InvariantCulture);
        SetLeft(_hoverZLabel, xLine - 15);
        SetTop(_hoverZLabel, ActualHeight - 16);
        _hoverZLabel.Visibility = Visibility.Visible;

        _hoverVLabel.Text = v.ToString("G4", CultureInfo.InvariantCulture)
            + (TryValueAt(_secondary, s, out double v2) ? " / " + v2.ToString("G4", CultureInfo.InvariantCulture) : "");
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
