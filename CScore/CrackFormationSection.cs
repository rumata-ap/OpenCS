namespace CScore;

/// <summary>
/// Сечение для расчёта образования трещин по НДМ (п. 6.1.24 СП 63): НДС сжатого и растянутого бетона —
/// по трёхлинейной диаграмме; двухлинейная допускается только для растянутого бетона при упругом сжатом.
/// Двухлинейная диаграмма области сечения (L2) сжатый бетон упругим не оставляет — её приведённый модуль
/// E_b,red заметно меньше E_b, и момент трещинообразования занижается (прямоугольник 250×600 B25:
/// 50,9 кН·м против 58,4 кН·м по трёхлинейной). Поэтому для Mcrc бетонные области с L2 переводятся на L3;
/// раскрытие трещин (п. 6.1.26) и остальные расчёты считаются по диаграмме области, как задано.
/// </summary>
public static class CrackFormationSection
{
    /// <summary>Нужна ли замена диаграммы: есть бетонная область с двухлинейной диаграммой.</summary>
    public static bool NeedsThreeLinear(CrossSection section) =>
        section.Areas.Any(IsTwoLinearConcrete);

    /// <summary>
    /// Сечение для образования трещин: само <paramref name="section"/>, если двухлинейного бетона нет,
    /// иначе копия, в которой бетон L2 переведён на L3 и его диаграммы перестроены. Исходное сечение
    /// не меняется.
    /// </summary>
    /// <param name="rebarDifferentialDiagram">Режим разностной диаграммы арматуры, как в расчёте: задан —
    /// арматура в переведённом бетоне перестраивается от его трёхлинейной диаграммы; null — диаграммы
    /// арматуры остаются прежними (режим, в котором их строили, неизвестен).</param>
    public static CrossSection For(CrossSection section, double sp63EtaMin = 0.85,
        IReadOnlyList<Diagramm>? pool = null, bool? rebarDifferentialDiagram = null, double ekbEtaMin = 0.05)
    {
        ArgumentNullException.ThrowIfNull(section);
        if (!NeedsThreeLinear(section)) return section;
        var clone = section.CloneForCalc();
        // CloneForCalc сохраняет ссылки HostArea на области оригинала — перепривязываем к копиям.
        var map = new Dictionary<MaterialArea, MaterialArea>(ReferenceEqualityComparer.Instance);
        for (int i = 0; i < section.Areas.Count && i < clone.Areas.Count; i++)
            map[section.Areas[i]] = clone.Areas[i];
        var switched = new HashSet<MaterialArea>(ReferenceEqualityComparer.Instance);
        foreach (var area in clone.Areas)
        {
            if (area.HostArea != null && map.TryGetValue(area.HostArea, out var host)) area.HostArea = host;
            if (IsTwoLinearConcrete(area)) { area.DiagrammType = DiagrammType.L3; switched.Add(area); }
        }
        try
        {
            // Сначала бетон, затем арматура в нём (разностная диаграмма берёт диаграммы бетона-носителя).
            foreach (var area in switched)
                area.ResolveAndBuildDiagramms(sp63EtaMin, pool, rebarDifferentialDiagram ?? true, ekbEtaMin);
            if (rebarDifferentialDiagram is { } diff)
                foreach (var area in clone.Areas.Where(a => a.HostArea != null && switched.Contains(a.HostArea)))
                    if (MaterialArea.IsCalcActive(area))
                        area.ResolveAndBuildDiagramms(sp63EtaMin, pool, diff, ekbEtaMin);
        }
        catch (ArgumentException)
        {
            // Характеристик бетона не хватает для трёхлинейной диаграммы (неполный пользовательский
            // материал) — образование трещин по диаграмме области, как раньше.
            return section;
        }
        return clone;
    }

    static bool IsTwoLinearConcrete(MaterialArea area) =>
        area.Material?.Type == MatType.Concrete && area.DiagrammType == DiagrammType.L2;
}
