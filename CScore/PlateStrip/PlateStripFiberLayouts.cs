using CScore.Fem;
using CScore.PlateRebar;

namespace CScore.PlateStrip;

/// <summary>Раскладка армирования полосы для фибровой сверки (Срез 8c): по элементам и точкам
/// квадратуры по ширине. Уровни: [элемент][точка ширины] → слои арматуры.</summary>
public sealed record PlateStripFiberLayouts(
    IReadOnlyList<IReadOnlyList<IReadOnlyList<PlateRebarLayer>>> PerElement,
    IReadOnlyList<FemValidationDiagnostic> Diagnostics)
{
    public bool IsCalculable => !Diagnostics.Any(d => d.IsError);

    /// <summary>Разрешить армирование в серединах элементов полосы и точках Гаусса по ширине
    /// через <see cref="PlateRebarFieldResolver"/> (тот же резолвер, что у источников ширины).</summary>
    public static PlateStripFiberLayouts Resolve(
        PlateRebarField field,
        PlateStripBeamAnalogy analogy,
        IReadOnlyList<double> stationFractions,
        int widthPointCount)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(analogy);
        ArgumentNullException.ThrowIfNull(stationFractions);
        if (stationFractions.Count < 2)
            throw new ArgumentException("Нужно не менее двух станций.", nameof(stationFractions));
        if (widthPointCount < 1)
            throw new ArgumentOutOfRangeException(nameof(widthPointCount));

        var diagnostics = new List<FemValidationDiagnostic>();
        var (vs, _) = EquivalentSectionCalculator.WidthGaussPoints(analogy.ExplicitWidthM, widthPointCount);
        var perElement = new List<IReadOnlyList<IReadOnlyList<PlateRebarLayer>>>();

        // Без зон армирование однородно: пространственный резолв (и геометрия границ полосы) не нужны.
        bool spatial = field.Zones.Count > 0;
        try
        {
            for (int e = 0; e < stationFractions.Count - 1; e++)
            {
                double mid = 0.5 * (stationFractions[e] + stationFractions[e + 1]);
                var points = new List<IReadOnlyList<PlateRebarLayer>>();
                foreach (double v in vs)
                {
                    double u = 0.0, vv = 0.0;
                    if (spatial)
                    {
                        var uv = PlateStripWidthSampler.Point(analogy, mid, v);
                        (u, vv) = (uv.U, uv.V);
                    }
                    var resolved = PlateRebarFieldResolver.Resolve(field, u, vv);
                    foreach (var d in resolved.Diagnostics)
                        diagnostics.Add(d.Code == "plate_rebar_zone_priority_conflict"
                            ? new(d.Code, d.Message, false) : d);
                    points.Add(resolved.Layers);
                }
                perElement.Add(points);
            }
        }
        catch (ArgumentException ex)
        {
            diagnostics.Add(new("plate_strip_fiber_layout_sampling_failed",
                $"Не удалось разрешить армирование вдоль полосы: {ex.Message}"));
            return new([], diagnostics);
        }

        return new(perElement, diagnostics);
    }
}
