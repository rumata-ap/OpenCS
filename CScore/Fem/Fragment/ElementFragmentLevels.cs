namespace CScore.Fem.Fragment;

/// <summary>Перекрытия для фрагмента вокруг стен и колонн (индексы в списке уровней <see cref="StoreyFloorLevels.Find"/>).</summary>
/// <param name="Available">Перекрытия, которые можно выбрать крайними i…k, по возрастанию отметки.</param>
/// <param name="DefaultFrom">Нижнее перекрытие по умолчанию: ближайшее к середине элементов, а у элементов на фундаментной плите
/// (перекрытие на низе элементов без этажа под ним) — эта плита; null — перекрытий нет.</param>
/// <param name="DefaultTo">Верхнее перекрытие по умолчанию: ближайшее к середине элементов, на фундаментной плите — не ниже
/// следующего над ней доступного; null — перекрытий нет.</param>
/// <param name="FullFrom">Нижнее перекрытие «вся высота»; null — перекрытий нет.</param>
/// <param name="FullTo">Верхнее перекрытие «вся высота»; null — перекрытий нет.</param>
public sealed record ElementFragmentHeight(IReadOnlyList<int> Available, int? DefaultFrom, int? DefaultTo, int? FullFrom, int? FullTo);

/// <summary>
/// Высота фрагмента вокруг стен и колонн (спека <c>2026-10-30-fem-element-fragment-prism-design.md</c>, разд. 2.2):
/// доступные перекрытия — задевающие призму в плане на высоте элементов и по одному соседнему ниже и выше; «вся высота» —
/// перекрытия на низе и верху элементов, иначе ближайшие за ними. По умолчанию — одно перекрытие у середины элементов; элементы на
/// фундаментной плите — от плиты (решение пользователя 13.11: подвальную стену не резать посреди этажа). Чистый класс, без БД.
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
        if (available.Count == 0) return new([], null, null, null, null);

        double mid = (elemZMin + elemZMax) / 2;
        int def = available.OrderBy(i => Math.Abs(levels[i].Z - mid)).ThenBy(i => i).First();
        int? AtZ(double z) => inside.Where(i => Math.Abs(levels[i].Z - z) <= tol)
            .OrderBy(i => Math.Abs(levels[i].Z - z)).Select(i => (int?)i).FirstOrDefault();
        int? bottom = AtZ(elemZMin);
        int from = bottom ?? below ?? available[0];
        int to = AtZ(elemZMax) ?? above ?? available[^1];

        // Элементы стоят на фундаментной плите (на низе элементов перекрытие, под ним в призме перекрытий нет): умолчание — от
        // плиты и не ниже следующего над ней перекрытия, иначе разрез посреди нижнего этажа отсекает плиту. Этаж ниже — именно
        // в призме: подвал в другой части здания (W62 «Музея», плита −3,400 вне призмы) стену не делает «надподвальной».
        int defFrom = def, defTo = def;
        if (bottom is int f && below is null)
        {
            int pos = available.IndexOf(f);
            defFrom = f;
            defTo = Math.Max(def, pos + 1 < available.Count ? available[pos + 1] : f);
        }
        return new(available, defFrom, defTo, from, Math.Max(from, to));
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
