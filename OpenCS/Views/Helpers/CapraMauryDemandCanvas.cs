using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace OpenCS.Views.Helpers;

/// <summary>Точка графика подбора Капри-Мори: фасетка θ, требование r(θ) и обеспечиваемая As,n(θ), см²/м.</summary>
public readonly record struct CapraMauryDemandPoint(double ThetaDeg, double Required, double Provided, bool Active);

/// <summary>
/// График подбора сетки одной грани по фасеткам θ ∈ [0°, 180°]: требование r(θ) (точки фасеток,
/// ломаная) и обеспечиваемая сеткой As,n(θ) = Asx·cos²θ + Asy·sin²θ (гладкая кривая). Управляющие
/// фасетки выделены.
/// </summary>
public sealed class CapraMauryDemandCanvas : Canvas
{
    IReadOnlyList<CapraMauryDemandPoint> _points = [];
    double _asx, _asy;

    /// <summary>Фасетки и подобранная сетка (см²/м).</summary>
    public void SetData(IReadOnlyList<CapraMauryDemandPoint> points, double asx, double asy)
    {
        _points = points ?? [];
        _asx = asx; _asy = asy;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (_points.Count == 0 || ActualWidth <= 40.0 || ActualHeight <= 30.0) return;

        const double left = 44.0, right = 10.0, top = 10.0, bottom = 22.0;
        double w = ActualWidth - left - right, h = ActualHeight - top - bottom;
        if (w <= 0.0 || h <= 0.0) return;

        double max = Math.Max(_asx, _asy);
        foreach (var p in _points) max = Math.Max(max, Math.Max(p.Required, p.Provided));
        if (max < 1e-9) max = 1.0;
        max *= 1.1;

        double X(double theta) => left + w * theta / 180.0;
        double Y(double v) => top + h * (1.0 - Math.Max(0.0, v) / max);

        var axis = new Pen(Brushes.Gray, 1.0);
        var grid = new Pen(new SolidColorBrush(Color.FromArgb(40, 128, 128, 128)), 1.0);
        dc.DrawLine(axis, new Point(left, top), new Point(left, top + h));
        dc.DrawLine(axis, new Point(left, top + h), new Point(left + w, top + h));
        for (int deg = 0; deg <= 180; deg += 45)
        {
            dc.DrawLine(grid, new Point(X(deg), top), new Point(X(deg), top + h));
            Label(dc, deg + "°", X(deg) - 8.0, top + h + 3.0);
        }
        Label(dc, (max / 1.1).ToString("F2", CultureInfo.CurrentCulture), 2.0, Y(max / 1.1) - 7.0);
        Label(dc, "0", left - 12.0, top + h - 7.0);

        // Обеспечиваемая сеткой As,n(θ) — гладкая кривая.
        var provPen = new Pen(Brushes.DarkOrange, 2.0);
        Point? prev = null;
        for (int i = 0; i <= 180; i++)
        {
            double a = i * Math.PI / 180.0, c = Math.Cos(a), s = Math.Sin(a);
            var pt = new Point(X(i), Y(_asx * c * c + _asy * s * s));
            if (prev is Point from) dc.DrawLine(provPen, from, pt);
            prev = pt;
        }

        // Требование r(θ) — ломаная по фасеткам, с замыканием на 180° (θ и θ + 180° — одна фасетка).
        var reqPen = new Pen(Brushes.SteelBlue, 1.5);
        var ordered = _points.OrderBy(p => p.ThetaDeg).ToList();
        prev = null;
        foreach (var p in ordered.Append(ordered[0] with { ThetaDeg = 180.0 }))
        {
            var pt = new Point(X(p.ThetaDeg), Y(p.Required));
            if (prev is Point from) dc.DrawLine(reqPen, from, pt);
            prev = pt;
        }
        foreach (var p in ordered)
        {
            var pt = new Point(X(p.ThetaDeg), Y(p.Required));
            if (p.Active)
                dc.DrawEllipse(Brushes.Red, null, pt, 4.0, 4.0);
            else
                dc.DrawEllipse(Brushes.SteelBlue, null, pt, 2.5, 2.5);
        }
    }

    static void Label(DrawingContext dc, string text, double x, double y)
    {
        var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), 10.0, Brushes.Gray, 96.0);
        dc.DrawText(ft, new Point(x, y));
    }
}
