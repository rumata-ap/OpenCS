using System.Globalization;
using CScore.Fem;
using CScore.PlateRebar;

namespace CScore.Import;

/// <summary>
/// Заданное в SCAD армирование пластин как источник мозаики (тег КЭ = номер КЭ SCAD): S1..S4 — см²/м,
/// поперечная — см²/м². КЭ вне групп заданного армирования — «нет данных».
/// </summary>
public sealed class ScadAssignedPlateRebarSource(ScadAssignedRebarFile file) : IPlateRebarFieldSource
{
    /// <inheritdoc/>
    public bool Supports(PlateRebarMosaicComponent component) => true;

    /// <inheritdoc/>
    public PlateRebarValue Get(string elemTag, PlateRebarMosaicComponent component)
    {
        if (!int.TryParse(elemTag.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int id)
            || file.Plate(id) is not { } p)
            return PlateRebarValue.Missing;
        return PlateRebarValue.Of(component switch
        {
            PlateRebarMosaicComponent.BottomX => p.Area(0),
            PlateRebarMosaicComponent.TopX => p.Area(1),
            PlateRebarMosaicComponent.BottomY => p.Area(2),
            PlateRebarMosaicComponent.TopY => p.Area(3),
            _ => p.TransverseArea,
        });
    }
}

/// <summary>
/// Заданное в SCAD армирование стержней как источник мозаики и эпюры (тег КЭ = номер КЭ SCAD). Величины —
/// как у подбора SCAD: площади граней S1..S4 и сумма, см²; <see cref="BarRebarComponent.Asw1"/> — поперечная
/// в плоскости Z (ветви ‖ Z1), <see cref="BarRebarComponent.Asw2"/> — в плоскости Y, см²/м.
/// Значение на КЭ — наименьшее по участкам; по сечениям — участок, в который попадает сечение.
/// </summary>
/// <param name="file">Заданное армирование схемы.</param>
/// <param name="sectionCount">Число сечений КЭ (из подбора или усилий); null — неизвестно: по одному значению
/// при одном участке, иначе три сечения.</param>
public sealed class ScadAssignedBarRebarSource(ScadAssignedRebarFile file, Func<int, int?>? sectionCount = null)
    : IBarRebarFieldSource
{
    const int DefaultSections = 3;

    /// <summary>В схеме есть группы заданного армирования стержней.</summary>
    public bool HasBars => file.Rods.Count > 0;

    /// <inheritdoc/>
    public bool Supports(BarRebarComponent component) =>
        component is BarRebarComponent.LongitudinalSum or BarRebarComponent.As1 or BarRebarComponent.As2
            or BarRebarComponent.As3 or BarRebarComponent.As4 or BarRebarComponent.Asw1 or BarRebarComponent.Asw2;

    /// <inheritdoc/>
    public PlateRebarValue Get(string elemTag, BarRebarComponent component) =>
        Supports(component) && Find(elemTag) is { Parts.Length: > 0 } rod
            ? PlateRebarValue.Of(rod.Parts.Min(p => Value(p, component)))
            : PlateRebarValue.Missing;

    /// <inheritdoc/>
    public IReadOnlyList<PlateRebarValue> GetSections(string elemTag, BarRebarComponent component)
    {
        if (!Supports(component) || !int.TryParse(elemTag.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int id)
            || file.Rod(id) is not { Parts.Length: > 0 } rod)
            return [];
        int n = sectionCount?.Invoke(id) is int c and > 0 ? c : rod.Parts.Length == 1 ? 1 : DefaultSections;
        return Enumerable.Range(1, n)
            .Select(k => PlateRebarValue.Of(Value(rod.PartAt(k, n)!, component)))
            .ToList();
    }

    /// <summary>
    /// Число сечений стержневых КЭ: из подбора SCAD (выгрузка плагина), иначе — наибольший номер сечения
    /// в строках усилий наборов.
    /// </summary>
    public static Func<int, int?> SectionCounts(ScadSelectedRebarFile? selected, IEnumerable<ForceSet>? forceSets = null)
    {
        Dictionary<int, int>? fromForces = null;
        return id =>
        {
            if (selected != null && selected.Bars.TryGetValue(id, out var bar) && bar.Sections.Count > 0)
                return bar.Sections.Count;
            if (forceSets == null) return null;
            fromForces ??= forceSets.SelectMany(s => s.Items)
                .Where(i => i.SourceElementNum != null && i.SourceSectionNum != null)
                .GroupBy(i => i.SourceElementNum!.Value)
                .ToDictionary(g => g.Key, g => g.Max(i => i.SourceSectionNum!.Value));
            return fromForces.TryGetValue(id, out int n) ? n : null;
        };
    }

    ScadAssignedRod? Find(string elemTag) =>
        int.TryParse(elemTag.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int id) ? file.Rod(id) : null;

    /// <summary>Величина компоненты на участке.</summary>
    public static double Value(ScadAssignedRodPart p, BarRebarComponent component) => component switch
    {
        BarRebarComponent.LongitudinalSum => p.LongitudinalSum,
        BarRebarComponent.As1 => p.S1.AreaCm2,
        BarRebarComponent.As2 => p.S2.AreaCm2,
        BarRebarComponent.As3 => p.S3?.AreaCm2 ?? 0,
        BarRebarComponent.As4 => p.S4?.AreaCm2 ?? 0,
        BarRebarComponent.Asw1 => p.StirrupsZ?.AreaPerMeter ?? 0,
        _ => p.StirrupsY?.AreaPerMeter ?? 0,
    };
}
