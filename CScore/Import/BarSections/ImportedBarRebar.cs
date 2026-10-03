namespace CScore.Import;

/// <summary>Армирование создаваемого ЖБ-сечения стержня по данным схемы-источника.</summary>
public enum ImportedBarRebarMode
{
    /// <summary>Без арматуры: только форма и материалы.</summary>
    None,
    /// <summary>Заданное: ТЗА ЛИРЫ (.RBT), заданное армирование SCAD (.SPR).</summary>
    Assigned,
    /// <summary>Подобранное: ASP ЛИРЫ, выгрузка плагина SCAD.</summary>
    Selected,
}

/// <summary>
/// Подобранные площади КЭ (см², округлены вверх до шага) до раскладки: КЭ одной формы, материалов и привязок
/// <paramref name="Covers"/> можно унифицировать (<see cref="ImportedBarRebar.Cluster{T}"/>) и разложить общую огибающую.
/// </summary>
/// <param name="Values">Компоненты: AU1..AU4, AS1..AS4 ЛИРЫ либо S1..S4 SCAD.</param>
/// <param name="Covers">Привязки арматуры (текст для группировки).</param>
/// <param name="Layout">Раскладка по площадям того же порядка компонент.</param>
public sealed record ImportedSelectedAreas(double[] Values, string Covers,
    Func<double[], (ImportedBarRebarLayout? Layout, string? Reason)> Layout);

/// <summary>Стержни армирования КЭ в осях сечения (x ‖ Y1, y ‖ Z1) и подпись источника для имени сечения.</summary>
/// <param name="Bars">Стержни.</param>
/// <param name="Label">Источник: «ASP», «ТЗА 3», «подбор SCAD», «SCAD «Ригели»».</param>
public sealed record ImportedBarRebarLayout(IReadOnlyList<LiraBarPoint> Bars, string Label)
{
    /// <summary>Суммарная площадь, см².</summary>
    public double TotalAreaCm2 => Bars.Sum(b => b.AreaM2) * 1e4;
}

/// <summary>
/// Армирование одного сечения проекта на весь КЭ (решения пользователя 03.10, спека §5.1): подобранное —
/// огибающая по сечениям КЭ с округлением площадей вверх до <see cref="SelectedAreaStepCm2"/>, затем унификация КЭ
/// с допуском перерасхода (<see cref="Cluster{T}"/>); заданное SCAD —
/// участок с наибольшей продольной; ТЗА ЛИРЫ по длине КЭ постоянны. Раскладка — та же, что у источников проверки
/// по КЭ «Подобранное/Заданное» (<see cref="LiraBarSectionBuilder"/>, <see cref="ScadBarSectionBuilder"/>).
/// </summary>
public static class ImportedBarRebar
{
    /// <summary>Шаг округления подобранных площадей, см²: КЭ с близким подбором получают одно сечение.</summary>
    public const double SelectedAreaStepCm2 = 0.5;

    /// <summary>Площадь, округлённая вверх до <see cref="SelectedAreaStepCm2"/>; неположительная — 0.</summary>
    public static double CeilArea(double cm2) =>
        cm2 > 0 ? Math.Ceiling(cm2 / SelectedAreaStepCm2 - 1e-9) * SelectedAreaStepCm2 : 0;

    /// <summary>
    /// Допуск унификации подбора по умолчанию (доля): ΣAs общего сечения превышает подбор любого его КЭ не более
    /// чем на 20 % (решение пользователя 03.10; на схеме 6-k1 — 219 сечений вместо 595).
    /// </summary>
    public const double DefaultSelectedTolerance = 0.2;

    /// <summary>Подбор ЛИРЫ (*.asp): огибающая по сечениям КЭ, площади вверх до шага.</summary>
    public static (ImportedBarRebarLayout? Layout, string? Reason) LiraSelected(LiraAspBar? bar, LiraBarProfile profile) =>
        Layout(LiraSelectedAreas(bar, profile));

    /// <summary>Площади подбора ЛИРЫ для КЭ (AU1..AU4, AS1..AS4): огибающая по сечениям, вверх до шага.</summary>
    public static (ImportedSelectedAreas? Areas, string? Reason) LiraSelectedAreas(LiraAspBar? bar, LiraBarProfile profile)
    {
        if (bar == null) return (null, "КЭ нет в файле подбора ASP");
        for (int i = 0; i < bar.Sections.Count; i++)
            if (bar.Sections[i].Areas.FailureCode is int code)
                return (null, $"подбор ЛИРЫ в сечении {i + 1} не выполнен (код {code})");

        var e = bar.Sections.Count > 0 ? Envelope(bar.Sections.Select(s => s.Areas)) : bar.Envelope;
        double[] a = [.. new[] { e.Au1, e.Au2, e.Au3, e.Au4, e.As1, e.As2, e.As3, e.As4 }.Select(CeilArea)];
        if (a.Sum() <= 0) return (null, "по подбору ЛИРЫ продольная арматура не требуется");
        var covers = bar.Covers;
        return (new ImportedSelectedAreas(a, string.Join("/", covers.A1, covers.A2, covers.A3), v =>
        {
            var areas = e with { Au1 = v[0], Au2 = v[1], Au3 = v[2], Au4 = v[3], As1 = v[4], As2 = v[5], As3 = v[6], As4 = v[7] };
            var (bars, reason) = LiraBarSectionBuilder.SelectedLayout(areas, profile, covers);
            return bars == null ? (null, reason) : (new ImportedBarRebarLayout(bars, "ASP"), null);
        }), null);
    }

    /// <summary>ТЗА ЛИРЫ (.RBT), назначенные КЭ (<paramref name="cell"/> — номера ТЗА КЭ).</summary>
    public static (ImportedBarRebarLayout? Layout, string? Reason) LiraAssigned(
        LiraRbtFile? rbt, string? cell, LiraBarProfile profile)
    {
        if (rbt == null) return (null, "у схемы нет файла ТЗА (RBT)");
        cell = cell?.Trim() ?? "";
        if (cell.Length == 0) return (null, "КЭ не назначены ТЗА");
        var (types, reason) = LiraAssignedBarSectionSource.ParseTypes(rbt, cell);
        if (types == null) return (null, reason);
        var (bars, layoutReason) = LiraBarSectionBuilder.AssignedLayout(types, profile);
        return bars == null ? (null, layoutReason) : (new ImportedBarRebarLayout(bars, "ТЗА " + cell), null);
    }

    /// <summary>Подбор SCAD (выгрузка плагина): огибающая по сечениям КЭ, площади вверх до шага; привязки — ЖБ-группы.</summary>
    public static (ImportedBarRebarLayout? Layout, string? Reason) ScadSelected(
        ScadSelectedBar? bar, ScadConcreteGroup? group, LiraBarProfile profile) =>
        Layout(ScadSelectedAreas(bar, group, profile));

    /// <summary>Площади подбора SCAD для КЭ (S1..S4): огибающая по сечениям, вверх до шага.</summary>
    public static (ImportedSelectedAreas? Areas, string? Reason) ScadSelectedAreas(
        ScadSelectedBar? bar, ScadConcreteGroup? group, LiraBarProfile profile)
    {
        if (bar == null) return (null, "КЭ нет в подборе SCAD");
        if (group == null) return (null, "КЭ нет в ЖБ-группах SCAD — привязки арматуры неизвестны");
        int failed = bar.Sections.ToList().FindIndex(s => s is not { IsComplete: true });
        if (failed >= 0) return (null, $"подбор SCAD в сечении {failed + 1} не выполнен");
        if (bar.Envelope is not { IsComplete: true } e) return (null, "подбор SCAD для КЭ не выполнен");

        double[] a = [CeilArea(e.As1!.Value), CeilArea(e.As2!.Value), CeilArea(e.As3!.Value), CeilArea(e.As4!.Value)];
        if (!(a.Sum() > 0)) return (null, "по подбору SCAD продольная арматура не требуется");
        var (a1, a2) = ScadConcreteGroupIndex.BarCovers(group);
        return (new ImportedSelectedAreas(a, string.Join("/", a1, a2), v =>
        {
            var areas = e with { As1 = v[0], As2 = v[1], As3 = v[2], As4 = v[3] };
            var (bars, reason) = ScadBarSectionBuilder.SelectedLayout(areas, profile, a1, a2);
            return bars == null ? (null, reason) : (new ImportedBarRebarLayout(bars, "подбор SCAD"), null);
        }), null);
    }

    /// <summary>
    /// Унификация подбора: КЭ объединяются в группы, сечение группы — покомпонентный максимум площадей её КЭ,
    /// ΣAs которого превышает ΣAs любого КЭ группы не более чем в (1 + <paramref name="tolerance"/>) раз; армирование
    /// КЭ поэтому не меньше подобранного. Жадно: КЭ по убыванию ΣAs, каждый — в группу с наименьшей ΣAs после
    /// присоединения либо в новую. Допуск 0 объединяет только равные наборы.
    /// </summary>
    /// <param name="items">КЭ и их площади (векторы одной длины, порядок компонент общий).</param>
    /// <param name="tolerance">Допустимый перерасход, доля (0,2 — 20 %).</param>
    public static List<(double[] Areas, List<T> Members)> Cluster<T>(
        IEnumerable<(T Item, double[] Areas)> items, double tolerance)
    {
        var clusters = new List<(double[] Areas, List<T> Members)>();
        // По убыванию ΣAs: присоединяемый КЭ — наименьший в группе, проверять допуск достаточно по нему.
        foreach (var (item, a) in items.OrderByDescending(x => x.Areas.Sum()))
        {
            double limit = (1 + Math.Max(0, tolerance)) * a.Sum() + 1e-9;
            int best = -1;
            double[]? bestAreas = null;
            for (int i = 0; i < clusters.Count; i++)
            {
                double[] joined = [.. clusters[i].Areas.Zip(a, Math.Max)];
                double sum = joined.Sum();
                if (sum <= limit && (bestAreas == null || sum < bestAreas.Sum() - 1e-9))
                    (best, bestAreas) = (i, joined);
            }
            if (best < 0) clusters.Add((a, [item]));
            else
            {
                clusters[best].Members.Add(item);
                clusters[best] = (bestAreas!, clusters[best].Members);
            }
        }
        return clusters;
    }

    static (ImportedBarRebarLayout? Layout, string? Reason) Layout((ImportedSelectedAreas? Areas, string? Reason) selected) =>
        selected.Areas is { } s ? s.Layout(s.Values) : (null, selected.Reason);

    /// <summary>Заданное армирование SCAD: участок с наибольшей продольной; привязки — ЖБ-группы.</summary>
    public static (ImportedBarRebarLayout? Layout, string? Reason) ScadAssigned(
        ScadAssignedRod? rod, ScadConcreteGroup? group, LiraBarProfile profile)
    {
        if (rod == null) return (null, "КЭ нет в группах заданного армирования SCAD");
        if (group == null) return (null, "КЭ нет в ЖБ-группах SCAD — привязки арматуры неизвестны");
        if (rod.Strongest is not { } part) return (null, $"в группе заданного армирования SCAD {rod.Num} нет участков");

        var (a1, a2) = ScadConcreteGroupIndex.BarCovers(group);
        var (bars, reason) = ScadBarSectionBuilder.AssignedLayout(part, profile, a1, a2);
        if (bars == null) return (null, reason);
        string label = rod.Name.Length > 0 ? $"SCAD «{rod.Name}»" : $"SCAD {rod.Num}";
        if (rod.Parts.Length > 1) label += $" уч.{part.PartNo}";
        return (new ImportedBarRebarLayout(bars, label), null);
    }

    static LiraAspBarAreas Envelope(IEnumerable<LiraAspBarAreas> sections)
    {
        var list = sections.ToList();
        double Max(Func<LiraAspBarAreas, double> f) => list.Max(f);
        return new LiraAspBarAreas(Max(a => a.Au1), Max(a => a.Au2), Max(a => a.Au3), Max(a => a.Au4),
            Max(a => a.As1), Max(a => a.As2), Max(a => a.As3), Max(a => a.As4),
            Max(a => a.Percent), Max(a => a.Asw1), Max(a => a.Asw2));
    }
}
