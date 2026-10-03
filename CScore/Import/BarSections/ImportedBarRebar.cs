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
/// огибающая по сечениям КЭ с округлением площадей вверх до <see cref="SelectedAreaStepCm2"/>; заданное SCAD —
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

    /// <summary>Подбор ЛИРЫ (*.asp): огибающая по сечениям КЭ, площади вверх до шага.</summary>
    public static (ImportedBarRebarLayout? Layout, string? Reason) LiraSelected(LiraAspBar? bar, LiraBarProfile profile)
    {
        if (bar == null) return (null, "КЭ нет в файле подбора ASP");
        for (int i = 0; i < bar.Sections.Count; i++)
            if (bar.Sections[i].Areas.FailureCode is int code)
                return (null, $"подбор ЛИРЫ в сечении {i + 1} не выполнен (код {code})");

        var e = bar.Sections.Count > 0 ? Envelope(bar.Sections.Select(s => s.Areas)) : bar.Envelope;
        var areas = e with
        {
            Au1 = CeilArea(e.Au1), Au2 = CeilArea(e.Au2), Au3 = CeilArea(e.Au3), Au4 = CeilArea(e.Au4),
            As1 = CeilArea(e.As1), As2 = CeilArea(e.As2), As3 = CeilArea(e.As3), As4 = CeilArea(e.As4),
        };
        if (areas.LongitudinalSum <= 0) return (null, "по подбору ЛИРЫ продольная арматура не требуется");
        var (bars, reason) = LiraBarSectionBuilder.SelectedLayout(areas, profile, bar.Covers);
        return bars == null ? (null, reason) : (new ImportedBarRebarLayout(bars, "ASP"), null);
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
        ScadSelectedBar? bar, ScadConcreteGroup? group, LiraBarProfile profile)
    {
        if (bar == null) return (null, "КЭ нет в подборе SCAD");
        if (group == null) return (null, "КЭ нет в ЖБ-группах SCAD — привязки арматуры неизвестны");
        int failed = bar.Sections.ToList().FindIndex(s => s is not { IsComplete: true });
        if (failed >= 0) return (null, $"подбор SCAD в сечении {failed + 1} не выполнен");
        if (bar.Envelope is not { IsComplete: true } e) return (null, "подбор SCAD для КЭ не выполнен");

        var areas = e with
        {
            As1 = CeilArea(e.As1!.Value), As2 = CeilArea(e.As2!.Value),
            As3 = CeilArea(e.As3!.Value), As4 = CeilArea(e.As4!.Value),
        };
        if (!(areas.LongitudinalSum > 0)) return (null, "по подбору SCAD продольная арматура не требуется");
        var (a1, a2) = ScadConcreteGroupIndex.BarCovers(group);
        var (bars, reason) = ScadBarSectionBuilder.SelectedLayout(areas, profile, a1, a2);
        return bars == null ? (null, reason) : (new ImportedBarRebarLayout(bars, "подбор SCAD"), null);
    }

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
