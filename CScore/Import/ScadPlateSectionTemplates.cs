using System.Globalization;
using CScore.Fem;

namespace CScore.Import;

/// <summary>
/// Сечение-шаблон пластинчатой цели по данным SCAD: толщина — своя толщина КЭ (из жёсткости), классы бетона
/// и арматуры — из ЖБ-группы КЭ, слои — условная арматура на привязках ЖБ-группы (a1/a3 — низ, a2/a4 — верх;
/// нулевые a3/a4 — как a1/a2). Шаблон даёт проверке бетон, арматурную сталь и диаметры; армирование
/// отдельного КЭ подставляет <see cref="ScadSelectedPlateSectionSource"/>.
/// </summary>
public static class ScadPlateSectionTemplates
{
    /// <summary>Типовой диаметр условной арматуры для запроса, м.</summary>
    public const double DefaultDiameterM = 0.012;

    /// <summary>Собрать шаблон цели.</summary>
    /// <param name="plates">Пластинчатые КЭ цели.</param>
    /// <param name="groups">ЖБ-группы схемы.</param>
    /// <param name="diameterM">Диаметр условной арматуры, м; null — не запрошен.</param>
    public static LiraPlateTemplateResult Build(
        IReadOnlyList<FemCheckScopeElement> plates, ScadConcreteGroupIndex groups, double? diameterM)
    {
        var suggested = new LiraPlateNominalRebar(0, DefaultDiameterM);
        var combos = new Dictionary<LiraPlateCombo, Dictionary<int, int>>();
        foreach (var e in plates)
        {
            if (e.ElemNum is not int num || groups.Find(num) is not { } g) continue;
            if (e.Element.ThicknessM is not (> 0 and var h)) continue;
            var combo = new LiraPlateCombo(Math.Round(h, 4), g.ConcreteClass.Trim(), g.LongitudinalRebarClass.Trim());
            if (!combos.TryGetValue(combo, out var byGroup)) combos[combo] = byGroup = [];
            byGroup[g.Num] = byGroup.GetValueOrDefault(g.Num) + 1;
        }
        if (combos.Count == 0)
            return new(null, false, suggested, "КЭ цели не входят в ЖБ-группы SCAD или у них не задана толщина");

        var ordered = combos.Select(c => (Combo: c.Key, Count: c.Value.Values.Sum(), Groups: c.Value))
            .OrderByDescending(c => c.Count).ThenBy(c => c.Combo.ThicknessM).ToList();
        var main = ordered[0];
        if (main.Combo.ConcreteClass.Length == 0 || main.Combo.RebarClass.Length == 0)
            return new(null, false, suggested, "в ЖБ-группе SCAD не указаны классы бетона и арматуры");

        var group = groups.Groups.First(g => g.Num == main.Groups.OrderByDescending(kv => kv.Value).First().Key);
        var (a1, a2, a3, a4) = ScadConcreteGroupIndex.PlateCovers(group);
        double t = main.Combo.ThicknessM;
        if (!(a1 > 0 && a2 > 0 && a3 > 0 && a4 > 0) || a1 + a2 >= t || a3 + a4 >= t)
            return new(null, false, suggested, string.Format(CultureInfo.InvariantCulture,
                "привязки ЖБ-группы SCAD {0} ({1:0.#}/{2:0.#}/{3:0.#}/{4:0.#} мм) не помещаются в толщину {5:0.#} мм",
                group.Num, a1 * 1000, a2 * 1000, a3 * 1000, a4 * 1000, t * 1000));

        if (diameterM is not (> 0 and var d))
            return new(null, true, suggested, diameterM == null ? null : "диаметр должен быть больше нуля");

        List<PlateRebarLayer> layers =
        [
            NominalLayer(+1, t, a2, a4, d),
            NominalLayer(-1, t, a1, a3, d),
        ];
        string tag = string.Format(CultureInfo.InvariantCulture, "SCAD {0} · ЖБ {1} · d{2:0.#}", main.Combo.Label, group.Num, d * 1000);
        return new(new LiraPlateTemplate(main.Combo, layers, tag, [], [], main.Count,
            [.. ordered.Skip(1).Select(c => (c.Combo, c.Count))]), false, suggested, null);
    }

    static PlateRebarLayer NominalLayer(int sign, double h, double coverX, double coverY, double d)
    {
        var layer = new PlateRebarLayer
        {
            Name = sign > 0 ? "Z+" : "Z-",
            InputMode = "diameter_spacing",
            DiameterX = d, DiameterY = d,
            SpacingX = LiraPlateNominalRebar.SpacingM, SpacingY = LiraPlateNominalRebar.SpacingM,
            Zsx = sign * (h / 2 - coverX), Zsy = sign * (h / 2 - coverY),
            Face = sign > 0 ? PlateRebar.RebarFace.PlusN : PlateRebar.RebarFace.MinusN,
        };
        layer.RecalcArea();
        return layer;
    }
}
