namespace CScore.Fem.Fragment;

/// <summary>Перекрытия для фрагмента вокруг стен и колонн (индексы в списке уровней <see cref="StoreyFloorLevels.Find"/>).</summary>
/// <param name="Available">Перекрытия, которые можно выбрать крайними i…k, по возрастанию отметки.</param>
/// <param name="Default">Перекрытие по умолчанию (одно — ближайшее к середине элементов); null — перекрытий нет.</param>
/// <param name="FullFrom">Нижнее перекрытие «вся высота»; null — перекрытий нет.</param>
/// <param name="FullTo">Верхнее перекрытие «вся высота»; null — перекрытий нет.</param>
public sealed record ElementFragmentHeight(IReadOnlyList<int> Available, int? Default, int? FullFrom, int? FullTo);

/// <summary>
/// Высота фрагмента вокруг стен и колонн (спека <c>2026-10-30-fem-element-fragment-prism-design.md</c>, разд. 2.2):
/// доступные перекрытия — задевающие призму в плане на высоте элементов и по одному соседнему ниже и выше; «вся высота» —
/// перекрытия на низе и верху элементов, иначе ближайшие за ними. Чистый класс, без БД.
/// </summary>
public static class ElementFragmentLevels
{
    /// <summary>Перекрытия для элементов с отметками <paramref name="elemZMin"/>…<paramref name="elemZMax"/> в призме.</summary>
    public static ElementFragmentHeight Of(IReadOnlyList<StoreyFloorLevel> levels, FragmentPrism prism, double elemZMin, double elemZMax)
    {
        ArgumentNullException.ThrowIfNull(levels);
        ArgumentNullException.ThrowIfNull(prism);
        const double tol = StoreyFloorLevels.LevelToleranceM;
        var corners = prism.Corners();
        var covering = Enumerable.Range(0, levels.Count).Where(i => StoreyFloorLevels.Covers(levels[i], corners)).ToList();
        var inside = covering.Where(i => levels[i].Z >= elemZMin - tol && levels[i].Z <= elemZMax + tol).ToList();
        int? below = covering.Where(i => levels[i].Z < elemZMin - tol).Select(i => (int?)i).LastOrDefault();
        int? above = covering.Where(i => levels[i].Z > elemZMax + tol).Select(i => (int?)i).FirstOrDefault();
        var available = new List<int>();
        if (below is int b) available.Add(b);
        available.AddRange(inside);
        if (above is int a) available.Add(a);
        if (available.Count == 0) return new([], null, null, null);

        double mid = (elemZMin + elemZMax) / 2;
        int def = available.OrderBy(i => Math.Abs(levels[i].Z - mid)).ThenBy(i => i).First();
        int? AtZ(double z) => inside.Where(i => Math.Abs(levels[i].Z - z) <= tol)
            .OrderBy(i => Math.Abs(levels[i].Z - z)).Select(i => (int?)i).FirstOrDefault();
        int from = AtZ(elemZMin) ?? below ?? available[0];
        int to = AtZ(elemZMax) ?? above ?? available[^1];
        return new(available, def, from, Math.Max(from, to));
    }

    /// <summary>
    /// Отметки разрезов для перекрытий <paramref name="i"/>…<paramref name="k"/>: z₁ — середина этажа под i, z₂ — над k
    /// (соседа нет — край схемы с запасом, как у <see cref="StoreyFloorLevels.Band"/>).
    /// </summary>
    public static (double Z1, double Z2) Range(IReadOnlyList<StoreyFloorLevel> levels, int i, int k, double schemaZMin, double schemaZMax)
    {
        ArgumentNullException.ThrowIfNull(levels);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(i, k);
        return (StoreyFloorLevels.Band(levels, i, schemaZMin, schemaZMax).Z1, StoreyFloorLevels.Band(levels, k, schemaZMin, schemaZMax).Z2);
    }

    /// <summary>«Вся высота» без перекрытий: низ и верх элементов с запасом <see cref="StoreyFloorLevels.EdgeMarginM"/>.</summary>
    public static (double Z1, double Z2) FullHeightNoFloors(double elemZMin, double elemZMax) =>
        (elemZMin - StoreyFloorLevels.EdgeMarginM, elemZMax + StoreyFloorLevels.EdgeMarginM);
}
