using CScore;
using OpenCS.ViewModels;
using System;

namespace OpenCS.Services;

/// <summary>Параметры векторного экспорта эпюры разреза (SVG/DXF).</summary>
public sealed class SectionCutExportArgs
{
    public required SectionCutResult Result { get; init; }
    public required SectionPlotMode Mode { get; init; }
    public bool Horizontal { get; init; }
    public bool AsOnScreen { get; init; }
    public bool FillMode { get; init; }
    public bool HatchMode { get; init; }
    public bool ShowRebarForce { get; init; }
    public double? EpsCu { get; init; }
    public Func<int?, MatType>? GetAreaMatType { get; init; }
    public Func<CutRebarMarker, double>? ResolveRebarLengthPx { get; init; }
    /// <summary>Снимок вида с экрана; если null — экспортёр строит упрощённый fit сам.</summary>
    public SectionCutViewTransform? View { get; init; }

    public const double DefaultRebarLinePx = 18.0;

    public double RebarLengthPx(CutRebarMarker r) =>
        ResolveRebarLengthPx?.Invoke(r) ?? DefaultRebarLinePx;

    public double RebarDisplayValue(CutRebarMarker r) =>
        ShowRebarForce
            ? r.ForceKN
            : Mode == SectionPlotMode.Stress ? r.Sig : r.Eps;

    public string FormatValue(double v) => Mode == SectionPlotMode.Stress
        ? v.ToString("+0.##;-0.##", System.Globalization.CultureInfo.InvariantCulture)
        : v.ToString("+0.#####;-0.#####", System.Globalization.CultureInfo.InvariantCulture);

    public string FormatRebarLabel(double v) => ShowRebarForce
        ? $"N={FormatValue(v)}"
        : Mode == SectionPlotMode.Stress ? $"σ={FormatValue(v)}" : $"ε={FormatValue(v)}";

    public MatType AreaMatType(int? areaIndex) =>
        GetAreaMatType?.Invoke(areaIndex) ?? MatType.Concrete;
}
