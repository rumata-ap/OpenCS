using CScore.Fem;
using CScore.PlateRebar;

namespace CScore.PlateStrip;

/// <summary>Армирование плиты в точке полосы: слои, диагностики и направление оси армирования.</summary>
/// <param name="Layers">Слои армирования в осях x/y сечения (оси армирования).</param>
/// <param name="Diagnostics">Диагностики разрешения армирования в точке.</param>
/// <param name="RebarAxisDeg">Угол оси x армирования в плоскости региона (оси U,V региона), градусы,
/// против часовой стрелки. У раскладки OpenCS — 0 (оси раскладки — оси региона).</param>
public sealed record StripRebarSample(
    IReadOnlyList<PlateRebarLayer> Layers,
    IReadOnlyList<FemValidationDiagnostic> Diagnostics,
    double RebarAxisDeg = 0.0)
{
    /// <summary>Толщина плиты в точке, м, если источник её знает (КЭ сетки); 0 — не задана.</summary>
    public double ThicknessM { get; init; }
}

/// <summary>
/// Источник армирования плиты вдоль полосы: по точке (u, v) в плоскости региона даёт слои армирования.
/// Раскладка OpenCS (фон + зоны региона) и армирование КЭ импортированных схем (заданное/подобранное
/// ЛИРЫ и SCAD) отвечают одним интерфейсом — расчёт полосы не зависит от программы-источника схемы.
/// </summary>
public interface IStripRebarSource
{
    /// <summary>Ключ источника (<see cref="FemCheckRebarSource"/>).</summary>
    string Key { get; }

    /// <summary>Армирование зависит от точки: без этого координаты точек полосы не вычисляются.</summary>
    bool IsSpatial { get; }

    /// <summary>Армирование в точке (u, v) плоскости региона.</summary>
    StripRebarSample Resolve(double u, double v);
}

/// <summary>Раскладка OpenCS: фоновые слои сечения плюс зоны региона (<see cref="PlateRebarField"/>).</summary>
public sealed class FieldStripRebarSource(PlateRebarField rebarField) : IStripRebarSource
{
    /// <summary>Поле армирования.</summary>
    public PlateRebarField Field => rebarField;

    /// <inheritdoc/>
    public string Key => FemCheckRebarSource.Layout;

    /// <inheritdoc/>
    public bool IsSpatial => rebarField.Zones.Count > 0;

    /// <inheritdoc/>
    public StripRebarSample Resolve(double u, double v)
    {
        var resolved = PlateRebarFieldResolver.Resolve(rebarField, u, v);
        // Конфликт приоритетов зон — предупреждение: группа приоритета пропущена, расчёт возможен.
        var diagnostics = resolved.Diagnostics
            .Select(d => d.Code == "plate_rebar_zone_priority_conflict"
                ? new FemValidationDiagnostic(d.Code, d.Message, false)
                : d)
            .ToList();
        return new(resolved.Layers, diagnostics, 0.0);
    }
}

/// <summary>
/// Проверка осей: полоса работает одноосно вдоль своей оси, а расчёт сечения берёт армирование вдоль
/// оси x плиты (Asx) и не поворачивает жёсткость на угол оси полосы. Поэтому ось полосы должна лежать
/// вдоль оси x армирования; иначе результат был бы молча неверным — расчёт блокируется диагностикой.
/// </summary>
public static class StripRebarAxisGuard
{
    /// <summary>|sin| угла между осью полосы и осью армирования, ниже которого оси считаются совпадающими
    /// (как у <see cref="PlateLayoutResolver"/>).</summary>
    public const double AlignedSinTolerance = 1e-4;

    /// <summary>Угол оси полосы в плоскости региона, градусы (против часовой стрелки от оси U).</summary>
    public static double StripAxisDeg(PlateStripBeamAnalogy analogy)
    {
        ArgumentNullException.ThrowIfNull(analogy);
        var line = analogy.Geometry.CenterLine;
        if (line.Count < 2) return 0.0;
        return Math.Atan2(line[^1].V - line[0].V, line[^1].U - line[0].U) * 180.0 / Math.PI;
    }

    /// <summary>Блокирующие диагностики несовпадения осей и слоёв под углом; пусто — оси согласованы.</summary>
    public static IReadOnlyList<FemValidationDiagnostic> Check(PlateStripBeamAnalogy analogy, StripRebarSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        var result = new List<FemValidationDiagnostic>();

        double delta = (StripAxisDeg(analogy) - sample.RebarAxisDeg) * Math.PI / 180.0;
        if (Math.Abs(Math.Sin(delta)) >= AlignedSinTolerance)
            result.Add(new("plate_strip_rebar_axis_mismatch",
                $"Ось полосы повёрнута на {delta * 180.0 / Math.PI:F1}° относительно оси армирования плиты: " +
                "расчёт полосы под углом к осям армирования пока не поддержан (нужна ось полосы вдоль оси x армирования)."));

        if (sample.Layers.Any(l => (l.Asx > 0.0 || l.Asy > 0.0) && Math.Abs(l.Angle) > 1e-9))
            result.Add(new("plate_strip_rebar_layer_angle",
                "Есть слои армирования, заданные под углом к осям плиты: расчёт полосы их направление не учитывает."));

        return result;
    }
}
