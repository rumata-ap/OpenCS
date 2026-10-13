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
        return Resolve(new FieldStripRebarSource(field), analogy, stationFractions, widthPointCount);
    }

    /// <summary>То же для произвольного источника армирования (раскладка OpenCS, КЭ импортированной схемы).
    /// Оси полосы и армирования проверяются <see cref="StripRebarAxisGuard"/>: несогласованные — ошибка.</summary>
    public static PlateStripFiberLayouts Resolve(
        IStripRebarSource source,
        PlateStripBeamAnalogy analogy,
        IReadOnlyList<double> stationFractions,
        int widthPointCount)
    {
        ArgumentNullException.ThrowIfNull(source);
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
        bool spatial = source.IsSpatial;
        var guardSeen = new HashSet<string>();
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
                        // Точка в отверстии: армирование берётся у ближайшей кромки материала.
                        var uv = PlateStripWidthSampler.Point(
                            analogy, mid, StripMaterialProfile.SnapToMaterial(analogy, mid, v));
                        (u, vv) = (uv.U, uv.V);
                    }
                    var sample = source.Resolve(u, vv);
                    diagnostics.AddRange(sample.Diagnostics);
                    foreach (var d in StripRebarAxisGuard.Check(analogy, sample))
                        if (guardSeen.Add(d.Code + d.Message)) diagnostics.Add(d);
                    points.Add(sample.Layers);
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
