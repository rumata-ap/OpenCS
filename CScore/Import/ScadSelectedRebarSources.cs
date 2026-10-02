using System.Globalization;
using CScore.Fem;
using CScore.PlateRebar;

namespace CScore.Import;

/// <summary>
/// Подобранное SCAD армирование пластин (выгрузка плагина) как источник мозаики (тег КЭ = номер КЭ SCAD).
/// AS1..AS4 — низ X1, верх X1, низ Y1, верх Y1; поперечная — max(ASWx, ASWy). Null (NaN у SCAD) — «нет данных».
/// </summary>
public sealed class ScadSelectedPlateRebarSource(ScadSelectedRebarFile file) : IPlateRebarFieldSource
{
    /// <inheritdoc/>
    public bool Supports(PlateRebarMosaicComponent component) => true;

    /// <inheritdoc/>
    public PlateRebarValue Get(string elemTag, PlateRebarMosaicComponent component)
    {
        if (!int.TryParse(elemTag.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int id)
            || !file.Plates.TryGetValue(id, out var p))
            return PlateRebarValue.Missing;
        double? v = component switch
        {
            PlateRebarMosaicComponent.BottomX => p.As1,
            PlateRebarMosaicComponent.TopX => p.As2,
            PlateRebarMosaicComponent.BottomY => p.As3,
            PlateRebarMosaicComponent.TopY => p.As4,
            _ => p.AswX == null && p.AswY == null ? null : Math.Max(p.AswX ?? 0, p.AswY ?? 0),
        };
        return v is double d ? PlateRebarValue.Of(d) : PlateRebarValue.Missing;
    }
}

/// <summary>
/// Подобранное SCAD армирование стержней (выгрузка плагина) как источник мозаики и эпюры (тег КЭ = номер КЭ SCAD).
/// AS1..AS4 — площади граней (S1 низ и S2 верх — с угловыми), <see cref="BarRebarComponent.Asw1"/> — IWz
/// (ветви ‖ Z1), <see cref="BarRebarComponent.Asw2"/> — IWy, см²/м. Угловых, рядов заданного армирования
/// и процента у SCAD нет.
/// </summary>
public sealed class ScadSelectedBarRebarSource(ScadSelectedRebarFile file) : IBarRebarFieldSource
{
    /// <summary>В выгрузке есть стержни.</summary>
    public bool HasBars => file.Bars.Count > 0;

    /// <inheritdoc/>
    public bool Supports(BarRebarComponent component) =>
        component is BarRebarComponent.LongitudinalSum or BarRebarComponent.As1 or BarRebarComponent.As2
            or BarRebarComponent.As3 or BarRebarComponent.As4 or BarRebarComponent.Asw1 or BarRebarComponent.Asw2;

    /// <inheritdoc/>
    public PlateRebarValue Get(string elemTag, BarRebarComponent component) =>
        Supports(component) && Find(elemTag) is { } bar ? Value(bar.Envelope, component) : PlateRebarValue.Missing;

    /// <inheritdoc/>
    public IReadOnlyList<PlateRebarValue> GetSections(string elemTag, BarRebarComponent component) =>
        Supports(component) && Find(elemTag) is { } bar
            ? bar.Sections.Select(s => Value(s, component)).ToList() : [];

    ScadSelectedBar? Find(string elemTag) =>
        int.TryParse(elemTag.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int id)
        && file.Bars.TryGetValue(id, out var bar) ? bar : null;

    static PlateRebarValue Value(ScadSelectedBarSection? s, BarRebarComponent component)
    {
        if (s == null) return PlateRebarValue.Missing;
        double? v = component switch
        {
            BarRebarComponent.LongitudinalSum => s.LongitudinalSum,
            BarRebarComponent.As1 => s.As1,
            BarRebarComponent.As2 => s.As2,
            BarRebarComponent.As3 => s.As3,
            BarRebarComponent.As4 => s.As4,
            BarRebarComponent.Asw1 => s.IwZ,
            _ => s.IwY,
        };
        return v is double d ? PlateRebarValue.Of(d) : PlateRebarValue.Missing;
    }
}
