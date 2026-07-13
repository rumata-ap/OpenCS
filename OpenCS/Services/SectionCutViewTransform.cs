using System;

namespace OpenCS.Services;

/// <summary>
/// Снимок видового преобразования эпюры разреза (как на экране: размер, масштаб, pan).
/// Координаты — экранные (Y вниз), как в WPF.
/// </summary>
public sealed class SectionCutViewTransform
{
    public double CanvasWidth { get; init; }
    public double CanvasHeight { get; init; }
    public double PlotOx { get; init; }
    public double PlotOy { get; init; }
    public double PlotW { get; init; }
    public double PlotH { get; init; }
    public double PanX { get; init; }
    public double PanY { get; init; }
    public double ScaleS { get; init; }
    public double ScaleV { get; init; }
    public double LengthMm { get; init; }
    public double FitVMin { get; init; }
    public double FitVMax { get; init; }
    public bool Horizontal { get; init; }

    const double SOriginFrac = 0.0;

    double BaseScreenS(double sMm)
    {
        double origin = Horizontal ? PlotOx : PlotOy;
        return origin + LengthMm * SOriginFrac * ScaleS + sMm * ScaleS;
    }

    double BaseScreenV0() => Horizontal ? PlotOy + PlotH / 2 : PlotOx + PlotW / 2;

    public (double X, double Y) ToScreen(double sMm, double value) => Horizontal
        ? (BaseScreenS(sMm) + PanX, BaseScreenV0() + PanY - value * ScaleV)
        : (BaseScreenV0() + PanX + value * ScaleV, BaseScreenS(sMm) + PanY);

    public double ScreenToS(double screenCoord) => Horizontal
        ? (screenCoord - PanX - PlotOx) / Math.Max(ScaleS, 1e-12)
        : (screenCoord - PanY - PlotOy) / Math.Max(ScaleS, 1e-12);

    public double ScreenToV(double screenCoord) => Horizontal
        ? (BaseScreenV0() + PanY - screenCoord) / Math.Max(ScaleV, 1e-12)
        : (screenCoord - BaseScreenV0() - PanX) / Math.Max(ScaleV, 1e-12);

    /// <summary>DXF: Y вверх, начало в левом нижнем углу холста.</summary>
    public (double X, double Y) ToDxf(double sMm, double value)
    {
        var (x, y) = ToScreen(sMm, value);
        return (x, CanvasHeight - y);
    }

    public (double X, double Y) ScreenToDxf(double screenX, double screenY) =>
        (screenX, CanvasHeight - screenY);
}
