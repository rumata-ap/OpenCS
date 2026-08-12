using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace OpenCS.Views.Helpers;

/// <summary>Универсальный график «Y по X» с сегментацией и клик-выбором ближайшей точки.
/// Используется и для λ–шаг (BuildLoadFactorCanvas — все точки SegmentId=0), и для λ–u
/// (ControlDisplacementPointsBuilder — SegmentId меняется на границе стадий/режимов, X=NaN
/// на шагах, где узел/DOF не определён). X=NaN — точка не рисуется (разрыв), но её позиция
/// в списке остаётся валидным индексом для StepClicked (соответствует Steps в VM).</summary>
public sealed class FemLoadFactorCanvas : Canvas
{
    IReadOnlyList<(double X, double Y, bool Converged, int SegmentId)> _points = [];
    int _selectedIndex;

    public FemLoadFactorCanvas()
    {
        Background = Brushes.White;
        SizeChanged += (_, _) => Redraw();
        MouseLeftButtonDown += OnClick;
    }

    /// <summary>Подпись оси X (например, «Шаг» или «Перемещение, м/рад») — рисуется под осью,
    /// по центру. Пустая строка/null — подпись не рисуется. DependencyProperty — нужно для
    /// биндинга через DynamicResource (переключение языка UI вживую).</summary>
    public static readonly DependencyProperty XAxisLabelProperty = DependencyProperty.Register(
        nameof(XAxisLabel), typeof(string), typeof(FemLoadFactorCanvas),
        new PropertyMetadata(null, (d, _) => ((FemLoadFactorCanvas)d).Redraw()));

    public string? XAxisLabel
    {
        get => (string?)GetValue(XAxisLabelProperty);
        set => SetValue(XAxisLabelProperty, value);
    }

    /// <summary>Подпись оси Y (например, «λ, коэфф. нагрузки») — рисуется в верхнем левом углу
    /// холста. Пустая строка/null — подпись не рисуется.</summary>
    public static readonly DependencyProperty YAxisLabelProperty = DependencyProperty.Register(
        nameof(YAxisLabel), typeof(string), typeof(FemLoadFactorCanvas),
        new PropertyMetadata(null, (d, _) => ((FemLoadFactorCanvas)d).Redraw()));

    public string? YAxisLabel
    {
        get => (string?)GetValue(YAxisLabelProperty);
        set => SetValue(YAxisLabelProperty, value);
    }

    /// <summary>Поднимается при клике по точке графика — позиционный индекс в списке,
    /// переданном в SetData (совпадает со Steps в вызывающей VM).</summary>
    public event Action<int>? StepClicked;

    public void SetData(IReadOnlyList<(double X, double Y, bool Converged, int SegmentId)> points, int selectedIndex)
    {
        _points = points ?? [];
        _selectedIndex = selectedIndex;
        Redraw();
    }

    const double AxisMargin = 40;

    /// <summary>Компактное представление значения деления оси — 3 значащих цифры, при малых
    /// величинах (например, шаг DisplacementControl ~1e-5 м) автоматически переходит на
    /// экспоненциальную запись, чтобы не показывать «0».</summary>
    static string FormatAxisValue(double v) => v.ToString("G3", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Группирует индексы точек с конечными координатами в последовательные
    /// пробеги одинакового SegmentId — каждый пробег рисуется отдельным Polyline (переход
    /// к новому SegmentId НЕ соединяется линией с предыдущим). Точки с NaN/Infinity X или Y
    /// исключены из всех групп (не рисуются вовсе, но их позиция в исходном списке не
    /// меняется — вызывающий Redraw() адресует точки по позиции для маркеров).</summary>
    public static IReadOnlyList<IReadOnlyList<int>> GroupBySegment(IReadOnlyList<(double X, double Y, bool Converged, int SegmentId)> points)
    {
        var groups = new List<List<int>>();
        List<int>? current = null;
        int? currentSegmentId = null;
        for (int i = 0; i < points.Count; i++)
        {
            var p = points[i];
            if (!double.IsFinite(p.X) || !double.IsFinite(p.Y)) { current = null; currentSegmentId = null; continue; }
            if (current == null || currentSegmentId != p.SegmentId)
            {
                current = [];
                groups.Add(current);
                currentSegmentId = p.SegmentId;
            }
            current.Add(i);
        }
        return groups;
    }

    void Redraw()
    {
        Children.Clear();
        if (ActualWidth < 20 || ActualHeight < 20 || _points.Count == 0) return;

        var finite = _points.Where(p => double.IsFinite(p.X) && double.IsFinite(p.Y)).ToList();
        if (finite.Count == 0) return;

        double x0 = AxisMargin, x1 = ActualWidth - AxisMargin, y0 = ActualHeight - AxisMargin, y1 = AxisMargin;
        double minX = finite.Min(p => p.X), maxX = finite.Max(p => p.X);
        double minY = Math.Min(0, finite.Min(p => p.Y)), maxY = Math.Max(1, finite.Max(p => p.Y));
        if (maxX - minX < 1e-12) { minX -= 0.5; maxX += 0.5; }
        if (maxY - minY < 1e-12) { maxY += 1; }

        double MapX(double x) => x0 + (x - minX) / (maxX - minX) * (x1 - x0);
        double MapY(double y) => y0 - (y - minY) / (maxY - minY) * (y0 - y1);

        Children.Add(new Line { X1 = x0, Y1 = y0, X2 = x1, Y2 = y0, Stroke = Brushes.Black, StrokeThickness = 1 });
        Children.Add(new Line { X1 = x0, Y1 = y0, X2 = x0, Y2 = y1, Stroke = Brushes.Black, StrokeThickness = 1 });

        void AddText(string text, double left, double top, double fontSize, Brush brush, bool rightAlign = false)
        {
            var tb = new TextBlock { Text = text, FontSize = fontSize, Foreground = brush };
            if (rightAlign)
            {
                tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                left -= tb.DesiredSize.Width;
            }
            SetLeft(tb, Math.Max(0, left));
            SetTop(tb, top);
            Children.Add(tb);
        }

        // значения на концах осей
        AddText(FormatAxisValue(minX), x0, y0 + 2, 9, Brushes.Gray);
        AddText(FormatAxisValue(maxX), x1, y0 + 2, 9, Brushes.Gray, rightAlign: true);
        AddText(FormatAxisValue(maxY), 2, y1 - 2, 9, Brushes.Gray);
        AddText(FormatAxisValue(minY), 2, y0 - 12, 9, Brushes.Gray);

        // подписи осей — центр X снизу, верхний левый угол для Y
        if (!string.IsNullOrEmpty(XAxisLabel))
        {
            var xLabel = new TextBlock { Text = XAxisLabel, FontSize = 10, Foreground = Brushes.DimGray };
            xLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            SetLeft(xLabel, Math.Max(0, (x0 + x1) / 2 - xLabel.DesiredSize.Width / 2));
            SetTop(xLabel, ActualHeight - AxisMargin + 16);
            Children.Add(xLabel);
        }
        if (!string.IsNullOrEmpty(YAxisLabel))
        {
            var yLabel = new TextBlock { Text = YAxisLabel, FontSize = 10, Foreground = Brushes.DimGray };
            SetLeft(yLabel, 2);
            SetTop(yLabel, 2);
            Children.Add(yLabel);
        }

        foreach (var group in GroupBySegment(_points))
        {
            if (group.Count < 2) continue;
            var poly = new Polyline { Stroke = Brushes.SteelBlue, StrokeThickness = 1.5 };
            foreach (var idx in group)
                poly.Points.Add(new Point(MapX(_points[idx].X), MapY(_points[idx].Y)));
            Children.Add(poly);
        }

        for (int i = 0; i < _points.Count; i++)
        {
            var p = _points[i];
            if (!double.IsFinite(p.X) || !double.IsFinite(p.Y)) continue;
            var dot = new Ellipse
            {
                Width = 8, Height = 8,
                Fill = p.Converged ? Brushes.SeaGreen : Brushes.Crimson,
                Stroke = i == _selectedIndex ? Brushes.Black : null,
                StrokeThickness = 2,
            };
            SetLeft(dot, MapX(p.X) - 4);
            SetTop(dot, MapY(p.Y) - 4);
            Children.Add(dot);
        }
    }

    void OnClick(object sender, MouseButtonEventArgs e)
    {
        if (_points.Count == 0) return;
        var pos = e.GetPosition(this);
        double x0 = AxisMargin, x1 = ActualWidth - AxisMargin, y0 = ActualHeight - AxisMargin, y1 = AxisMargin;
        var finite = _points.Where(p => double.IsFinite(p.X) && double.IsFinite(p.Y)).ToList();
        if (finite.Count == 0) return;
        double minX = finite.Min(p => p.X), maxX = finite.Max(p => p.X);
        double minY = Math.Min(0, finite.Min(p => p.Y)), maxY = Math.Max(1, finite.Max(p => p.Y));
        if (maxX - minX < 1e-12) { minX -= 0.5; maxX += 0.5; }
        if (maxY - minY < 1e-12) { maxY += 1; }
        double MapX(double x) => x0 + (x - minX) / (maxX - minX) * (x1 - x0);
        double MapY(double y) => y0 - (y - minY) / (maxY - minY) * (y0 - y1);

        int best = -1; double bestDist = double.MaxValue;
        for (int i = 0; i < _points.Count; i++)
        {
            var p = _points[i];
            if (!double.IsFinite(p.X) || !double.IsFinite(p.Y)) continue;
            double dx = MapX(p.X) - pos.X, dy = MapY(p.Y) - pos.Y;
            double dist = dx * dx + dy * dy;
            if (dist < bestDist) { bestDist = dist; best = i; }
        }
        if (best >= 0) StepClicked?.Invoke(best);
    }
}
