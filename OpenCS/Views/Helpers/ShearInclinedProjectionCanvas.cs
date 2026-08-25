using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CScore.Sp63Shear;

namespace OpenCS.Views.Helpers;

/// <summary>
/// Диаграмма несущей способности наклонного сечения по длине проекции C:
/// Qb убывает, Qsw растёт, их сумма имеет минимум — он и определяет критическое C.
/// </summary>
public sealed class ShearInclinedProjectionCanvas : Canvas
{
    IReadOnlyList<ProjectionPoint> _curve = [];
    double _criticalC;

    /// <summary>Точки кривой по проекции.</summary>
    public IReadOnlyList<ProjectionPoint> Curve
    {
        get => _curve;
        set { _curve = value ?? []; InvalidateVisual(); }
    }

    /// <summary>Критическая длина проекции, м — отмечается вертикальной линией.</summary>
    public double CriticalC
    {
        get => _criticalC;
        set { _criticalC = value; InvalidateVisual(); }
    }

    /// <summary>Рисует кривые Qb, Qsw, их сумму и действующую поперечную силу.</summary>
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (_curve.Count < 2 || ActualWidth <= 20.0 || ActualHeight <= 20.0) return;

        const double left = 52.0, right = 12.0, top = 12.0, bottom = 24.0;
        double plotWidth = ActualWidth - left - right;
        double plotHeight = ActualHeight - top - bottom;
        if (plotWidth <= 0.0 || plotHeight <= 0.0) return;

        double minC = _curve.Min(p => p.C);
        double maxC = _curve.Max(p => p.C);
        if (maxC - minC < 1e-9) return;

        double maxValue = _curve.Max(p => Math.Max(p.QSum, p.Q)) * 1.1;
        if (maxValue <= 0.0) return;

        double PxX(double c) => left + plotWidth * (c - minC) / (maxC - minC);
        double PxY(double value) => top + plotHeight * (1.0 - Math.Min(value, maxValue) / maxValue);

        var axisPen = new Pen(Brushes.Gray, 1.0);
        dc.DrawLine(axisPen, new Point(left, top), new Point(left, top + plotHeight));
        dc.DrawLine(axisPen, new Point(left, top + plotHeight),
                             new Point(left + plotWidth, top + plotHeight));

        DrawLabel(dc, maxValue.ToString("F0", CultureInfo.CurrentCulture), 4.0, top - 2.0);
        DrawLabel(dc, minC.ToString("F2", CultureInfo.CurrentCulture), left, top + plotHeight + 4.0);
        DrawLabel(dc, maxC.ToString("F2", CultureInfo.CurrentCulture),
                  left + plotWidth - 26.0, top + plotHeight + 4.0);

        DrawSeries(dc, new Pen(Brushes.SteelBlue, 2.0), p => p.Qb, PxX, PxY);
        DrawSeries(dc, new Pen(Brushes.SeaGreen, 2.0), p => p.Qsw, PxX, PxY);
        DrawSeries(dc, new Pen(Brushes.DarkSlateBlue, 2.5), p => p.QSum, PxX, PxY);
        DrawSeries(dc, new Pen(Brushes.Red, 1.5) { DashStyle = DashStyles.Dash }, p => p.Q, PxX, PxY);

        if (_criticalC >= minC && _criticalC <= maxC)
        {
            double x = PxX(_criticalC);
            dc.DrawLine(new Pen(Brushes.DarkRed, 1.0) { DashStyle = DashStyles.Dot },
                new Point(x, top), new Point(x, top + plotHeight));
            DrawLabel(dc, $"C = {_criticalC:F3} м", x + 2.0, top);
        }
    }

    /// <summary>Рисует одну кривую по выбранной величине точки.</summary>
    void DrawSeries(
        DrawingContext dc, Pen pen, Func<ProjectionPoint, double> selector,
        Func<double, double> pxX, Func<double, double> pxY)
    {
        Point? previous = null;
        foreach (var point in _curve.OrderBy(p => p.C))
        {
            double value = selector(point);
            if (!double.IsFinite(value)) { previous = null; continue; }

            var current = new Point(pxX(point.C), pxY(value));
            if (previous is Point from) dc.DrawLine(pen, from, current);
            previous = current;
        }
    }

    /// <summary>Рисует подпись оси.</summary>
    static void DrawLabel(DrawingContext dc, string text, double x, double y)
    {
        var formatted = new FormattedText(
            text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), 10.0, Brushes.Gray, 96.0);
        dc.DrawText(formatted, new Point(x, y));
    }
}
