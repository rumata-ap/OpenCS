using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OpenCS.ViewModels;

namespace OpenCS.Views.Helpers;

/// <summary>
/// График коэффициента использования вдоль элемента: η по поперечной силе и η по моменту.
/// Стоянки, для которых проверка не выполнялась (NaN), в линии не участвуют — линия
/// на этих участках прерывается, чтобы не рисовать несуществующие значения.
/// </summary>
public sealed class ShearInclinedEtaCanvas : Canvas
{
    IReadOnlyList<ShearInclinedStationVM> _stations = [];

    /// <summary>Стоянки, отображаемые на графике.</summary>
    public IReadOnlyList<ShearInclinedStationVM> Stations
    {
        get => _stations;
        set { _stations = value ?? []; InvalidateVisual(); }
    }

    /// <summary>Рисует оси, предельную линию η = 1 и кривые η(s).</summary>
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (_stations.Count < 2 || ActualWidth <= 20.0 || ActualHeight <= 20.0) return;

        const double left = 44.0, right = 12.0, top = 10.0, bottom = 22.0;
        double plotWidth = ActualWidth - left - right;
        double plotHeight = ActualHeight - top - bottom;
        if (plotWidth <= 0.0 || plotHeight <= 0.0) return;

        double minS = _stations.Min(s => s.S);
        double maxS = _stations.Max(s => s.S);
        if (maxS - minS < 1e-9) return;

        double maxEta = 1.2;
        foreach (var station in _stations)
        {
            if (double.IsFinite(station.Eta)) maxEta = Math.Max(maxEta, station.Eta);
            if (double.IsFinite(station.EtaM)) maxEta = Math.Max(maxEta, station.EtaM);
        }

        double PxX(double s) => left + plotWidth * (s - minS) / (maxS - minS);
        double PxY(double eta) => top + plotHeight * (1.0 - Math.Min(eta, maxEta) / maxEta);

        var axisPen = new Pen(Brushes.Gray, 1.0);
        dc.DrawLine(axisPen, new Point(left, top), new Point(left, top + plotHeight));
        dc.DrawLine(axisPen, new Point(left, top + plotHeight),
                             new Point(left + plotWidth, top + plotHeight));

        // Предельная линия η = 1
        var limitPen = new Pen(Brushes.Red, 1.0) { DashStyle = DashStyles.Dash };
        double limitY = PxY(1.0);
        dc.DrawLine(limitPen, new Point(left, limitY), new Point(left + plotWidth, limitY));
        DrawLabel(dc, "1.0", left - 40.0, limitY - 8.0);
        DrawLabel(dc, maxEta.ToString("F2", CultureInfo.CurrentCulture), left - 40.0, top - 2.0);
        DrawLabel(dc, minS.ToString("F2", CultureInfo.CurrentCulture), left, top + plotHeight + 2.0);
        DrawLabel(dc, maxS.ToString("F2", CultureInfo.CurrentCulture),
                  left + plotWidth - 24.0, top + plotHeight + 2.0);

        DrawSeries(dc, new Pen(Brushes.SteelBlue, 2.0), station => station.Eta, PxX, PxY);
        DrawSeries(dc, new Pen(Brushes.DarkOrange, 2.0), station => station.EtaM, PxX, PxY);
    }

    /// <summary>Рисует одну кривую, разрывая её на невыполнявшихся проверках.</summary>
    void DrawSeries(
        DrawingContext dc, Pen pen, Func<ShearInclinedStationVM, double> selector,
        Func<double, double> pxX, Func<double, double> pxY)
    {
        Point? previous = null;
        foreach (var station in _stations.OrderBy(s => s.S))
        {
            double value = selector(station);
            if (!double.IsFinite(value)) { previous = null; continue; }

            var point = new Point(pxX(station.S), pxY(value));
            if (previous is Point from) dc.DrawLine(pen, from, point);
            previous = point;
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
