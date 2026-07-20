using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace OpenCS.Views.Helpers;

/// <summary>2D-эпюра выбранной компоненты усилия вдоль одного конструктивного стержня.</summary>
public sealed class FemMemberForceCanvas : Canvas
{
    /// <summary>Сегмент эпюры: дуговые координаты концов и значения усилия.</summary>
    public readonly record struct Segment(double S0, double S1, double V0, double V1);

    IReadOnlyList<Segment> _segments = [];
    string _title = "";

    public FemMemberForceCanvas()
    {
        Background = Brushes.White;
        SizeChanged += (_, _) => Redraw();
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
        Children.Clear();
        if (ActualWidth < 20 || ActualHeight < 20) return;

        const double margin = 40;
        double w = ActualWidth, h = ActualHeight;
        double x0 = margin, x1 = w - margin;
        double axisY = h / 2;

        double sMax = _segments.Count > 0 ? _segments.Max(s => s.S1) : 0;
        double sMin = _segments.Count > 0 ? _segments.Min(s => s.S0) : 0;
        double sSpan = sMax - sMin;
        double vMax = _segments.Count > 0
            ? _segments.SelectMany(s => new[] { System.Math.Abs(s.V0), System.Math.Abs(s.V1) }).DefaultIfEmpty(0).Max()
            : 0;

        // Заголовок
        AddText(_title, x0, 6, Brushes.Black, 13, true);

        if (sSpan <= 1e-9 || vMax <= 1e-12)
        {
            AddText("нет данных", x0, axisY - 8, Brushes.Gray, 12, false);
            DrawAxis(x0, x1, axisY);
            return;
        }

        double MapX(double s) => x0 + (s - sMin) / sSpan * (x1 - x0);
        double MapY(double v) => axisY - v / vMax * (h / 2 - margin);

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
                    new Point(MapX(seg.S0), axisY),
                    new Point(MapX(seg.S0), MapY(seg.V0)),
                    new Point(MapX(seg.S1), MapY(seg.V1)),
                    new Point(MapX(seg.S1), axisY),
                ]
            };
            Children.Add(poly);
        }

        DrawAxis(x0, x1, axisY);

        // Подписи экстремумов
        AddText(vMax.ToString("G4", CultureInfo.InvariantCulture), x1 - 60, MapY(vMax) - 16, Brushes.Black, 11, false);
        AddText((-vMax).ToString("G4", CultureInfo.InvariantCulture), x1 - 60, MapY(-vMax) + 2, Brushes.Black, 11, false);
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
